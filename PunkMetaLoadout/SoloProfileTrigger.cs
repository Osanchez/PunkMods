using System;
using HarmonyLib;
using UnityEngine.InputSystem;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Single-player runs never open the co-op <c>InputSelectorScreen</c>, so the join-screen trigger
    /// (<see cref="JoinProfileTrigger"/>) never fires and a solo player could never pick a profile.
    /// This intercepts the common start choke point — <c>GameScene.GoToGameScene(RunArguments)</c>,
    /// which both <c>RunSetupScreen.StartGame</c> and PunkSeedPicker's seed screen funnel through — and
    /// for a NEW single-player run opens the picker for P1 (slot 0) first, resuming the start only after
    /// the player chooses. Co-op and continue are left untouched (profiles handled at the join screen /
    /// not picked on continue).
    ///
    /// Hooking GoToGameScene (rather than RunSetupScreen.StartGame) means we coexist with PunkSeedPicker
    /// without sharing a patched method: SeedPicker cancels StartGame and calls GoToGameScene itself, so
    /// the picker appears after the seed screen (class -> seed -> profile -> run). No prefix-ordering
    /// fight, and the direct GoToGameScene call from the seed screen is still caught.
    /// </summary>
    [HarmonyPatch(typeof(GameScene), nameof(GameScene.GoToGameScene))]
    internal static class SoloProfileTrigger
    {
        // True only while we replay the start after the picker closes, so our own re-invocation passes
        // straight through instead of re-opening the picker (avoids infinite recursion).
        private static bool _bypass;

        private static bool Prefix(RunArguments args)
        {
            if (_bypass) { _bypass = false; return true; }   // our replayed start — let it through
            try
            {
                if (args.isCoop) return true;        // co-op: profiles chosen at the join screen
                if (args.isContinue) return true;    // continue: builds restored from save, no pick
                if (ProfileOverlay.IsOpen) return true;   // safety: already picking — don't cancel/re-open

                // The device the solo player is using (for overlay navigation). If somehow neither is
                // present there'd be no way to dismiss the overlay, so just start normally.
                InputDevice device = (InputDevice)Gamepad.current ?? Keyboard.current;
                if (device == null) return true;

                // Start each session fresh (mirrors the co-op join screen clearing slots on open): if the
                // player backs out of the picker, P1 resolves to "No Profile" rather than a stale pick.
                try { ProfileApi.SetSlot(0, null); } catch { }

                var argsCopy = args;   // RunArguments is a struct — capture by value for the replay
                ProfileOverlay.Open(null, 0, device, () =>
                {
                    _bypass = true;
                    try { GameScene.GoToGameScene(argsCopy); }
                    catch (Exception e)
                    {
                        _bypass = false;
                        Plugin.Log?.LogWarning($"solo start replay failed: {e.Message}");
                    }
                });
                return false;   // cancel this start; the on-close callback restarts it
            }
            catch (Exception e)
            {
                Plugin.Log?.LogWarning($"solo profile trigger failed; starting normally: {e.Message}");
                return true;    // never block the run on our account
            }
        }
    }
}
