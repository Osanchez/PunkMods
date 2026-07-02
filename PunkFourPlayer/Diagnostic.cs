using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PunkFourPlayer
{
    /// <summary>
    /// DIAGNOSTIC (read-only): dumps the co-op run-setup / input-assignment UI — full hierarchy
    /// with RectTransform anchors/pivot/position/size and text alignment — so layout issues (e.g. a
    /// misaligned header) can be diagnosed and fixed precisely. Changes nothing in-game.
    /// </summary>
    internal static class UiDump
    {
        internal static void Write(string file, Transform root, string note)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"=== {note} ===");
                sb.AppendLine("(parent: " + (root.parent != null ? root.parent.name : "<none>") + ")");
                Dump(root, 0, sb);
                var path = Path.Combine(Application.persistentDataPath, file);
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo($"UI dumped to: {path}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"dump failed: {e}"); }
        }

        private static void Dump(Transform t, int depth, StringBuilder sb)
        {
            var comps = t.GetComponents<Component>().Select(c => c == null ? "<missing>" : c.GetType().Name);
            sb.Append(new string(' ', depth * 2)).Append(t.name);
            if (!t.gameObject.activeSelf) sb.Append(" (inactive)");
            sb.Append("  [").Append(string.Join(", ", comps)).Append(']');

            if (t is RectTransform rt)
                sb.Append($"  | aMin({F(rt.anchorMin)}) aMax({F(rt.anchorMax)}) piv({F(rt.pivot)}) pos({F(rt.anchoredPosition)}) size({F(rt.sizeDelta)})");

            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var ct = c.GetType();
                var textP = ct.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (textP != null && textP.PropertyType == typeof(string))
                {
                    var v = textP.GetValue(c) as string;
                    if (!string.IsNullOrEmpty(v)) sb.Append("  text=\"").Append(v).Append('"');
                }
                var alignP = ct.GetProperty("alignment", BindingFlags.Instance | BindingFlags.Public);
                if (alignP != null) { try { sb.Append("  align=").Append(alignP.GetValue(c)); } catch { } }
            }
            sb.AppendLine();
            for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, sb);
        }

        private static string F(Vector2 v) => $"{v.x:0.##},{v.y:0.##}";
    }

    [HarmonyPatch(typeof(RunSetupScreen), "OnEnable")]
    internal static class RunSetupDump
    {
        private static bool _done;
        private static void Postfix(RunSetupScreen __instance)
        {
            if (_done) return; _done = true;
            UiDump.Write("run_setup_dump.txt", __instance.transform, "RunSetupScreen (full run-setup root)");
        }
    }

    [HarmonyPatch(typeof(InputSelectorScreen), "OnEnable")]
    internal static class InputSelectorDump
    {
        private static bool _done;
        private static void Postfix(InputSelectorScreen __instance)
        {
            if (_done) return; _done = true;
            // dump from one level up so any sibling header is captured too
            var root = __instance.transform.parent != null ? __instance.transform.parent : __instance.transform;
            UiDump.Write("input_selector_dump.txt", root, "InputSelectorScreen area (parent of the input screen)");
        }
    }
}
