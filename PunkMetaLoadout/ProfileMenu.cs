using System.Collections.Generic;
using System.Linq;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Registers the PROFILES section in the Mods menu (via the soft ModMenuBridge — no-ops if the menu
    /// isn't installed). Profile selection/creation happens on the player-select screen (the ready-up
    /// overlay); this menu holds:
    ///   - "Keep Across Runs" scroll toggle: Ship / Vault / Ship + Vault (default) — what persists.
    ///   - a shared "Profile" selector plus CLEAR (reset that profile's saved builds) and DELETE.
    /// </summary>
    internal static class ProfileMenu
    {
        // Display order for the keep-mode scroll toggle. Index here != stored value (stored 0 = Ship +
        // Vault so old saves default correctly), so map between the two.
        private static readonly List<string> KeepModeOptions = new List<string> { "Ship", "Vault", "Ship + Vault" };
        private static readonly int[] IndexToMode = { 1, 2, 0 };   // display 0/1/2 -> stored Ship/Vault/Ship+Vault

        private static int _target;   // selected profile index, shared by CLEAR and DELETE

        internal static void Register()
        {
            ModMenuBridge.AddList("Keep Across Runs", () => KeepModeOptions, GetKeepIndex, SetKeepIndex);
            ModMenuBridge.AddList("Profile", () => ProfileStore.Profiles.ToList(), ClampTarget, idx => _target = idx);
            ModMenuBridge.AddAction("Clear Profile", "CLEAR", ClearSelected);   // reset the selected profile's builds
            ModMenuBridge.AddAction("Delete Profile", "DELETE", DeleteSelected);
        }

        // ---- keep-across-runs mode ----
        private static int GetKeepIndex()
        {
            int mode = ProfileStore.GetKeepMode();
            int i = System.Array.IndexOf(IndexToMode, mode);
            return i < 0 ? 2 : i;   // fall back to "Ship + Vault"
        }

        private static void SetKeepIndex(int i)
        {
            if (i < 0 || i >= IndexToMode.Length) return;
            ProfileStore.SetKeepMode(IndexToMode[i]);
        }

        // ---- profile selector shared by CLEAR + DELETE ----
        private static int ClampTarget()
        {
            int count = ProfileStore.Profiles.Count;
            if (count == 0) return 0;
            _target = System.Math.Min(System.Math.Max(_target, 0), count - 1);
            return _target;
        }

        private static void ClearSelected()
        {
            var profs = ProfileStore.Profiles.ToList();
            if (_target >= 0 && _target < profs.Count)
                ProfileStore.ClearProfile(profs[_target]);   // keep the profile, wipe its saved builds
        }

        private static void DeleteSelected()
        {
            var profs = ProfileStore.Profiles.ToList();
            if (_target >= 0 && _target < profs.Count)
                ProfileStore.DeleteProfile(profs[_target]);
            _target = 0;
        }
    }
}
