using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace PunkFourPlayer
{
    /// <summary>
    /// Soft (reflection) bridge into PunkMetaLoadout's profile system — no hard assembly dependency.
    /// PunkMetaLoadout owns the profile concept, data, and the ready-up picker overlay; FourPlayer only
    /// READS profile state here to (a) show each column's chosen profile in the header and (b) freeze
    /// its own start logic while the picker is open. If MetaLoadout isn't installed every member is a
    /// null-safe no-op and the join screen behaves exactly like vanilla-plus-N-columns (no profiles).
    /// </summary>
    internal static class ProfileBridge
    {
        private static readonly Type T = Type.GetType("PunkMetaLoadout.ProfileApi, PunkMetaLoadout");
        internal static bool Available => T != null;

        internal static string GetSlot(int slot)
        { try { return T?.GetMethod("GetSlot")?.Invoke(null, new object[] { slot }) as string; } catch { return null; } }

        internal static void ClearSlots()
        { try { T?.GetMethod("ClearSlots")?.Invoke(null, null); } catch { } }

        // True while MetaLoadout's ready-up picker overlay is open — FourPlayer freezes start/movement.
        internal static bool IsPickerOpen
        { get { try { return (bool)(T?.GetProperty("IsPickerOpen")?.GetValue(null) ?? false); } catch { return false; } } }

        // Bumped by MetaLoadout each time the picker closes; FourPlayer refreshes its header tags when
        // this advances instead of relying on a callback from MetaLoadout.
        internal static int ChangeCounter
        { get { try { return (int)(T?.GetProperty("ChangeCounter")?.GetValue(null) ?? 0); } catch { return 0; } } }
    }

    // Freeze the KEYBOARD's own join row whenever emulated controllers are present — so the keyboard
    // only puppets the selected sim (via PunkSimController) instead of also driving its own join row.
    // (The picker-open freeze now lives in PunkMetaLoadout, which owns the picker.)
    [HarmonyPatch(typeof(InputSelectorDeviceRow), "Update")]
    internal static class FreezeKeyboardRowForSim
    {
        private static bool Prefix(InputSelectorDeviceRow __instance)
        {
            if (__instance.Device is Keyboard && JoinGate.AnySim()) return false;
            return true;
        }
    }
}
