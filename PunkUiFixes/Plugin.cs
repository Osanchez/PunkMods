using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;

namespace PunkUiFixes
{
    /// <summary>
    /// Small vanilla UI alignment fixes. Standalone plugin.
    ///
    /// Fix #1 — co-op "ASSIGN INPUT" screen header on ultrawide:
    /// The device-rows container is anchored to the Window center, but the header (Players: the
    /// P1 / ASSIGN INPUT / P2 labels + underline) is anchored to the Window's top-LEFT with a fixed
    /// pixel offset. On 16:9 both land on center; on wider aspect ratios the rows follow center while
    /// the header stays left, so the icons appear shoved right of their column labels. Re-anchoring
    /// the header to be horizontally centered makes both share the screen center on any aspect ratio
    /// (a no-op at 16:9).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.uifixes";
        public const string Name = "PUNK UI Fixes";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} v{Version} loaded.");
        }

        // Hot-reload teardown: remove the UI-alignment patch so a reload doesn't double-hook it.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
        }
    }

    [HarmonyPatch(typeof(InputSelectorScreen), "OnEnable")]
    internal static class CenterAssignInputHeader
    {
        private static void Postfix(InputSelectorScreen __instance)
        {
            try
            {
                // InputSelectorScreen is the canvas root; the header lives at Window/Players.
                var players = __instance.transform.Find("Window/Players") as RectTransform;
                if (players == null) return;

                // Already centered? leave it.
                if (Mathf.Approximately(players.anchorMin.x, 0.5f) && Mathf.Approximately(players.anchorMax.x, 0.5f))
                    return;

                var aMin = players.anchorMin; aMin.x = 0.5f; players.anchorMin = aMin;
                var aMax = players.anchorMax; aMax.x = 0.5f; players.anchorMax = aMax;
                var pos = players.anchoredPosition; pos.x = 0f; players.anchoredPosition = pos;

                Plugin.Log.LogInfo("Centered the ASSIGN INPUT header (ultrawide alignment fix).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"header-align fix failed: {e.Message}"); }
        }
    }
}
