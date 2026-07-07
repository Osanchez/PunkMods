using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using Com.LuisPedroFonseca.ProCamera2D;
using HarmonyLib;
using UnityEngine;

namespace PunkCombatTweaksExtended
{
    /// <summary>
    /// A bundle of small combat feel tweaks, each toggled from the in-game Mods Menu. Toggles are
    /// framed as removals: ON = the effect is removed/suppressed, OFF = the game's stock behavior.
    /// Every toggle defaults OFF, so with nothing changed the game plays exactly as shipped.
    ///   - Remove Slow-Mo on Damage: suppresses the brief slow-motion when a player ship takes damage.
    ///   - Remove Screen Shake: suppresses all camera screen shake (shooting, damage, death, level start).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.combattweaksextended";
        public const string Name = "PUNK Combat Tweaks Extended";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        // ON = remove (suppress) the damage slow-mo. OFF = game default (slow-mo plays).
        internal static BepInEx.Configuration.ConfigEntry<bool> RemoveSlowMo;
        // ON = remove (suppress) all camera screen shake. OFF = game default (shake plays).
        internal static BepInEx.Configuration.ConfigEntry<bool> RemoveScreenShake;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            RemoveSlowMo = cfg.Bind("General", "RemoveSlowMoOnDamage", false,
                "Remove the brief slow-motion when a player takes damage. ON = removed (full speed); OFF = game default.");
            RemoveScreenShake = cfg.Bind("General", "RemoveScreenShake", false,
                "Remove camera screen shake (shooting, damage, death, level start). ON = removed (no shake); OFF = game default.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ModMenuBridge.AddToggle("Remove Slow-Mo on Damage", () => RemoveSlowMo.Value, v => RemoveSlowMo.Value = v);
            ModMenuBridge.AddToggle("Remove Screen Shake", () => RemoveScreenShake.Value, v => RemoveScreenShake.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. Remove slow-mo {(RemoveSlowMo.Value ? "ON" : "OFF")}, " +
                        $"remove screen shake {(RemoveScreenShake.Value ? "ON" : "OFF")}.");
        }

        // Hot-reload teardown: undo the Harmony patches and the Mods-menu rows.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            try { ModMenuBridge.RemoveAll(); } catch { }
        }
    }

    // The damage slow-mo is the only TimeManager modifier whose owner is a Ship (Ship.OnDamage /
    // OnShieldDamage). Skip just that one when the toggle is off; leave all other modifiers alone.
    [HarmonyPatch(typeof(TimeManager), nameof(TimeManager.AddModifier))]
    internal static class SuppressDamageSlowMo
    {
        private static bool Prefix(object owner)
        {
            if (Plugin.RemoveSlowMo != null && Plugin.RemoveSlowMo.Value && owner is Ship)
                return false;   // remove: skip applying the slow-mo modifier
            return true;
        }
    }

    // All of PUNK's one-shot camera shakes (ShipCameraShaker shoot/damage/death, ShakeOnStart) funnel
    // through ProCamera2DShake.Instance.Shake(...). The preset/index/name overloads all delegate to
    // this core overload, so suppressing it here kills every screen shake in one patch when the toggle
    // is off. (ConstantShake is a separate path and is intentionally left alone — out of scope.)
    [HarmonyPatch(typeof(ProCamera2DShake), nameof(ProCamera2DShake.Shake),
        new Type[] { typeof(float), typeof(Vector2), typeof(int), typeof(float),
                     typeof(float), typeof(Vector3), typeof(float), typeof(bool) })]
    internal static class SuppressScreenShake
    {
        private static bool Prefix()
        {
            if (Plugin.RemoveScreenShake != null && Plugin.RemoveScreenShake.Value)
                return false;   // remove: skip applying the shake
            return true;
        }
    }
}
