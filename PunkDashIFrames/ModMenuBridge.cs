using System;

namespace PunkDashIFrames
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
    }
}
