using System;

namespace PunkMinionTuning
{
    /// <summary>Soft (reflection) hook into the optional PunkModsMenu framework — no hard dependency.</summary>
    internal static class ModMenuBridge
    {
        private static readonly Type T = Type.GetType("PunkModsMenu.ModMenu, PunkModsMenu");
        public static bool Available => T != null;

        public static void AddToggle(string label, Func<bool> get, Action<bool> set)
        {
            try { T?.GetMethod("AddToggle")?.Invoke(null, new object[] { label, get, set }); }
            catch { }
        }

        // Hot-reload teardown: drop the rows this mod registered so a reload doesn't stack duplicates.
        public static void RemoveAll()
        {
            try { T?.GetMethod("RemoveByCaller")?.Invoke(null, null); }
            catch { }
        }
    }
}
