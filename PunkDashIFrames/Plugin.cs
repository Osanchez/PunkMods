using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;
using UnityEngine;

namespace PunkDashIFrames
{
    /// <summary>
    /// Gives the ship dash a short window of invincibility — while dashing you take no damage for a
    /// second, so you can dash through lasers and bullets. Toggleable from the Mods menu; the window
    /// length is a config value (default 1s). Subscribes to each ship's DashStarted and skips all
    /// hull damage on that ship until the window expires.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.dashiframes";
        public const string Name = "PUNK Dash I-Frames";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<bool> Enabled;
        internal static BepInEx.Configuration.ConfigEntry<float> Seconds;

        // Per-ship invincibility deadline (game time), keyed by the ship's damage resource.
        internal static readonly Dictionary<DamagableResource, float> IframeUntil = new Dictionary<DamagableResource, float>();
        private static readonly List<(ShipMovement move, Action handler)> _subs = new List<(ShipMovement, Action)>();
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Enabled = cfg.Bind("General", "Enabled", true, "Grant invincibility while dashing.");
            Seconds = cfg.Bind("General", "Seconds", 0.5f, "How long the dash invincibility lasts, in seconds.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            GameController.GameStarted += Rebuild;
            ModMenuBridge.AddToggle("Dash Invincibility", () => Enabled.Value, v => Enabled.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. Enabled={Enabled.Value}, window={Seconds.Value}s.");
        }

        // Hot-reload teardown: undo the damage-block patch, stop listening for run starts, detach every
        // per-ship DashStarted handler we added, and clear the tracking state and Mods-menu row.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            try { GameController.GameStarted -= Rebuild; } catch { }
            try
            {
                foreach (var (m, h) in _subs) { if (m != null) m.DashStarted -= h; }
                _subs.Clear();
                IframeUntil.Clear();
            }
            catch { }
            try { ModMenuBridge.RemoveAll(); } catch { }
        }

        // (Re)subscribe to every ship's DashStarted at the start of each run.
        private static void Rebuild()
        {
            try
            {
                foreach (var (m, h) in _subs) { if (m != null) m.DashStarted -= h; }
                _subs.Clear();
                IframeUntil.Clear();

                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships == null) return;
                foreach (var ship in ships)
                {
                    if (ship == null) continue;
                    var move = ship.GetComponentInChildren<ShipMovement>();
                    var dmg = ship.GetComponentInChildren<DamagableResource>();
                    if (move == null || dmg == null) continue;
                    Action h = () => { IframeUntil[dmg] = Time.time + Mathf.Max(0f, Seconds.Value); };
                    move.DashStarted += h;
                    _subs.Add((move, h));
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Dash I-Frames rebuild failed: {e.Message}"); }
        }

        internal static bool Invincible(DamagableResource d)
            => Enabled != null && Enabled.Value && d != null
               && IframeUntil.TryGetValue(d, out float until) && Time.time < until;
    }

    // All hull damage funnels through DamagableResource.Damage(float); skip it during the dash window.
    [HarmonyPatch(typeof(DamagableResource), nameof(DamagableResource.Damage), new[] { typeof(float) })]
    internal static class BlockDamageDuringDash
    {
        private static bool Prefix(DamagableResource __instance) => !Plugin.Invincible(__instance);
    }
}
