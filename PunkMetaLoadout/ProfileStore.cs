using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Sirenix.Serialization;
using UnityEngine;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Named profiles + which profile each player slot uses. A profile holds that person's builds
    /// per class (profiles/&lt;name&gt;/&lt;class&gt;.json) ; the shared vault persists per class
    /// (vault_&lt;class&gt;.json). P1-P4 -> profile assignments and the profile list persist in
    /// profiles.json. All under &lt;persistentDataPath&gt;/meta_loadouts/.
    /// </summary>
    internal static class ProfileStore
    {
        internal static string Root => Path.Combine(Application.persistentDataPath, "meta_loadouts");
        private static string ProfilesDir => Path.Combine(Root, "profiles");
        private static string MetaFile => Path.Combine(Root, "profiles.json");

        public class Persist
        {
            public List<string> profiles = new List<string>();
            public string[] slots = new string[4];   // index 0..3 = P1..P4 -> profile name or null
        }

        private static Persist _data = new Persist();
        private static bool _loaded;

        internal static IReadOnlyList<string> Profiles { get { EnsureLoaded(); return _data.profiles; } }

        internal static string GetSlot(int slot)          // slot 0..3
        {
            EnsureLoaded();
            return (slot >= 0 && slot < 4) ? _data.slots[slot] : null;
        }

        internal static void SetSlot(int slot, string profile)
        {
            EnsureLoaded();
            if (slot < 0 || slot >= 4) return;
            // A profile belongs to one player at a time: clear it from any other slot.
            if (!string.IsNullOrEmpty(profile))
                for (int i = 0; i < 4; i++) if (i != slot && _data.slots[i] == profile) _data.slots[i] = null;
            _data.slots[slot] = string.IsNullOrEmpty(profile) ? null : profile;
            SaveMeta();
        }

        internal static string CreateProfile()
        {
            EnsureLoaded();
            int n = 1;
            while (_data.profiles.Contains($"Profile {n}")) n++;
            return CreateProfile($"Profile {n}");
        }

        /// <summary>Create a profile with the given (cleaned, de-duplicated) display name.</summary>
        internal static string CreateProfile(string desired)
        {
            EnsureLoaded();
            string clean = CleanName(desired);
            if (string.IsNullOrEmpty(clean)) return CreateProfile();
            string name = UniqueName(clean);
            _data.profiles.Add(name);
            try { Directory.CreateDirectory(ProfileDir(name)); } catch { }
            SaveMeta();
            return name;
        }

        /// <summary>Rename a profile (its build folder + any slot assignments move with it).</summary>
        internal static void Rename(string oldName, string desired)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(oldName) || !_data.profiles.Contains(oldName)) return;
            string clean = CleanName(desired);
            if (string.IsNullOrEmpty(clean) || clean == oldName) return;
            string nn = UniqueName(clean);
            try
            {
                var od = ProfileDir(oldName); var nd = ProfileDir(nn);
                if (Directory.Exists(od)) { if (Directory.Exists(nd)) Directory.Delete(nd, true); Directory.Move(od, nd); }
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profile rename move failed: {e.Message}"); }
            int idx = _data.profiles.IndexOf(oldName);
            if (idx >= 0) _data.profiles[idx] = nn;
            for (int i = 0; i < 4; i++) if (_data.slots[i] == oldName) _data.slots[i] = nn;
            SaveMeta();
        }

        private static string ProfileDir(string profile) => Path.Combine(ProfilesDir, profile);

        private static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = new string(s.Where(c => !Path.GetInvalidFileNameChars().Contains(c)).ToArray()).Trim();
            return s.Length > 24 ? s.Substring(0, 24) : s;
        }

        private static string UniqueName(string b)
        {
            if (!_data.profiles.Contains(b)) return b;
            for (int i = 2; ; i++) { var n = $"{b} {i}"; if (!_data.profiles.Contains(n)) return n; }
        }

        internal static void DeleteProfile(string name)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(name)) return;
            _data.profiles.Remove(name);
            for (int i = 0; i < 4; i++) if (_data.slots[i] == name) _data.slots[i] = null;
            try { var d = Path.Combine(ProfilesDir, name); if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            SaveMeta();
        }

        /// <summary>Clear all P1-P4 -> profile assignments (keeps the profiles themselves).</summary>
        internal static void ClearSlots()
        {
            EnsureLoaded();
            bool any = false;
            for (int i = 0; i < 4; i++) if (_data.slots[i] != null) { _data.slots[i] = null; any = true; }
            if (any) SaveMeta();
        }

        internal static void ClearAll()
        {
            try { if (Directory.Exists(Root)) Directory.Delete(Root, true); } catch { }
            _data = new Persist();
            SaveMeta();
        }

        // ---- per-profile build + shared per-class vault file paths ----
        internal static string GridFile(string profile, string cls)
            => Path.Combine(ProfilesDir, profile, $"{cls}.json");
        internal static string VaultFile(string cls)
            => Path.Combine(Root, $"vault_{cls}.json");

        // ---- persistence of the profile list + slot assignments ----
        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (File.Exists(MetaFile))
                    _data = SerializationUtility.DeserializeValue<Persist>(File.ReadAllBytes(MetaFile), DataFormat.JSON) ?? new Persist();
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profiles load failed: {e.Message}"); _data = new Persist(); }
            if (_data.slots == null || _data.slots.Length != 4) _data.slots = new string[4];
            if (_data.profiles == null) _data.profiles = new List<string>();
            // drop slot assignments to profiles that no longer exist
            for (int i = 0; i < 4; i++) if (_data.slots[i] != null && !_data.profiles.Contains(_data.slots[i])) _data.slots[i] = null;
        }

        private static void SaveMeta()
        {
            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(MetaFile, SerializationUtility.SerializeValue(_data, DataFormat.JSON));
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profiles save failed: {e.Message}"); }
        }
    }

    /// <summary>
    /// Public surface other mods (e.g. PunkFourPlayer's ready-up overlay) call via reflection,
    /// so there's no hard assembly dependency. Mirrors the internal ProfileStore operations.
    /// </summary>
    public static class ProfileApi
    {
        public static List<string> List() => ProfileStore.Profiles.ToList();
        public static string GetSlot(int slot) => ProfileStore.GetSlot(slot);
        public static void SetSlot(int slot, string profile) => ProfileStore.SetSlot(slot, profile);
        public static void ClearSlots() => ProfileStore.ClearSlots();
        public static string Create(string name) => ProfileStore.CreateProfile(name);
        public static void Rename(string oldName, string newName) => ProfileStore.Rename(oldName, newName);
        public static void Delete(string name) => ProfileStore.DeleteProfile(name);
    }
}
