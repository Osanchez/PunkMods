using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace PunkSeedPicker
{
    /// <summary>
    /// Adds a seed screen between picking your class and world generation: shows the run's seed,
    /// lets you edit it (or paste one to share/replay a world), then START generates with it. Poked
    /// into the existing flow by intercepting RunSetupScreen.StartGame -> GameScene.GoToGameScene.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.seedpicker";
        public const string Name = "PUNK Seed Picker";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            var cfg = new BepInEx.Configuration.ConfigFile(Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Enabled = cfg.Bind("General", "Enabled", true,
                "Show the seed-entry screen after choosing a class on a new run.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);

            ModMenuBridge.AddToggle("Seed Picker", () => Enabled.Value, v => Enabled.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded." + (ModMenuBridge.Available ? " | Mods-menu toggle registered." : ""));
        }

        // Hot-reload teardown: undo the StartGame/seed-screen patches and the Mods-menu row. The seed
        // screen itself is transient (opened only during run setup, not tracked here), so nothing else
        // to destroy.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            try { ModMenuBridge.RemoveAll(); } catch { }
        }
    }

    // Intercept the moment after the class is chosen, before the game scene loads. Show the seed
    // screen and skip the immediate start; the screen's START calls GoToGameScene itself.
    [HarmonyPatch(typeof(RunSetupScreen), "StartGame")]
    internal static class StartGamePatch
    {
        private static readonly System.Reflection.FieldInfo ArgsF = AccessTools.Field(typeof(RunSetupScreen), "arguments");

        private static bool Prefix(RunSetupScreen __instance)
        {
            try
            {
                if (!Plugin.Enabled.Value) return true;
                var args = (RunArguments)ArgsF.GetValue(__instance);
                if (args.isContinue) return true;          // resuming a save: keep its world, no seed screen
                SeedScreen.Open(args, __instance);
                return false;                              // don't start yet — the screen will
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"seed screen failed; starting normally: {e.Message}");
                return true;
            }
        }
    }
}
