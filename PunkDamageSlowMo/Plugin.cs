using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;

namespace PunkDamageSlowMo
{
    /// <summary>
    /// Toggles the brief slow-motion the game plays whenever a player ship takes damage. The game's
    /// own behavior is ON (slow-mo plays), so that's the default; turn it OFF to keep combat at full
    /// speed. Only the ship-damage slow-mo is affected — the damage SFX and every other time-scale
    /// effect (consumables, etc.) are untouched.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.damageslowmo";
        public const string Name = "PUNK Damage Slow-Mo";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        // ON = slow-mo plays on damage (the game's default behavior). OFF = suppressed.
        internal static BepInEx.Configuration.ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Enabled = cfg.Bind("General", "Enabled", true,
                "Play the brief slow-motion when a player takes damage. ON = game default; OFF = full speed.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            ModMenuBridge.AddToggle("Slow-Mo on Damage", () => Enabled.Value, v => Enabled.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. Damage slow-mo {(Enabled.Value ? "ON" : "OFF")}.");
        }

        // Hot-reload teardown: undo the Harmony patch and the Mods-menu row.
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
            if (Plugin.Enabled != null && !Plugin.Enabled.Value && owner is Ship)
                return false;   // skip applying the slow-mo modifier
            return true;
        }
    }
}
