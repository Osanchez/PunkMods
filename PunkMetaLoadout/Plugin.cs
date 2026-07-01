using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;
using Sirenix.Serialization;         // Odin — the game's own serializer; supports DataFormat.JSON
using UnityEngine;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Roguelite meta-progression: persists each player's ship build across death, keyed by
    /// (class, player slot). The class is the starting loadout chosen for the run, the slot is the
    /// player position (P1..P4) — so P1's "Tank" build is separate from P1's "Cannon" build, and
    /// from P2's "Tank" build. The shared vault (spare modules/ingredients/consumables) persists per
    /// class. Files live under <persistentDataPath>/meta_loadouts/ as Odin JSON.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.metaloadout";
        public const string Name = "PUNK Meta Loadout (persistent build)";
        public const string Version = "2.0.0";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

            GameController.LevelGenerated += MetaLoadout.OnRunReady;
            GameController.GameOver       += MetaLoadout.OnGameOver;

            // PROFILES section (P1-P4 selectors + per-profile create/delete) in the Mods menu, IF
            // that framework is installed (else these quietly no-op). Deletion is one-at-a-time.
            ProfileMenu.Register();

            Log.LogInfo($"{Name} v{Version} loaded. Saves at: {MetaLoadout.Dir}" +
                        (ModMenuBridge.Available ? " | Mods-menu row registered." : " | no Mods menu — clear by deleting that folder."));
        }
    }

    internal static class MetaLoadout
    {
        internal static string Dir => ProfileStore.Root;

        private static string _runClass = "default";
        private static bool _isContinue;
        internal static bool Suppressed;   // set by ClearProgress; cleared on next run

        private static readonly List<(ModuleGrid grid, Action<Module> handler)> _gridSubs = new List<(ModuleGrid, Action<Module>)>();
        private static Vault _vault;
        private static Action<Ingredient, int, int> _ingH;
        private static Action<int> _conH;
        internal static Vault RestoredVault;

        private static string Sanitize(string s)
            => string.Join("_", (string.IsNullOrEmpty(s) ? "default" : s).Split(Path.GetInvalidFileNameChars()));

        /// <summary>Mods-menu / public API: wipe all profiles + saved builds.</summary>
        public static void ClearProgress()
        {
            ProfileStore.ClearAll();
            Suppressed = true;
            Plugin.Log?.LogInfo("All profiles + loadouts cleared.");
        }

        // ---------- run wiring ----------

        internal static void OnRunReady(Level level)
        {
            try
            {
                Suppressed = false;
                var args = GameScene.arguments;
                _isContinue = args.isContinue;
                _runClass = Sanitize(args.startingLoadout != null ? args.startingLoadout.name : "default");

                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships == null) return;

                // New runs: each player loads the build from THEIR assigned profile (for this class);
                // "No Profile" players keep the fresh starting loadout. Shared vault loads per class.
                if (!_isContinue)
                {
                    for (int i = 0; i < ships.Count; i++) RestoreShip(ships[i], i + 1);
                    RestoreVault();
                }

                Resubscribe(ships);
                SaveAll(ships);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"OnRunReady failed: {e}"); }
        }

        private static void RestoreShip(Ship ship, int slot)   // slot is 1-based
        {
            var profile = ProfileStore.GetSlot(slot - 1);
            if (string.IsNullOrEmpty(profile)) return;          // No Profile -> fresh starting loadout
            if (!(ship?.ModuleGridOwner?.ModuleGrid is ModuleGrid grid)) return;
            var mem = LoadGrid(ProfileStore.GridFile(profile, _runClass));
            if (mem == null) return;                            // profile has no build for this class yet
            grid.RestoreFromMemento(mem);
            try { ship.Unit.ComponentData.RecalculateStats(grid); } catch { }
            Plugin.Log.LogInfo($"Restored P{slot} from profile '{profile}' (class '{_runClass}').");
        }

        private static void RestoreVault()
        {
            var vault = ServiceLocator.Get<Vault>();
            if (vault == null || ReferenceEquals(vault, RestoredVault)) return;
            var mem = LoadVault(ProfileStore.VaultFile(_runClass));
            if (mem != null) { vault.RestoreFromMemento(mem); RestoredVault = vault; }
        }

        private static void Resubscribe(System.Collections.Generic.IReadOnlyList<Ship> ships)
        {
            foreach (var (g, h) in _gridSubs) { if (g != null) { g.ModuleInstalled -= h; g.ModuleUninstalled -= h; } }
            _gridSubs.Clear();
            for (int i = 0; i < ships.Count; i++)
            {
                int slot = i + 1;
                if (!(ships[i]?.ModuleGridOwner?.ModuleGrid is ModuleGrid grid)) continue;
                Action<Module> h = _ => SaveGrid(grid, slot);
                grid.ModuleInstalled += h;
                grid.ModuleUninstalled += h;
                _gridSubs.Add((grid, h));
            }

            var vault = ServiceLocator.Get<Vault>();
            if (_vault != null) { _vault.IngredientAmountChanged -= _ingH; _vault.ConsumableAmountChanged -= _conH; }
            if (vault != null)
            {
                _ingH = (a, b, c) => SaveVault();
                _conH = _ => SaveVault();
                vault.IngredientAmountChanged += _ingH;
                vault.ConsumableAmountChanged += _conH;
                _vault = vault;
            }
        }

        // ---------- save / load ----------

        private static void SaveGrid(ModuleGrid grid, int slot)   // slot is 1-based
        {
            if (Suppressed || grid == null) return;
            var profile = ProfileStore.GetSlot(slot - 1);
            if (string.IsNullOrEmpty(profile)) return;            // No Profile -> nothing persists
            try
            {
                var f = ProfileStore.GridFile(profile, _runClass);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, SerializationUtility.SerializeValue(grid.CreateMemento(), DataFormat.JSON));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"SaveGrid P{slot} failed: {e.Message}"); }
        }

        internal static void SaveVault()
        {
            if (Suppressed) return;
            try
            {
                var vault = ServiceLocator.Get<Vault>();
                if (vault == null) return;
                var f = ProfileStore.VaultFile(_runClass);
                Directory.CreateDirectory(Path.GetDirectoryName(f));
                File.WriteAllBytes(f, SerializationUtility.SerializeValue(vault.CreateMemento(), DataFormat.JSON));
            }
            catch (Exception e) { Plugin.Log.LogWarning($"SaveVault failed: {e.Message}"); }
        }

        private static void SaveAll(System.Collections.Generic.IReadOnlyList<Ship> ships)
        {
            for (int i = 0; i < ships.Count; i++)
                if (ships[i]?.ModuleGridOwner?.ModuleGrid is ModuleGrid g) SaveGrid(g, i + 1);
            SaveVault();
        }

        internal static void OnGameOver()
        {
            var ships = ServiceLocator.Get<ShipManager>()?.Ships;
            if (ships != null) SaveAll(ships);
        }

        private static ModuleGrid.Memento LoadGrid(string f)
        {
            try { return File.Exists(f) ? SerializationUtility.DeserializeValue<ModuleGrid.Memento>(File.ReadAllBytes(f), DataFormat.JSON) : null; }
            catch (Exception e) { Plugin.Log.LogWarning($"LoadGrid failed: {e.Message}"); return null; }
        }

        private static Vault.Memento LoadVault(string f)
        {
            try { return File.Exists(f) ? SerializationUtility.DeserializeValue<Vault.Memento>(File.ReadAllBytes(f), DataFormat.JSON) : null; }
            catch (Exception e) { Plugin.Log.LogWarning($"LoadVault failed: {e.Message}"); return null; }
        }
    }

    // Vault.Store (module picked up into the stash) raises no event — patch it so stash pickups save too.
    [HarmonyPatch(typeof(Vault), nameof(Vault.Store))]
    internal static class VaultStorePatch
    {
        private static void Postfix() => MetaLoadout.SaveVault();
    }
}
