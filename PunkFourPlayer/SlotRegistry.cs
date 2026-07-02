using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Punk.SaveLoad;
using UnityEngine;

namespace PunkFourPlayer
{
    /// <summary>
    /// Persistent per-ship PLAYER SLOT (0-based: P1=0, P2=1, P3=2, P4=3).
    ///
    /// The problem it solves: every ship entity has a RANDOM instanceId
    /// (EntityManager.CreateInstanceId == rnd.Next(), persisted and restored verbatim), and
    /// ShipManager.SpawnShipGameObjects orders ships by "GetShips() orderby instanceId". On a NEW run
    /// the two vanilla ships are spawned BEFORE this mod places the extras (we run in a postfix), so
    /// the game only sorts the first two and the final order is [P1,P2, extras...]. On CONTINUE all
    /// four ships are already restored, so the game sorts ALL of them by their random ids at once —
    /// interleaving the extras among the primaries and mapping the same saved ships to DIFFERENT
    /// player numbers than the session that created the run. That scrambles which HUD, color, in-world
    /// "PN" label and controller each ship gets. Every identity source keys off the ShipManager.Ships
    /// index, so if we make that index equal the persistent slot, they can never disagree.
    ///
    /// Mechanism: a tiny sibling file ("fourplayer_slots.txt", one "instanceId,slot" per line) written
    /// into the run's save folder alongside the game's own files (Strategy 2 in docs/13). It is
    /// slot-scoped and auto-deleted with the save (GameSaver.DeleteSave removes the folder). We hook
    /// GameSaver.Save (write) and GameSaver.Load (read) — no engine memento layout is touched, so it
    /// can never corrupt the game's own saves.
    ///
    /// Legacy saves (created before this mod version) have no file: we fall back to the current
    /// instanceId-order assignment (today's behavior — functional, occasionally scrambled) and then
    /// persist the slots so every subsequent continue is stable. A missing/corrupt file never throws.
    /// </summary>
    internal static class SlotRegistry
    {
        private const string FileName = "fourplayer_slots.txt";

        // instanceId -> slot, read from the save on CONTINUE (empty on a new run / legacy save).
        private static readonly Dictionary<int, int> _loaded = new Dictionary<int, int>();
        // instanceId -> slot resolved for the CURRENT run; this is what gets persisted on save.
        private static readonly Dictionary<int, int> _current = new Dictionary<int, int>();

        // Clear any mapping carried over in memory from a previous run (Load isn't called on a new run,
        // so without this the previous continue's map would leak into a fresh run).
        internal static void ResetForNewRun()
        {
            _loaded.Clear();
            _current.Clear();
        }

        /// <summary>
        /// Assign a stable slot to every current ship. Ships whose instanceId is in the loaded map keep
        /// their remembered slot. Any unknown ship (a NEW run, or a legacy save with no slot file) keeps
        /// its CURRENT position in the ShipManager.Ships list — i.e. slot == the index the game/mod just
        /// produced. On a new run that index IS the authoritative as-created identity (P1,P2,extras...),
        /// so we record exactly that; on a legacy continue it equals today's behavior (functional, just
        /// occasionally scrambled) which we then persist so future continues are stable. Using list
        /// order (not a re-sort by instanceId) is essential: the new-run order is NOT globally sorted by
        /// id, so re-sorting here would record the wrong identity.
        /// The resolved map is stored in <see cref="_current"/> for persistence on the next save.
        /// </summary>
        internal static Dictionary<Ship, int> Resolve(IReadOnlyList<Ship> ships)
        {
            var result = new Dictionary<Ship, int>();
            var used = new HashSet<int>();

            int InstanceId(Ship s) { try { return s.SavableEntity.EntityData.instanceId; } catch { return int.MinValue; } }

            // Pass 1: honor remembered slots (ignore duplicates/out-of-range -> treat as unknown).
            foreach (var s in ships)
            {
                if (s == null) continue;
                int iid = InstanceId(s);
                if (_loaded.TryGetValue(iid, out var slot) && slot >= 0 && slot < ships.Count && used.Add(slot))
                    result[s] = slot;
            }

            // Pass 2: fill remaining ships into the lowest free slots in CURRENT LIST ORDER, so an
            // unmapped ship keeps the position the game/mod just gave it (the as-created identity).
            int next = 0;
            foreach (var s in ships)
            {
                if (s == null || result.ContainsKey(s)) continue;
                while (used.Contains(next)) next++;
                used.Add(next);
                result[s] = next;
            }

            // Record the resolved mapping for persistence.
            _current.Clear();
            foreach (var kv in result)
            {
                int iid = InstanceId(kv.Key);
                if (iid != int.MinValue) _current[iid] = kv.Value;
            }
            return result;
        }

        private static string PathFor(string folderName)
            => Path.Combine(Application.persistentDataPath, "saves", folderName, FileName);

        // Read the sibling file for a save folder into _loaded (called from the GameSaver.Load hook).
        private static void ReadFile(string folderName)
        {
            _loaded.Clear();
            try
            {
                var path = PathFor(folderName);
                if (!File.Exists(path)) { Plugin.Log.LogInfo($"[slots] no slot file for '{folderName}' (legacy save; will fall back to instanceId order)."); return; }
                foreach (var line in File.ReadAllLines(path))
                {
                    var parts = line.Split(',');
                    if (parts.Length == 2 && int.TryParse(parts[0], out var iid) && int.TryParse(parts[1], out var slot))
                        _loaded[iid] = slot;
                }
                Plugin.Log.LogInfo($"[slots] loaded {_loaded.Count} slot mapping(s) from '{folderName}'.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[slots] read failed ({folderName}); using instanceId fallback: {e.Message}"); _loaded.Clear(); }
        }

        // Write _current into the save folder (called from the GameSaver.Save hook).
        private static void WriteFile(string folderName)
        {
            try
            {
                if (_current.Count == 0) return;
                var dir = Path.Combine(Application.persistentDataPath, "saves", folderName);
                Directory.CreateDirectory(dir);   // GameSaver creates it async on a thread; be safe.
                File.WriteAllLines(Path.Combine(dir, FileName), _current.Select(kv => $"{kv.Key},{kv.Value}"));
                Plugin.Log.LogInfo($"[slots] wrote {_current.Count} slot mapping(s) to '{folderName}'.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[slots] write failed ({folderName}): {e.Message}"); }
        }

        // ---- persistence hooks (additive sibling file; never touches the game's own save data) ----

        [HarmonyPatch(typeof(GameSaver), "Load", new[] { typeof(string) })]
        internal static class Load_Patch
        {
            private static void Postfix(string folderName) => ReadFile(folderName);
        }

        [HarmonyPatch(typeof(GameSaver), "Save", new[] { typeof(string) })]
        internal static class Save_Patch
        {
            // Save(bool) routes through Save(string), so this covers both. Runs on the main thread.
            private static void Postfix(string folderName) => WriteFile(folderName);
        }
    }
}
