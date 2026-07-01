using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PunkFourPlayer
{
    /// <summary>
    /// Makes the co-op "ASSIGN INPUT" header dynamic: it shows one player column (P1..Pn) per
    /// connected controller, clamped to [2, PlayerCount]. Rebuilds whenever a device is added or
    /// removed. (Cosmetic for now — the device rows still slide between 2 positions; widening the
    /// row movement + start logic to N columns is the next step.)
    /// </summary>
    internal static class JoinHeader
    {
        // P1/P2 keep their vanilla colors; P3/P4 get these.
        private static readonly Color[] ExtraColors =
        {
            new Color(0.49f, 0.86f, 0.49f),   // P3 green
            new Color(0.78f, 0.55f, 0.92f),   // P4 purple
        };

        // Matches the vanilla P1/P2 horizontal offsets (±284 from the header center) so 2 columns
        // look identical to stock; 3-4 columns spread evenly across the same span.
        private const float SpanHalf = 284f;

        internal static void Rebuild(InputSelectorScreen screen)
        {
            try
            {
                var players = screen.transform.Find("Window/Players");
                if (players == null) return;

                var rows = (AccessTools.Field(typeof(InputSelectorScreen), "rows").GetValue(screen) as IEnumerable)
                    ?.Cast<InputSelectorDeviceRow>().Where(r => r != null).ToList() ?? new List<InputSelectorDeviceRow>();

                // Columns follow the number of controllers (gamepads); the keyboard is a spectator.
                int gamepadCount = rows.Count(r => r.Device is Gamepad);
                int n = Mathf.Clamp(gamepadCount, 2, Mathf.Clamp(Plugin.Target, 2, 4));

                // Capture each controller's CURRENT player choice BEFORE the layout changes, so we can
                // keep them on the same player (just re-spaced) rather than letting columns shift.
                var oldIndex = rows.ToDictionary(r => r, r => JoinLayout.PosToPlayerIndex(r.Position));

                JoinLayout.SetN(n);

                // Collect existing label objects (P1, P2, and any clones we made before).
                var labels = new List<RectTransform>();
                for (int i = 0; i < players.childCount; i++)
                {
                    var c = players.GetChild(i);
                    if (c.name.StartsWith("UI Inputselector Player")) labels.Add(c as RectTransform);
                }
                if (labels.Count == 0) return;   // template missing — bail

                // Grow: clone the first label until we have n.
                while (labels.Count < n)
                {
                    var clone = UnityEngine.Object.Instantiate(labels[0].gameObject, players);
                    clone.name = $"UI Inputselector Player (mod{labels.Count})";
                    labels.Add(clone.transform as RectTransform);
                }

                // With 3+ columns a middle label lands on the centered "ASSIGN INPUT" title, so
                // lift the title onto its own line above the column row. (2 columns => vanilla.)
                if (players.Find("Text") is RectTransform title)
                {
                    var tp = title.anchoredPosition;
                    tp.y = (n > 2) ? 130f : 4.12f;   // lift clear of the P# columns AND the profile tags above them
                    title.anchoredPosition = tp;
                }

                for (int i = 0; i < labels.Count; i++)
                {
                    var lbl = labels[i];
                    if (i >= n) { lbl.gameObject.SetActive(false); continue; }
                    lbl.gameObject.SetActive(true);

                    // uniform anchoring + even spread
                    lbl.anchorMin = new Vector2(0.5f, 1f);
                    lbl.anchorMax = new Vector2(0.5f, 1f);
                    lbl.pivot = new Vector2(0.5f, 0.5f);
                    lbl.anchoredPosition = new Vector2(JoinLayout.PlayerIndexToX(i), -50f);   // leaves the center free for unassigned

                    // P1/P2 keep their original colors; recolor only the extra columns.
                    SetLabel(lbl, $"P{i + 1}", i >= 2 ? (Color?)ExtraColors[Mathf.Min(i - 2, ExtraColors.Length - 1)] : null);
                    SetTag(lbl, i);   // assigned-profile name above the column
                }

                // Keep existing controllers on their chosen player (re-spaced for the new column
                // count); newly added controllers stay at the unassigned center.
                foreach (var row in rows)
                    DeviceRowSetPosition.RemapForNChange(row, oldIndex[row]);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"JoinHeader.Rebuild failed: {e.Message}"); }
        }

        private static readonly Color ProfileColor = new Color(0.92f, 0.66f, 0.27f, 1f);

        // Refresh just the assigned-profile name tags (called when the overlay closes, no relayout).
        internal static void UpdateProfileLabels(InputSelectorScreen screen)
        {
            try
            {
                var players = screen.transform.Find("Window/Players");
                if (players == null) return;
                int idx = 0;
                for (int i = 0; i < players.childCount; i++)
                {
                    var c = players.GetChild(i);
                    if (!c.name.StartsWith("UI Inputselector Player") || !c.gameObject.activeSelf) continue;
                    SetTag(c as RectTransform, idx);
                    idx++;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"profile tags failed: {e.Message}"); }
        }

        // The amber profile name shown above a P# column (blank when no profile is assigned).
        private static void SetTag(RectTransform lbl, int playerIndex)
        {
            if (lbl == null) return;
            var tag = lbl.Find("ModProfileTag") as RectTransform;
            if (tag == null)
            {
                var go = new GameObject("ModProfileTag", typeof(RectTransform));
                tag = go.GetComponent<RectTransform>();
                tag.SetParent(lbl, false);
                tag.anchorMin = tag.anchorMax = new Vector2(0.5f, 0.5f);
                tag.pivot = new Vector2(0.5f, 0.5f);
                tag.anchoredPosition = new Vector2(0f, 46f);   // above the P# text
                tag.sizeDelta = new Vector2(320f, 30f);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = UnityEngine.Object.FindObjectOfType<TMP_Text>()?.font;
                t.fontSize = 22; t.alignment = TextAlignmentOptions.Center;
                t.enableWordWrapping = false; t.color = ProfileColor;
            }
            var tmp = tag.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                var prof = ProfileBridge.GetSlot(playerIndex);
                tmp.text = string.IsNullOrEmpty(prof)
                    ? "<color=#7a7a82>NO PROFILE</color>"   // valid state: don't save this player's loadout
                    : prof.ToUpper();
            }
        }

        private static void SetLabel(Transform label, string text, Color? color)
        {
            var t = label.Find("Text");
            if (t == null) return;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var tp = c.GetType().GetProperty("text", typeof(string));
                if (tp != null && tp.CanWrite) tp.SetValue(c, text);
                if (color.HasValue)
                {
                    var cp = c.GetType().GetProperty("color", typeof(Color));
                    if (cp != null && cp.CanWrite) cp.SetValue(c, color.Value);
                }
            }
        }
    }

    [HarmonyPatch(typeof(InputSelectorScreen), "OnEnable")]
    internal static class JoinHeader_OnEnable
    {
        private static void Postfix(InputSelectorScreen __instance) => JoinHeader.Rebuild(__instance);
    }

    [HarmonyPatch(typeof(InputSelectorScreen), "AddDevice")]
    internal static class JoinHeader_AddDevice
    {
        private static void Postfix(InputSelectorScreen __instance) => JoinHeader.Rebuild(__instance);
    }

    [HarmonyPatch(typeof(InputSelectorScreen), "RemoveDevice")]
    internal static class JoinHeader_RemoveDevice
    {
        private static void Postfix(InputSelectorScreen __instance) => JoinHeader.Rebuild(__instance);
    }
}
