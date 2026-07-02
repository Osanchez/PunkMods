using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PunkFourPlayer
{
    /// <summary>
    /// Geometry + slot/player mapping for the N-column join screen. Position 0 is the unassigned
    /// CENTER (retained from vanilla); players occupy slots to the left and right of it so the
    /// center stays free. For N=2 this is exactly vanilla (P1 left, P2 right, unassigned center).
    ///   N=2: P1(-1)  [0]  P2(+1)
    ///   N=4: P1(-2) P2(-1) [0] P3(+1) P4(+2)
    /// </summary>
    internal static class JoinLayout
    {
        internal const float OuterX = 284f;     // outermost column offset (matches vanilla P1/P2)
        internal static int N = 2, Left = 1, Right = 1;

        internal static void SetN(int n)
        {
            N = Mathf.Clamp(n, 2, 4);
            Left = Mathf.CeilToInt(N / 2f);
            Right = N - Left;
        }

        internal static float Unit => OuterX / Mathf.Max(1, Mathf.Max(Left, Right));
        internal static int MinPos => -Left;
        internal static int MaxPos => Right;
        internal static float PosToX(int pos) => pos * Unit;                  // 0 => center
        internal static int PosToPlayerIndex(int pos)                          // -1 if unassigned
            => pos == 0 ? -1 : (pos < 0 ? pos + Left : Left + (pos - 1));
        internal static int PlayerIndexToPos(int i)                            // inverse of PosToPlayerIndex
            => (i < Left) ? (i - Left) : (i - Left + 1);
        internal static float PlayerIndexToX(int i) => PlayerIndexToPos(i) * Unit;   // header label x for Pi
    }

    /// <summary>Explicit device-&gt;player picks captured from the join screen, read by the spawner.</summary>
    internal static class FourPlayerRuntime
    {
        internal static Dictionary<int, InputDevice> SlotDevices;   // 0-based player index -> device
    }

    // Replace InputSelectorDeviceRow.SetPosition so a controller can move across N player columns
    // with an unassigned center (vanilla clamped to just -1/0/+1).
    [HarmonyPatch(typeof(InputSelectorDeviceRow), "SetPosition")]
    internal static class DeviceRowSetPosition
    {
        private static readonly FieldInfo MovingF = AccessTools.Field(typeof(InputSelectorDeviceRow), "movingTransform");
        private static readonly FieldInfo LeftArrowF = AccessTools.Field(typeof(InputSelectorDeviceRow), "leftArrow");
        private static readonly FieldInfo RightArrowF = AccessTools.Field(typeof(InputSelectorDeviceRow), "rightArrow");
        private static readonly FieldInfo LastMoveF = AccessTools.Field(typeof(InputSelectorDeviceRow), "lastMoveTime");
        private static readonly FieldInfo PosChangedF = AccessTools.Field(typeof(InputSelectorDeviceRow), "PositionChanged");
        private static readonly MethodInfo PosSetter = AccessTools.PropertySetter(typeof(InputSelectorDeviceRow), "Position");

        private static bool Prefix(InputSelectorDeviceRow __instance, int pos)
        {
            try { Apply(__instance, pos); }
            catch (Exception e) { Plugin.Log.LogWarning($"SetPosition failed: {e.Message}"); }
            return false;   // skip vanilla (which clamps to [-1,1])
        }

        internal static void Apply(InputSelectorDeviceRow row, int pos)
        {
            pos = Mathf.Clamp(pos, JoinLayout.MinPos, JoinLayout.MaxPos);
            if (row.Position == pos) return;
            PosSetter.Invoke(row, new object[] { pos });
            PlaceX(row, pos);
            UpdateArrows(row, pos);
            LastMoveF.SetValue(row, Time.unscaledTime);
            row.SetReady(false);
            (PosChangedF.GetValue(row) as Delegate)?.DynamicInvoke(row, pos);
        }

        // When the column count changes, keep a controller on the SAME player (just re-spaced)
        // instead of letting its raw position drift to a different player; new/unassigned ones go
        // to the center. Preserves the ready state and fires no events.
        internal static void RemapForNChange(InputSelectorDeviceRow row, int oldPlayerIndex)
        {
            int newPos = (oldPlayerIndex < 0 || oldPlayerIndex >= JoinLayout.N) ? 0 : JoinLayout.PlayerIndexToPos(oldPlayerIndex);
            bool keepReady = row.IsReady && newPos != 0;
            PosSetter.Invoke(row, new object[] { newPos });
            PlaceX(row, newPos);
            UpdateArrows(row, newPos);
            row.SetReady(keepReady);
        }

        private static void PlaceX(InputSelectorDeviceRow row, int pos)
        {
            if (!(MovingF.GetValue(row) is RectTransform moving)) return;
            moving.anchorMin = new Vector2(0.5f, moving.anchorMin.y);
            moving.anchorMax = new Vector2(0.5f, moving.anchorMax.y);
            moving.pivot = new Vector2(0.5f, moving.pivot.y);
            var ap = moving.anchoredPosition;
            ap.x = JoinLayout.PosToX(pos);
            moving.anchoredPosition = ap;
        }

        private static void UpdateArrows(InputSelectorDeviceRow row, int pos)
        {
            if (LeftArrowF.GetValue(row) is Image la) la.enabled = pos > JoinLayout.MinPos;
            if (RightArrowF.GetValue(row) is Image ra) ra.enabled = pos < JoinLayout.MaxPos;
        }
    }

    // Don't auto-bump a controller off a slot another already chose; let two sit on the same slot
    // and block the start until everyone resolves to a unique pick (see InputSelectorStart).
    [HarmonyPatch(typeof(InputSelectorScreen), "OnDevicePositionChanged")]
    internal static class NoAutoFlip
    {
        private static bool Prefix() => false;
    }

    // A player position can only be claimed by ONE player: block readying onto a slot another player
    // has already readied. They have to move to a free column to confirm. (Un-readying is always allowed.)
    [HarmonyPatch(typeof(InputSelectorDeviceRow), "SetReady")]
    internal static class BlockDuplicateReady
    {
        private static bool Prefix(InputSelectorDeviceRow __instance, bool ready)
        {
            if (!ready || __instance.Position == 0) return true;
            try
            {
                foreach (var r in Resources.FindObjectsOfTypeAll<InputSelectorDeviceRow>())
                {
                    if (r == null || r == __instance || !r.gameObject.scene.IsValid()) continue;
                    if (r.Position == __instance.Position && r.IsReady) return false;   // slot already taken
                }
            }
            catch { }
            return true;
        }
    }

    // Replaces the vanilla auto-start. When every controller that picked a slot is ready AND all
    // their slots are unique (>=2 players), a "PRESS START TO BEGIN" prompt appears; pressing Start
    // (gamepad Start button, or keyboard Enter) begins. Then we capture the device->player map for
    // the spawner and fire the vanilla DevicesAssigned so the existing run-setup flow continues.
    [HarmonyPatch(typeof(InputSelectorScreen), "Update")]
    internal static class InputSelectorStart
    {
        private static readonly FieldInfo RowsF = AccessTools.Field(typeof(InputSelectorScreen), "rows");
        private static readonly FieldInfo AssignedF = AccessTools.Field(typeof(InputSelectorScreen), "assigned");
        private static readonly FieldInfo DevicesAssignedF = AccessTools.Field(typeof(InputSelectorScreen), "DevicesAssigned");

        // Last-seen value of MetaLoadout's picker change counter, to refresh the header profile tags
        // only when a pick actually changes (cheap poll — no callback from MetaLoadout).
        private static int _lastProfileChange;

        private static bool Prefix(InputSelectorScreen __instance)
        {
            try
            {
                if ((bool)AssignedF.GetValue(__instance)) return false;

                // Let newly-connected ("new") controllers press Start to join the session, and show
                // the join indicator while any are waiting.
                JoinGate.HandlePendingJoins(__instance);
                JoinAddPrompt.Show(__instance, JoinGate.Pending.Count > 0);

                var rows = (RowsF.GetValue(__instance) as IEnumerable)?.Cast<InputSelectorDeviceRow>().ToList();
                if (rows == null) return false;

                // The profile picker itself (opening it on ready-up, and freezing the row while it's up)
                // is owned by PunkMetaLoadout now. We only observe it: refresh the per-column profile
                // tags when a pick changed, and freeze OUR start/movement logic while the picker is open.
                if (ProfileBridge.Available)
                {
                    int cc = ProfileBridge.ChangeCounter;
                    if (cc != _lastProfileChange) { _lastProfileChange = cc; JoinHeader.UpdateProfileLabels(__instance); }
                }
                if (ProfileBridge.IsPickerOpen) return false;   // MetaLoadout's picker is open — freeze start/movement

                var gamepads = rows.Where(r => r.Device is Gamepad).ToList();
                var slotted = rows.Where(r => r.Position != 0).ToList();
                var positions = slotted.Select(r => r.Position).ToList();
                bool allControllersChosen = gamepads.All(r => r.Position != 0);     // no controller left unassigned
                bool enoughPlayers = slotted.Count >= 2;
                bool allReady = slotted.All(r => r.IsReady);
                bool allUnique = positions.Distinct().Count() == positions.Count;
                bool ready = allControllersChosen && enoughPlayers && allReady && allUnique;

                JoinPrompt.Show(__instance, ready);

                if (ready && StartPressed())
                {
                    AssignedF.SetValue(__instance, true);

                    var map = new Dictionary<int, InputDevice>();
                    foreach (var r in slotted) map[JoinLayout.PosToPlayerIndex(r.Position)] = r.Device;
                    FourPlayerRuntime.SlotDevices = map;

                    InputDevice d0 = map.TryGetValue(0, out var a) ? a : null;
                    InputDevice d1 = map.TryGetValue(1, out var b) ? b : null;
                    (DevicesAssignedF.GetValue(__instance) as Action<InputDevice, InputDevice>)?.Invoke(d0, d1);
                    Plugin.Log.LogInfo($"Join: {map.Count} players started by controller.");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"join start failed: {e.Message}"); }
            return false;   // we fully own the start logic now
        }

        private static bool StartPressed()
        {
            // F9 on the keyboard (chosen to avoid clashing with normal KBM bindings like Enter),
            // or an already-JOINED controller's Start button. A pending (not-yet-joined) controller's
            // Start is reserved for joining the session, so it never begins the run.
            var kb = Keyboard.current;
            if (kb != null && kb.f9Key.wasPressedThisFrame) return true;
            foreach (var g in Gamepad.all)
                if (!JoinGate.Pending.Contains(g) && g.startButton.wasPressedThisFrame) return true;
            return false;
        }
    }

    // The "PRESS START TO BEGIN" prompt: a clone of the ASSIGN INPUT title, centered ABOVE the
    // header row (so it never overlaps the player columns in the middle of the screen).
    internal static class JoinPrompt
    {
        internal static void Show(InputSelectorScreen screen, bool visible)
        {
            try
            {
                var go = Ensure(screen);
                if (go == null) return;
                if (visible) Reposition(screen, go);   // keep it above the (possibly resized) header
                if (go.activeSelf != visible) go.SetActive(visible);
            }
            catch { }
        }

        // Sit centered, just above the "ASSIGN INPUT" title (which itself sits above the columns).
        private static void Reposition(InputSelectorScreen screen, GameObject go)
        {
            var title = screen.transform.Find("Window/Players/Text") as RectTransform;
            float baseY = (title != null) ? title.anchoredPosition.y : 58f;
            if (go.GetComponent<RectTransform>() is RectTransform rt)
                rt.anchoredPosition = new Vector2(0f, baseY + 80f);
        }

        private static GameObject Ensure(InputSelectorScreen screen)
        {
            var players = screen.transform.Find("Window/Players");
            if (players == null) return null;
            var existing = players.Find("ModStartPrompt");
            if (existing != null) return existing.gameObject;

            var title = players.Find("Text");      // the "ASSIGN INPUT" TMP, for styling
            if (title == null) return null;

            var clone = UnityEngine.Object.Instantiate(title.gameObject, players);
            clone.name = "ModStartPrompt";
            if (clone.GetComponent<RectTransform>() is RectTransform rt)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);   // top-center of the header block
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(900f, 60f);
            }
            SetText(clone.transform, "PRESS START TO BEGIN   -   F9 / CONTROLLER START");
            clone.SetActive(false);
            return clone;
        }

        private static void SetText(Transform t, string text)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", typeof(string));
                if (p != null && p.CanWrite) { p.SetValue(c, text); return; }
            }
        }
    }

    /// <summary>
    /// Gates "new" controllers behind a press-Start-to-join step. Controllers connected when the
    /// screen opens (the host's) auto-join as before; controllers that connect afterwards (e.g. a
    /// friend's pad in a Remote Play session) are held "pending" — they get no row until they press
    /// Start. Only pending controllers' Start joins; it never begins the run.
    /// </summary>
    internal static class JoinGate
    {
        internal static bool Initializing;   // true during the screen's OnEnable device sweep
        internal static bool ForceAdd;       // true while we deliberately add a joined controller
        internal static readonly HashSet<InputDevice> Pending = new HashSet<InputDevice>();

        private static readonly MethodInfo AddDeviceM = AccessTools.Method(typeof(InputSelectorScreen), "AddDevice");

        internal static void Reset() { Pending.Clear(); }

        // Emulated controllers from PunkSimController are named "PunkSim..."; treat them as dev tools.
        internal static bool IsSim(InputDevice device)
            => device?.name != null && device.name.IndexOf("PunkSim", StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool AnySim()
        {
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++) if (IsSim(pads[i])) return true;
            return false;
        }

        internal static void HandlePendingJoins(InputSelectorScreen screen)
        {
            if (Pending.Count == 0) return;
            InputDevice joined = null;
            foreach (var dev in Pending)
                if (dev is Gamepad g && g.startButton.wasPressedThisFrame) { joined = dev; break; }
            if (joined == null) return;

            Pending.Remove(joined);
            ForceAdd = true;
            try { AddDeviceM.Invoke(screen, new object[] { joined }); }   // creates the row (postfix re-lays out columns)
            catch (Exception e) { Plugin.Log.LogWarning($"join add failed: {e.Message}"); }
            finally { ForceAdd = false; }
            Plugin.Log.LogInfo("A new controller joined via Start.");
        }
    }

    // Mark the screen's initial device sweep so those controllers auto-join (host side).
    [HarmonyPatch(typeof(InputSelectorScreen), "OnEnable")]
    internal static class JoinGate_OnEnable
    {
        private static void Prefix()
        {
            JoinGate.Initializing = true;
            JoinGate.Reset();
            // Always start the player-select screen with NO profiles assigned (don't carry over the
            // last session's picks). The profiles themselves are kept; only the slot assignments reset.
            if (ProfileBridge.Available) ProfileBridge.ClearSlots();
        }
        private static void Postfix() { JoinGate.Initializing = false; }
    }

    // Block auto-adding controllers that connect AFTER open; hold them pending until they press Start.
    [HarmonyPatch(typeof(InputSelectorScreen), "AddDevice")]
    internal static class JoinGate_AddDevice
    {
        private static bool Prefix(InputDevice device)
        {
            if (JoinGate.Initializing || JoinGate.ForceAdd) return true;   // host-side sweep / deliberate join
            if (!(device is Gamepad)) return true;                         // keyboard/mouse unaffected
            if (JoinGate.IsSim(device)) return true;                       // emulated controllers auto-join (dev tool)
            JoinGate.Pending.Add(device);                                  // new controller waits for Start
            return false;                                                  // skip the auto-add
        }
    }

    // If a pending (un-joined) controller disconnects, forget it.
    [HarmonyPatch(typeof(InputSelectorScreen), "RemoveDevice")]
    internal static class JoinGate_RemoveDevice
    {
        private static void Prefix(InputDevice device) => JoinGate.Pending.Remove(device);
    }

    // The "PRESS START TO JOIN" indicator: a clone of the ASSIGN INPUT title, shown at the bottom
    // whenever one or more new controllers are waiting to join.
    internal static class JoinAddPrompt
    {
        internal static void Show(InputSelectorScreen screen, bool visible)
        {
            try
            {
                var go = Ensure(screen);
                if (go != null && go.activeSelf != visible) go.SetActive(visible);
            }
            catch { }
        }

        private static GameObject Ensure(InputSelectorScreen screen)
        {
            var window = screen.transform.Find("Window");
            if (window == null) return null;
            var existing = window.Find("ModJoinPrompt");
            if (existing != null) return existing.gameObject;

            var title = window.Find("Players/Text");
            if (title == null) return null;

            var clone = UnityEngine.Object.Instantiate(title.gameObject, window);
            clone.name = "ModJoinPrompt";
            if (clone.GetComponent<RectTransform>() is RectTransform rt)
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(0f, 96f);   // bottom-center, above the BACK button
                rt.sizeDelta = new Vector2(1000f, 60f);
            }
            SetText(clone.transform, "NEW CONTROLLER  -  PRESS START TO JOIN");
            clone.SetActive(false);
            return clone;
        }

        private static void SetText(Transform t, string text)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", typeof(string));
                if (p != null && p.CanWrite) { p.SetValue(c, text); return; }
            }
        }
    }
}
