using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;
using UnityEngine.InputSystem;

namespace PunkDebugKey
{
    /// <summary>
    /// Opens PUNK's built-in developer debug menu with <b>F1</b>.
    ///
    /// v2 design (crash-safe): a Harmony postfix on <c>DebugMenu.Update()</c> detects F1 via
    /// the new Input System and replays the game's own "open" branch. It deliberately does NOT
    /// mutate the game's InputActions and does NOT call SetActive — both of those (done during
    /// scene load in v1) hard-crashed the game on this Unity 6 build. This runs only while the
    /// menu's own Update runs (i.e. it's active) and only on an actual F1 keypress, fully
    /// guarded by try/catch. Esc-to-close keeps working because the game's own Update handles
    /// it off the <c>isOpened</c> flag we set.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.debugkey";
        public const string Name = "PUNK Debug Menu Key (F1)";
        public const string Version = "2.1.0";

        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            // Default ON; but if the Mods menu framework is installed, default OFF so the user
            // explicitly opts in via its toggle. (This default only applies on first run; the
            // saved value persists afterward.)
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Enabled = cfg.Bind("General", "Enabled", !ModMenuBridge.Available,
                "Enable the F1 developer debug menu. Defaults ON, or OFF when the Mods menu is installed.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(DebugMenuPatch));
            ModMenuBridge.AddToggle("Debug Menu (F1)", () => Enabled.Value, v => Enabled.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. F1 debug menu {(Enabled.Value ? "ON" : "OFF")}." +
                        (ModMenuBridge.Available ? " Toggle it in the Mods menu." : ""));
        }

        // Hot-reload teardown: undo the DebugMenu.Update postfix and the Mods-menu row.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            try { ModMenuBridge.RemoveAll(); } catch { }
        }
    }

    [HarmonyPatch(typeof(DebugMenu), "Update")]
    internal static class DebugMenuPatch
    {
        // Private members of DebugMenu we need to read/replay its open sequence.
        private static readonly FieldInfo IsOpenedF   = AccessTools.Field(typeof(DebugMenu), "isOpened");
        private static readonly FieldInfo ScreenF     = AccessTools.Field(typeof(DebugMenu), "screen");
        private static readonly FieldInfo TimeMgrF    = AccessTools.Field(typeof(DebugMenu), "timeManager");
        private static readonly FieldInfo WeaponDropF = AccessTools.Field(typeof(DebugMenu), "weaponDropdown");
        private static readonly MethodInfo SetHoverM  = AccessTools.Method(typeof(DebugMenu), "SetShipsHovering");

        private static bool _warned;

        // Change KeyControl here to rebind, e.g. Keyboard.current.f3Key / backquoteKey.
        private static bool OpenPressed()
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value) return false;   // disabled in config / Mods menu
            var kb = Keyboard.current;
            return kb != null && kb.f1Key.wasPressedThisFrame;
        }

        private static void Postfix(DebugMenu __instance)
        {
            if (!OpenPressed()) return;

            try
            {
                // Already open? leave it (F1 opens, Esc closes — same split the game uses).
                if (IsOpenedF == null || (bool)IsOpenedF.GetValue(__instance)) return;

                // Replay DebugMenu.Update()'s open branch exactly, so the game's own Close()
                // path (slow-mo removal, re-enable ship control, un-hover) reverses it cleanly.
                IsOpenedF.SetValue(__instance, true);
                ServiceLocator.Get<ShipManager>()?.DisableShipControl();
                SetHoverM?.Invoke(__instance, new object[] { true });
                (TimeMgrF?.GetValue(__instance) as TimeManager)?.SetTimeScale(0.1f, __instance);
                (ScreenF?.GetValue(__instance) as UIScreen)?.Open();
                (WeaponDropF?.GetValue(__instance) as WeaponDropdown)?.Refresh();

                Plugin.Log.LogInfo("Opened debug menu via F1.");
            }
            catch (Exception e)
            {
                if (!_warned) { Plugin.Log.LogWarning($"F1 open failed (Ctrl+Alt+D still works): {e}"); _warned = true; }
            }
        }
    }
}
