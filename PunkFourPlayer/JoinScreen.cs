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

                // Discover controllers the game never announced. Steam Remote Play virtual pads show
                // up in Gamepad.all but frequently DON'T fire InputSystem.onDeviceChange, so vanilla's
                // auto-add (and our onDeviceChange-driven gate) never sees them. A per-frame sweep parks
                // any such pad as "pending" so it can press Start to join like any other new controller.
                JoinGate.DiscoverUndetectedPads(__instance);

                // Let newly-connected ("new") controllers press Start to join the session, and show
                // the join indicator while any are waiting (unless the screen is already full).
                JoinGate.HandlePendingJoins(__instance);
                JoinAddPrompt.Show(__instance, JoinGate.Pending.Count > 0 && JoinGate.HasRoom(__instance));

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
        private static readonly FieldInfo RowsF = AccessTools.Field(typeof(InputSelectorScreen), "rows");

        internal static void Reset() { Pending.Clear(); }

        private static List<InputSelectorDeviceRow> GetRows(InputSelectorScreen screen)
            => (RowsF.GetValue(screen) as IEnumerable)?.Cast<InputSelectorDeviceRow>().Where(r => r != null).ToList()
               ?? new List<InputSelectorDeviceRow>();

        // How many controllers have actually JOINED (have a device row). The keyboard is a spectator.
        internal static int GamepadRowCount(InputSelectorScreen screen)
            => GetRows(screen).Count(r => r.Device is Gamepad);

        // Room for another player? Capped at the mod's max (Plugin.Target, 2..4). Prevents Start-spam
        // adding rows past the supported player count.
        internal static bool HasRoom(InputSelectorScreen screen)
            => GamepadRowCount(screen) < Mathf.Clamp(Plugin.Target, 2, 4);

        private static bool IsJoined(List<InputSelectorDeviceRow> rows, InputDevice device)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Device == device) return true;
            return false;
        }

        // Poll-based join for pads the game never announced (Remote Play). Any gamepad in Gamepad.all
        // that isn't already a row and isn't already pending is parked as pending so it can press Start
        // to join. The "already a row / already pending" checks are what enforce ONE join per physical
        // controller — a joined pad is a row, so it's never re-parked and Start can't add a second slot.
        internal static void DiscoverUndetectedPads(InputSelectorScreen screen)
        {
            try
            {
                if (Initializing) return;   // the OnEnable sweep owns the host-side pads on the first frame
                var rows = GetRows(screen);
                var pads = Gamepad.all;
                for (int i = 0; i < pads.Count; i++)
                {
                    var g = pads[i];
                    if (g == null || IsSim(g)) continue;      // sim pads auto-join via the AddDevice gate
                    if (Pending.Contains(g)) continue;        // already waiting for Start
                    if (IsJoined(rows, g)) continue;          // already has a slot (dedupe)
                    Pending.Add(g);
                    Plugin.Log.LogInfo($"[join] detected controller '{g.name}' (id={g.deviceId}) not announced by the game " +
                        "(Remote Play?) — held PENDING, press Start to join.");
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"pad discovery failed: {e.Message}"); }
        }

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

            // Cap at the supported player count; a pad stays pending if the screen is full (a slot may
            // free up if someone drops), but we never add a row beyond the max.
            if (!HasRoom(screen))
            {
                Plugin.Log.LogInfo($"Four-Player: join ignored — already at the max {Mathf.Clamp(Plugin.Target, 2, 4)} controllers.");
                return;
            }

            Pending.Remove(joined);
            ForceAdd = true;
            try { AddDeviceM.Invoke(screen, new object[] { joined }); }   // creates the row (postfix re-lays out columns)
            catch (Exception e) { Plugin.Log.LogWarning($"join add failed: {e.Message}"); }
            finally { ForceAdd = false; }
            Plugin.Log.LogInfo($"[join] controller '{joined.name}' (id={joined.deviceId}) JOINED via Start — {GamepadRowCount(screen)} controller row(s) now.");
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
        private static void Postfix(InputSelectorScreen __instance)
        {
            JoinGate.Initializing = false;
            JoinDebug.Snapshot(__instance, "input screen OPENED");
        }
    }

    // Escaping back to the menu (screen disabled) clears the join state, so a fresh open always starts
    // clean — a deliberate "reset if something looks wrong" escape hatch. We clear the pending set here;
    // the vanilla OnEnable clears the device rows on the next open. FourPlayerRuntime.SlotDevices is
    // intentionally NOT cleared: the ship spawner reads it AFTER the screen closes on a real start, and
    // it's overwritten on the next join, so a stale value can't leak into a fresh session.
    [HarmonyPatch(typeof(InputSelectorScreen), "OnDisable")]
    internal static class JoinGate_OnDisable
    {
        private static void Postfix()
        {
            int pend = JoinGate.Pending.Count;
            JoinGate.Reset();
            Plugin.Log.LogInfo($"[join] input screen CLOSED — cleared {pend} pending controller(s); reopen starts clean.");
        }
    }

    // Block auto-adding controllers that connect AFTER open; hold them pending until they press Start.
    [HarmonyPatch(typeof(InputSelectorScreen), "AddDevice")]
    internal static class JoinGate_AddDevice
    {
        private static bool Prefix(InputDevice device)
        {
            if (JoinGate.Initializing || JoinGate.ForceAdd) return true;   // host-side sweep / deliberate join
            if (!(device is Gamepad)) return true;                         // keyboard/mouse unaffected
            if (JoinGate.IsSim(device)) { Plugin.Log.LogInfo($"[join] sim pad '{device.name}' (id={device.deviceId}) auto-joined."); return true; }
            JoinGate.Pending.Add(device);                                  // new controller waits for Start
            Plugin.Log.LogInfo($"[join] controller '{device.name}' (id={device.deviceId}) connected after open — held PENDING (press Start to join).");
            return false;                                                  // skip the auto-add
        }
    }

    // If a pending (un-joined) controller disconnects, forget it.
    [HarmonyPatch(typeof(InputSelectorScreen), "RemoveDevice")]
    internal static class JoinGate_RemoveDevice
    {
        private static void Prefix(InputDevice device) => JoinGate.Pending.Remove(device);
    }

    // The "PRESS START TO JOIN" indicator: a clone of the ASSIGN INPUT title, shown near the TOP of
    // the screen whenever one or more new controllers are waiting to join. It sits above the title and
    // above the BEGIN prompt, so it never overlaps the P# header labels (y=-50) or the vanilla title.
    internal static class JoinAddPrompt
    {
        // Dim secondary green (matches the join screen's P3 color) so it reads as a hint, not a title,
        // and is visually distinct from the white "PRESS START TO BEGIN" prompt.
        private static readonly Color PromptColor = new Color(0.62f, 0.85f, 0.62f, 1f);

        internal static void Show(InputSelectorScreen screen, bool visible)
        {
            try
            {
                var go = Ensure(screen);
                if (go == null) return;
                if (visible) Reposition(screen, go);   // track the (possibly resized/lifted) title
                if (go.activeSelf != visible) go.SetActive(visible);
            }
            catch { }
        }

        // Sit centered near the top, above the "ASSIGN INPUT" title AND above the BEGIN prompt
        // (title+80), so both can show at once without overlapping each other or the header row.
        private static void Reposition(InputSelectorScreen screen, GameObject go)
        {
            var title = screen.transform.Find("Window/Players/Text") as RectTransform;
            float baseY = (title != null) ? title.anchoredPosition.y : 4.12f;
            if (go.GetComponent<RectTransform>() is RectTransform rt)
                rt.anchoredPosition = new Vector2(0f, baseY + 150f);
        }

        private static GameObject Ensure(InputSelectorScreen screen)
        {
            var players = screen.transform.Find("Window/Players");
            if (players == null) return null;
            var existing = players.Find("ModJoinPrompt");
            if (existing != null) return existing.gameObject;

            var title = players.Find("Text");
            if (title == null) return null;

            var clone = UnityEngine.Object.Instantiate(title.gameObject, players);
            clone.name = "ModJoinPrompt";
            if (clone.GetComponent<RectTransform>() is RectTransform rt)
            {
                rt.anchorMin = new Vector2(0.5f, 1f);   // top-center of the header block
                rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(1000f, 60f);
            }
            SetText(clone.transform, "PRESS START TO JOIN");
            SetColor(clone.transform, PromptColor);
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

        private static void SetColor(Transform t, Color color)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("color", typeof(Color));
                if (p != null && p.CanWrite) { p.SetValue(c, color); return; }
            }
        }
    }

    /// <summary>
    /// Diagnostic snapshot of the join screen's input landscape — every gamepad the OS/Steam exposes
    /// (name + id + sim flag), the keyboard state, the current device rows, and the pending set. Logged
    /// on open and on every device add/remove so a "too many controllers" situation (e.g. Steam Remote
    /// Play surfacing phantom/duplicate pads) is fully traceable from BepInEx\LogOutput.log.
    /// </summary>
    internal static class JoinDebug
    {
        internal static void Snapshot(InputSelectorScreen screen, string reason)
        {
            try
            {
                var pads = Gamepad.all;
                var sb = new System.Text.StringBuilder();
                sb.Append($"[join] {reason}: {pads.Count} gamepad(s), kbm={(Keyboard.current != null && Mouse.current != null)}");
                for (int i = 0; i < pads.Count; i++)
                {
                    var g = pads[i];
                    sb.Append($"\n         pad[{i}] '{g.name}' display='{g.displayName}' id={g.deviceId} sim={JoinGate.IsSim(g)}");
                }
                var rows = (AccessTools.Field(typeof(InputSelectorScreen), "rows").GetValue(screen) as IEnumerable)
                    ?.Cast<InputSelectorDeviceRow>().Where(r => r != null).ToList() ?? new List<InputSelectorDeviceRow>();
                sb.Append($"\n         rows={rows.Count} [{string.Join(", ", rows.Select(r => $"{r.Device?.name ?? "?"}@{r.Position}"))}]");
                sb.Append($"  pending={JoinGate.Pending.Count} [{string.Join(", ", JoinGate.Pending.Select(d => d?.name ?? "?"))}]");
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[join] snapshot failed: {e.Message}"); }
        }
    }
}
