using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Owns the join-screen profile picker trigger: opens <see cref="ProfileOverlay"/> when a player
    /// readies up, and (standalone only) blocks the vanilla auto-start while the picker is open.
    ///
    /// Soft dependency on PunkFourPlayer in ONE direction only (reflection): when FourPlayer is
    /// present it owns the whole N-player join flow, so this class stays out of its way (see the
    /// prefix/postfix split below) and reuses its N-column player-index mapping.
    /// </summary>
    internal static class JoinProfileTrigger
    {
        // ---- FourPlayer presence (cached) ----
        private static bool _fpChecked;
        private static bool _fpPresent;
        internal static bool FourPlayerPresent
        {
            get
            {
                if (!_fpChecked)
                {
                    _fpChecked = true;
                    try { _fpPresent = Type.GetType("PunkFourPlayer.Plugin, PunkFourPlayer") != null; }
                    catch { _fpPresent = false; }
                }
                return _fpPresent;
            }
        }

        // ---- sim-controller detection ----
        // A sim controller is a Gamepad named "PunkSim..." (PunkSimController's naming — independent of
        // FourPlayer). When FourPlayer is present we prefer its authoritative JoinGate.IsSim; the
        // name-prefix check is the standalone fallback.
        private static bool _simMethodTried;
        private static MethodInfo _simMethod;
        internal static bool IsSim(InputDevice device)
        {
            if (device == null) return false;
            if (FourPlayerPresent)
            {
                if (!_simMethodTried)
                {
                    _simMethodTried = true;
                    try
                    {
                        _simMethod = Type.GetType("PunkFourPlayer.JoinGate, PunkFourPlayer")
                            ?.GetMethod("IsSim", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    catch { _simMethod = null; }
                }
                if (_simMethod != null)
                {
                    try { return (bool)_simMethod.Invoke(null, new object[] { device }); } catch { }
                }
            }
            return device is Gamepad && device.name != null &&
                   device.name.IndexOf("PunkSim", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // ---- row Position -> 0-based player index ----
        // When FourPlayer is present reuse its N-column mapping (JoinLayout.PosToPlayerIndex); else the
        // vanilla 2-player mapping: Position < 0 => P1(0), Position > 0 => P2(1), Position 0 => skip.
        private static bool _posMethodTried;
        private static MethodInfo _posMethod;
        internal static int PosToPlayerIndex(int pos)
        {
            if (FourPlayerPresent)
            {
                if (!_posMethodTried)
                {
                    _posMethodTried = true;
                    try
                    {
                        _posMethod = Type.GetType("PunkFourPlayer.JoinLayout, PunkFourPlayer")
                            ?.GetMethod("PosToPlayerIndex", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                    catch { _posMethod = null; }
                }
                if (_posMethod != null)
                {
                    try { return (int)_posMethod.Invoke(null, new object[] { pos }); } catch { }
                }
            }
            return pos == 0 ? -1 : (pos < 0 ? 0 : 1);
        }

        // Per-row "was ready last frame", to detect the unready -> ready transition that opens the
        // picker. Cleared when the screen (re-)opens.
        internal static readonly Dictionary<InputSelectorDeviceRow, bool> ReadyPrev =
            new Dictionary<InputSelectorDeviceRow, bool>();

        internal static void Reset() => ReadyPrev.Clear();

        private static FieldInfo _rowsF;
        private static IEnumerable<InputSelectorDeviceRow> Rows(InputSelectorScreen screen)
        {
            if (_rowsF == null) _rowsF = AccessTools.Field(typeof(InputSelectorScreen), "rows");
            return (_rowsF.GetValue(screen) as IEnumerable)?.Cast<InputSelectorDeviceRow>();
        }

        // Open the picker on any slotted row's unready -> ready transition.
        internal static void Poll(InputSelectorScreen screen)
        {
            try
            {
                var rows = Rows(screen);
                if (rows == null) return;
                foreach (var r in rows)
                {
                    if (r == null) continue;
                    bool isReady = r.IsReady && r.Position != 0;
                    bool was = ReadyPrev.TryGetValue(r, out var w) && w;
                    if (isReady && !was && !ProfileOverlay.IsOpen)
                    {
                        int pi = PosToPlayerIndex(r.Position);
                        if (pi >= 0) ProfileOverlay.Open(screen, pi, r.Device);
                    }
                    ReadyPrev[r] = isReady;
                }
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profile trigger poll failed: {e.Message}"); }
        }
    }

    // STANDALONE path (FourPlayer ABSENT): a prefix on the vanilla join Update that (a) opens the
    // picker on ready-up and (b) blocks the vanilla auto-start (DevicesAssigned) while the picker is
    // open. It must both open AND block here, because vanilla would otherwise assign in the very same
    // frame the second player readies — before any postfix could open the picker.
    // When FourPlayer is PRESENT this prefix is inert (returns true): FourPlayer owns the flow, its own
    // Update prefix does the freezing, and Harmony may skip this prefix anyway once FourPlayer's prefix
    // returns false — so the trigger is done from a postfix instead (below).
    [HarmonyPatch(typeof(InputSelectorScreen), "Update")]
    internal static class ProfileJoinFreeze
    {
        private static bool Prefix(InputSelectorScreen __instance)
        {
            if (JoinProfileTrigger.FourPlayerPresent) return true;   // FourPlayer handles it
            try
            {
                JoinProfileTrigger.Poll(__instance);
                if (ProfileOverlay.IsOpen) return false;             // freeze vanilla assign while picking
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profile join freeze failed: {e.Message}"); }
            return true;
        }
    }

    // BOTH-INSTALLED path (FourPlayer PRESENT): FourPlayer's Update prefix returns false (it fully owns
    // start), which would SKIP a MetaLoadout prefix. Postfixes always run, so open the picker here.
    // FourPlayer never auto-starts on ready-up (it requires an explicit Start press) so there's no
    // same-frame race — the picker opens this frame and FourPlayer freezes next frame via IsPickerOpen.
    // Inert standalone (the prefix above already polled this frame).
    [HarmonyPatch(typeof(InputSelectorScreen), "Update")]
    internal static class ProfileJoinTrigger
    {
        private static void Postfix(InputSelectorScreen __instance)
        {
            if (!JoinProfileTrigger.FourPlayerPresent) return;       // standalone handled in the prefix
            JoinProfileTrigger.Poll(__instance);
        }
    }

    // Freeze the device rows while the picker is up (stops movement/ready toggles behind the overlay).
    // Returns false ONLY when the picker is open, so any other mod's prefix on this method still runs
    // in the normal case (e.g. FourPlayer's own keyboard/sim freeze).
    [HarmonyPatch(typeof(InputSelectorDeviceRow), "Update")]
    internal static class ProfileFreezeRows
    {
        private static bool Prefix() => !ProfileOverlay.IsOpen;
    }

    // On (re-)opening the join screen: clear the ready-transition memory, and reset all P1-P4 -> profile
    // slot assignments so each session starts with no profiles picked (the profiles themselves persist).
    // Harmless double-clear when FourPlayer is present (it also clears slots on OnEnable).
    [HarmonyPatch(typeof(InputSelectorScreen), "OnEnable")]
    internal static class ProfileJoin_OnEnable
    {
        private static void Postfix()
        {
            try
            {
                JoinProfileTrigger.Reset();
                ProfileApi.ClearSlots();
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"profile join reset failed: {e.Message}"); }
        }
    }
}
