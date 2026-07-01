using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;
using UnityEngine;

namespace PunkReviveItem
{
    /// <summary>
    /// Adds a shop consumable — the "Revive Beacon" — that, when used, revives a random downed
    /// player. It's a fixed shop item (available from the start) costing 2500 of the shop currency.
    /// The consumable is built at runtime, registered in the ConsumableRegistry, and appended to the
    /// run's consumable shop list.
    /// </summary>
    public class ReviveConsumable : Consumable
    {
        public override void Use(Ship ship)
        {
            try
            {
                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships == null) return;
                var dead = ships.Where(s => s != null && s.IsDead).ToList();
                if (dead.Count == 0) { Plugin.Log.LogInfo("Revive Beacon used, but no players are down."); return; }
                var pick = dead[UnityEngine.Random.Range(0, dead.Count)];
                pick.Resurrect();
                RefillFuel(pick);              // Resurrect() zeroes every non-health tank (incl. fuel) — give it back
                MoveTo(pick, ship.transform.position);   // spawn where the beacon was used, not the death spot
                Plugin.Log.LogInfo("Revive Beacon revived a downed player.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Revive failed: {e.Message}"); }
        }

        private static readonly System.Reflection.FieldInfo FuelField = AccessTools.Field(typeof(Ship), "fuel");

        private static void RefillFuel(Ship ship)
        {
            try
            {
                var fuel = FuelField?.GetValue(ship) as Resource;
                var unit = ship.Unit;
                if (fuel != null && unit != null && unit.HasTank(fuel))
                {
                    var tank = unit.GetTank(fuel);
                    tank.Value = tank.Capacity;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Refuel after revive failed: {e.Message}"); }
        }

        // Teleport the revived ship to where the beacon was used, nudged a little so it doesn't stack
        // on the user, with momentum cleared.
        private static void MoveTo(Ship ship, Vector3 where)
        {
            try
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle.normalized * 2f;
                Vector3 target = where + new Vector3(off.x, off.y, 0f);
                ship.transform.position = target;
                var rb = ship.Rigidbody;
                if (rb != null) { rb.position = target; rb.velocity = Vector2.zero; rb.angularVelocity = 0f; }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Revive teleport failed: {e.Message}"); }
        }
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.reviveitem";
        public const string Name = "PUNK Revive Item";
        public const string Version = "1.0.0";

        internal const string ItemId = "punk_revive_random";
        internal const int Cost = 2500;

        internal static ManualLogSource Log;
        private static ReviveConsumable _consumable;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} v{Version} loaded. Revive Beacon ({Cost}) will appear in the shop.");
        }

        internal static ReviveConsumable Consumable()
        {
            if (_consumable != null) return _consumable;
            _consumable = ScriptableObject.CreateInstance<ReviveConsumable>();
            _consumable.id = ItemId;
            _consumable.displayName = "Revive Beacon";
            _consumable.description = "Revives a random downed player.";
            _consumable.maxCount = 5;
            _consumable.icon = MakeIcon();
            return _consumable;
        }

        // Register our consumable in the ConsumableRegistry so it resolves by id (shop + save/load).
        internal static void EnsureRegistered()
        {
            try
            {
                var c = Consumable();
                var reg = ServiceLocator.Get<ConsumableRegistry>();
                if (reg == null) return;
                var baseT = typeof(ScriptableObjectRegistry<Consumable, string>);
                if (AccessTools.Field(baseT, "itemDictionary").GetValue(reg) is Dictionary<string, Consumable> dict && !dict.ContainsKey(c.id))
                    dict[c.id] = c;
                if (AccessTools.Field(baseT, "itemList").GetValue(reg) is List<Consumable> list && !list.Contains(c))
                    list.Add(c);
            }
            catch (Exception e) { Log.LogWarning($"Consumable registration failed: {e.Message}"); }
        }

        // A simple themed icon: a green medical cross on a dark rounded background.
        private static Sprite MakeIcon()
        {
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            var clear = new Color(0, 0, 0, 0);
            var bg = new Color(0.10f, 0.11f, 0.13f, 0.95f);
            var green = new Color(0.33f, 0.92f, 0.45f, 1f);
            float c = (s - 1) * 0.5f;
            float arm = s * 0.12f, half = s * 0.33f, radius = s * 0.46f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c, dy = y - c;
                    Color col = clear;
                    if (Mathf.Sqrt(dx * dx + dy * dy) <= radius) col = bg;
                    if ((Mathf.Abs(dx) <= arm && Mathf.Abs(dy) <= half) || (Mathf.Abs(dy) <= arm && Mathf.Abs(dx) <= half)) col = green;
                    tex.SetPixel(x, y, col);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    // Add the Revive Beacon to the fixed consumable shop list when a new run initializes.
    [HarmonyPatch(typeof(RunData), "Initialize")]
    internal static class InjectShopItem
    {
        private static void Postfix(RunData __instance)
        {
            try
            {
                Plugin.EnsureRegistered();

                var list = __instance.ConsumableShopItems;
                if (list == null) return;
                if (list.Any(i => i?.consumable != null && i.consumable.id == Plugin.ItemId)) return;   // already present

                // Reuse whatever Resource the existing consumables are priced in (the shop "coins").
                Resource coin = null;
                foreach (var it in list)
                {
                    if (it?.price == null) continue;
                    foreach (var pr in it.price)
                        if (pr.currencyType == Price.CurrencyType.Resource && pr.resource != null) { coin = pr.resource; break; }
                    if (coin != null) break;
                }
                if (coin == null) { Plugin.Log.LogWarning("Could not find the shop currency; Revive Beacon not added."); return; }

                var price = new Price { currencyType = Price.CurrencyType.Resource, resource = coin, amount = Plugin.Cost };
                list.Add(new ConsumableShopItem
                {
                    consumable = Plugin.Consumable(),
                    price = new List<Price> { price },
                    priceIncrement = new List<Price>(),
                });
                Plugin.Log.LogInfo($"Revive Beacon added to the shop for {Plugin.Cost}.");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Revive Beacon injection failed: {e.Message}"); }
        }
    }

    // On loading a save, make sure the consumable is registered before mementos resolve it by id.
    [HarmonyPatch(typeof(RunData), "RestoreFromMemento")]
    internal static class RegisterOnLoad
    {
        private static void Prefix() => Plugin.EnsureRegistered();
    }
}
