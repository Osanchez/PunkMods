using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace PunkFourPlayer
{
    /// <summary>Per-player rumble strength for P3/P4 (the game only stores P1/P2). Persisted to
    /// PunkFourPlayer's config; also exposed as sliders in the Gameplay settings tab.</summary>
    internal static class RumbleConfig
    {
        // Fully-qualified: Punk.Main also defines a colliding "ConfigFile" type.
        private static BepInEx.Configuration.ConfigEntry<float> _p3, _p4;

        internal static void Init(BepInEx.Configuration.ConfigFile cfg)
        {
            _p3 = cfg.Bind("Rumble", "P3GamepadRumble", 1f,
                new BepInEx.Configuration.ConfigDescription("Player 3 gamepad rumble strength.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));
            _p4 = cfg.Bind("Rumble", "P4GamepadRumble", 1f,
                new BepInEx.Configuration.ConfigDescription("Player 4 gamepad rumble strength.", new BepInEx.Configuration.AcceptableValueRange<float>(0f, 1f)));
        }

        internal static float P3 { get => _p3?.Value ?? 1f; set { if (_p3 != null) _p3.Value = value; } }
        internal static float P4 { get => _p4?.Value ?? 1f; set { if (_p4 != null) _p4.Value = value; } }
    }

    // The game scales rumble by IsPlayerTwo (p1/p2 only), so P3/P4 wrongly reuse P1/P2's setting.
    // After the original runs, re-apply the correct motor speeds for P3/P4 using their own values.
    [HarmonyPatch(typeof(ShipGamepadRumble), "Rumble")]
    internal static class RumblePerPlayer
    {
        private static readonly FieldInfo ShipF = AccessTools.Field(typeof(ShipGamepadRumble), "ship");
        private static readonly FieldInfo PadF = AccessTools.Field(typeof(ShipGamepadRumble), "gamepad");

        private static void Postfix(ShipGamepadRumble __instance, RumblePreset preset)
        {
            try
            {
                if (preset == null) return;
                var ship = ShipF?.GetValue(__instance) as Ship;
                if (ship == null || ship.shipInput == null || !ship.shipInput.UsesGamepad) return;

                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships == null) return;
                int idx = -1;
                for (int i = 0; i < ships.Count; i++) if (ships[i] == ship) { idx = i; break; }
                if (idx < 2) return;   // P1/P2 already correct from the original

                float num = idx == 2 ? RumbleConfig.P3 : RumbleConfig.P4;
                var pad = PadF?.GetValue(__instance) as UnityEngine.InputSystem.Gamepad;
                pad?.SetMotorSpeeds(preset.leftMotorSpeed * num, preset.rightMotorSpeed * num);
            }
            catch { }
        }
    }

    // Add "P3 RUMBLE" / "P4 RUMBLE" sliders to the Gameplay settings tab, cloned from the P2 slider.
    [HarmonyPatch(typeof(GameplayOptionsTab), "OnOpened")]
    internal static class GameplayRumbleSliders
    {
        private static readonly FieldInfo P2F = AccessTools.Field(typeof(GameplayOptionsTab), "p2RumbleSlider");
        private static readonly FieldInfo ItemsF = AccessTools.Field(typeof(OptionsTab), "items");
        private static readonly Dictionary<GameplayOptionsTab, (OptionsMenuItemSlider p3, OptionsMenuItemSlider p4)> _added
            = new Dictionary<GameplayOptionsTab, (OptionsMenuItemSlider, OptionsMenuItemSlider)>();

        private static void Postfix(GameplayOptionsTab __instance)
        {
            try
            {
                if (!_added.TryGetValue(__instance, out var s) || s.p3 == null)
                {
                    s = Create(__instance);
                    _added[__instance] = s;
                }
                if (s.p3 != null) s.p3.Value = RumbleConfig.P3;   // re-sync each open
                if (s.p4 != null) s.p4.Value = RumbleConfig.P4;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"P3/P4 rumble sliders failed: {e.Message}"); }
        }

        private static (OptionsMenuItemSlider, OptionsMenuItemSlider) Create(GameplayOptionsTab tab)
        {
            var p2 = P2F.GetValue(tab) as OptionsMenuItemSlider;
            if (p2 == null) return (null, null);

            var p3 = Clone(p2, "P3 RUMBLE", RumbleConfig.P3, v => RumbleConfig.P3 = v);
            var p4 = Clone(p2, "P4 RUMBLE", RumbleConfig.P4, v => RumbleConfig.P4 = v);

            int si = p2.transform.GetSiblingIndex();
            p3.transform.SetSiblingIndex(si + 1);
            p4.transform.SetSiblingIndex(si + 2);

            // Insert into the tab's nav item array right after P2.
            if (ItemsF.GetValue(tab) is OptionsMenuitemBase[] arr)
            {
                var items = arr.ToList();
                int ii = items.IndexOf(p2);
                if (ii >= 0) { items.Insert(ii + 1, p4); items.Insert(ii + 1, p3); }
                else { items.Add(p3); items.Add(p4); }
                ItemsF.SetValue(tab, items.ToArray());
            }
            return (p3, p4);
        }

        private static OptionsMenuItemSlider Clone(OptionsMenuItemSlider src, string label, float value, Action<float> onChanged)
        {
            var clone = UnityEngine.Object.Instantiate(src.gameObject, src.transform.parent).GetComponent<OptionsMenuItemSlider>();
            clone.name = label;
            SetText(clone.transform.Find("Visual/ItemName") ?? FindByName(clone.transform, "ItemName"), label);
            clone.Value = value;
            clone.ValueChanged.AddListener(_ => { try { onChanged(clone.Value); } catch { } });
            return clone;
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindByName(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        private static void SetText(Transform t, string text)
        {
            if (t == null) return;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", typeof(string));
                if (p != null && p.CanWrite) { p.SetValue(c, text); return; }
            }
        }
    }
}
