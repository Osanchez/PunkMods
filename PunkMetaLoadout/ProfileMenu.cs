using System.Linq;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Registers the PROFILES section in the Mods menu. Profile selection/creation now happens on
    /// the player-select screen (the ready-up overlay), so the menu only keeps profile DELETION:
    /// pick a profile and delete it. All via the soft ModMenuBridge (no-ops if the menu isn't installed).
    /// </summary>
    internal static class ProfileMenu
    {
        private static int _deleteTarget;

        internal static void Register()
        {
            ModMenuBridge.AddList("Delete which", () => ProfileStore.Profiles.ToList(), ClampTarget, idx => _deleteTarget = idx);
            ModMenuBridge.AddAction("Delete Profile", "DELETE", DeleteSelected);
        }

        private static int ClampTarget()
        {
            int count = ProfileStore.Profiles.Count;
            if (count == 0) return 0;
            _deleteTarget = System.Math.Min(System.Math.Max(_deleteTarget, 0), count - 1);
            return _deleteTarget;
        }

        private static void DeleteSelected()
        {
            var profs = ProfileStore.Profiles.ToList();
            if (_deleteTarget >= 0 && _deleteTarget < profs.Count)
                ProfileStore.DeleteProfile(profs[_deleteTarget]);
            _deleteTarget = 0;
        }
    }
}
