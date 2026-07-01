using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace PunkScoreboard
{
    /// <summary>
    /// Per-player run statistics, instrumented by the mod. PUNK itself tracks none of these
    /// per player (kills/score/time are global totals; damage and deaths aren't tracked at all),
    /// so we wire our own counters off confirmed hooks and reset them at the start of each run.
    /// </summary>
    internal static class Scoreboard
    {
        // Per-player counters, indexed by ShipManager.Ships order (0 = P1). Sized to the live ship count.
        internal static List<Ship> Ships = new List<Ship>();
        internal static int[]   Kills        = new int[0];
        internal static int[]   Bosses       = new int[0];
        internal static int[]   Deaths       = new int[0];
        internal static float[] DamageDealt  = new float[0];
        internal static float[] TimeAlive    = new float[0];

        private static bool[] _prevDead = new bool[0];

        // Attacker Unit -> player index, for crediting damage and kills.
        private static Dictionary<Unit, int> _unitToPlayer = new Dictionary<Unit, int>();

        // KilledAnotherUnit subscriptions, kept so we can detach on rebuild.
        private static readonly List<(Unit unit, Action<Unit, Unit> handler)> _killHandlers
            = new List<(Unit, Action<Unit, Unit>)>();

        // The most recent player kill this frame — used to attribute boss kills (which the game
        // registers separately via RunData.RegisterBossKilled in the same destruction frame).
        private static int _lastKillerPlayer = -1;
        private static int _lastKillFrame = -1;

        private static readonly System.Reflection.FieldInfo ShipDamagableF =
            AccessTools.Field(typeof(Ship), "damagableResource");

        internal static bool HasRun => Ships != null && Ships.Count > 0;

        /// <summary>Re-read the ship roster, reset all counters, and re-subscribe kill events. Called on GameStarted.</summary>
        internal static void Rebuild()
        {
            try
            {
                DetachKillHandlers();

                var sm = ServiceLocator.Get<ShipManager>();
                Ships = sm?.Ships?.ToList() ?? new List<Ship>();
                int n = Ships.Count;

                Kills       = new int[n];
                Bosses      = new int[n];
                Deaths      = new int[n];
                DamageDealt = new float[n];
                TimeAlive   = new float[n];
                _prevDead   = new bool[n];
                _unitToPlayer = new Dictionary<Unit, int>();
                _lastKillerPlayer = -1;
                _lastKillFrame = -1;

                for (int i = 0; i < n; i++)
                {
                    var unit = Ships[i]?.Unit;
                    if (unit == null) continue;
                    _unitToPlayer[unit] = i;

                    int idx = i; // capture
                    Action<Unit, Unit> h = (killer, killed) => OnKill(idx, killer, killed);
                    unit.KilledAnotherUnit += h;
                    _killHandlers.Add((unit, h));
                }
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"Scoreboard.Rebuild failed: {e.Message}"); }
        }

        private static void DetachKillHandlers()
        {
            foreach (var (unit, handler) in _killHandlers)
            {
                try { if (unit != null) unit.KilledAnotherUnit -= handler; } catch { }
            }
            _killHandlers.Clear();
        }

        private static void OnKill(int player, Unit killer, Unit killed)
        {
            try
            {
                if (killer == null || killed == null) return;
                _lastKillerPlayer = player;
                _lastKillFrame = Time.frameCount;
                if (player >= 0 && player < Kills.Length && killer.IsEnemiesWith(killed))
                    Kills[player]++;
            }
            catch { }
        }

        /// <summary>Called from the RunData.RegisterBossKilled postfix; credits the same-frame killer.</summary>
        internal static void OnBossKilled()
        {
            if (_lastKillFrame == Time.frameCount &&
                _lastKillerPlayer >= 0 && _lastKillerPlayer < Bosses.Length)
                Bosses[_lastKillerPlayer]++;
        }

        /// <summary>Called from the DamagableResource hit-handler postfixes with the attacker and the health delta.</summary>
        internal static void CreditDamage(Unit owner, float delta)
        {
            if (owner == null || delta <= 0f || _unitToPlayer == null) return;
            if (_unitToPlayer.TryGetValue(owner, out int p) && p >= 0 && p < DamageDealt.Length)
                DamageDealt[p] += delta;
        }

        /// <summary>Per-frame: detect death transitions and accumulate alive time. Called from Plugin.Update.</summary>
        internal static void Tick()
        {
            if (Ships == null || Ships.Count == 0)
            {
                // Lazily build if a run is already in progress (e.g. loaded save) but we missed GameStarted.
                if (ServiceLocator.Get<ShipManager>()?.Ships?.Count > 0) Rebuild();
                return;
            }

            for (int i = 0; i < Ships.Count; i++)
            {
                var s = Ships[i];
                if (s == null) continue;
                bool dead = s.IsDead;
                if (dead && !_prevDead[i]) Deaths[i]++;
                _prevDead[i] = dead;
                // Unscaled so the scoreboard's own slow-mo (and other time-scale effects) don't
                // distort survival time; matches how the game accrues RunData.TotalRunTime.
                if (!dead) TimeAlive[i] += Time.unscaledDeltaTime;
            }
        }

        internal static float CurrentHealth(int i)
        {
            try { return GetDamagable(i)?.CurrentHealth ?? 0f; } catch { return 0f; }
        }

        internal static float MaxHealth(int i)
        {
            try { return GetDamagable(i)?.MaxHealth ?? 0f; } catch { return 0f; }
        }

        private static DamagableResource GetDamagable(int i)
        {
            if (Ships == null || i < 0 || i >= Ships.Count || Ships[i] == null) return null;
            return ShipDamagableF?.GetValue(Ships[i]) as DamagableResource;
        }
    }
}
