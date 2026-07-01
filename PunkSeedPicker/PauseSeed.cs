using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace PunkSeedPicker
{
    /// <summary>
    /// Shows the current world's seed on the in-game pause screen (the "Save and Exit" menu), so you
    /// can note or share the seed of a run you're already in. Cloned from the run-time label so it
    /// matches the game's style; the seed comes from the same Seed service the main-menu text uses.
    /// </summary>
    [HarmonyPatch(typeof(PauseScreen), "Open")]
    internal static class PauseSeedDisplay
    {
        private static readonly FieldInfo RunTimeF = AccessTools.Field(typeof(PauseScreen), "runTime");
        private static readonly Dictionary<PauseScreen, TMP_Text> _texts = new Dictionary<PauseScreen, TMP_Text>();

        private static void Postfix(PauseScreen __instance)
        {
            try
            {
                if (!_texts.TryGetValue(__instance, out var seedText) || seedText == null)
                {
                    var runTime = RunTimeF?.GetValue(__instance) as TMP_Text;
                    if (runTime == null) return;

                    var clone = UnityEngine.Object.Instantiate(runTime.gameObject, runTime.transform.parent);
                    clone.name = "ModSeedText";
                    seedText = clone.GetComponent<TMP_Text>();
                    if (seedText != null && seedText.rectTransform != null)
                        seedText.rectTransform.anchoredPosition += new Vector2(0f, -40f);   // sit just below the run time
                    _texts[__instance] = seedText;
                }

                if (seedText != null)
                {
                    var seed = ServiceLocator.Get<Seed>();
                    seedText.text = seed != null ? $"SEED: {seed}" : "";
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"pause seed display failed: {e.Message}"); }
        }
    }
}
