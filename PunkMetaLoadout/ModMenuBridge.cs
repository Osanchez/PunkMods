using System;
using System.Collections.Generic;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Soft hook into the optional PunkModsMenu framework. Uses reflection so there's NO hard
    /// dependency: if the menu plugin isn't installed, these calls quietly do nothing and the mod
    /// keeps working (you'd clear progress by deleting the JSON file yourself).
    /// </summary>
    internal static class ModMenuBridge
    {
        private static readonly Type T = Type.GetType("PunkModsMenu.ModMenu, PunkModsMenu");

        public static bool Available => T != null;

        public static void AddButton(string label, string neutralLabel, string actionLabel, Action action, bool confirm, string confirmText)
        {
            try { T?.GetMethod("AddButton")?.Invoke(null, new object[] { label, neutralLabel, actionLabel, action, confirm, confirmText }); }
            catch { /* menu absent or signature changed - ignore */ }
        }

        public static void AddToggle(string label, Func<bool> get, Action<bool> set)
        {
            try { T?.GetMethod("AddToggle")?.Invoke(null, new object[] { label, get, set }); }
            catch { }
        }

        public static void AddAction(string label, string actionLabel, Action action)
        {
            try { T?.GetMethod("AddAction")?.Invoke(null, new object[] { label, actionLabel, action }); }
            catch { }
        }

        public static void AddList(string label, Func<List<string>> options, Func<int> getIndex, Action<int> setIndex)
        {
            try { T?.GetMethod("AddList")?.Invoke(null, new object[] { label, options, getIndex, setIndex }); }
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
