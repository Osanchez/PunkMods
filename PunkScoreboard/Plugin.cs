using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PunkScoreboard
{
    /// <summary>
    /// A co-op scoreboard shown while <b>Tab</b> (or a gamepad's <b>Select/View</b> button) is held.
    /// Columns are stats, rows are players. While it's open the game slows to 0.1x (like the debug
    /// menu), and the displayed values refresh on a slow poll (every few seconds) rather than every
    /// frame. Every competitive column is instrumented by this mod — PUNK tracks none of them per
    /// player — and resets at the start of each run.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.scoreboard";
        public const string Name = "PUNK Scoreboard";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<bool> Enabled;

        private const float SlowScale   = 0.1f;   // matches the debug menu's slow-mo
        private const float PollSeconds = 2.5f;   // how often the on-screen values refresh while open

        private GUIStyle _style;
        private GUIStyle _styleR;   // right-aligned
        private Texture2D _bg;

        private bool  _open;          // is the board currently shown?
        private bool  _slowed;        // have we applied the time-scale modifier?
        private float _lastPollUnscaled = -999f;

        // Cached display snapshot, rebuilt on the poll cadence (not every frame).
        private struct RowS { public string name, kills, bosses, deaths, dmg, time, hp, fuel; }
        private string _snapHeader = "";
        private RowS[] _snap = new RowS[0];

        // Column layout: header label + x offset (from panel left) + width.
        private static readonly (string head, float x, float w)[] Cols =
        {
            ("PLAYER", 0,    165),
            ("KILLS",  165,  78),
            ("BOSS",   243,  78),
            ("DEATHS", 321,  84),
            ("DMG",    405,  101),
            ("TIME",   506,  87),
            ("HP",     593,  134),
            ("FUEL",   727,  84),
        };
        private const float PanelW = 838f;

        private void Awake()
        {
            Log = Logger;

            var cfg = new BepInEx.Configuration.ConfigFile(
                System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Enabled = cfg.Bind("General", "Enabled", true,
                "Show the co-op scoreboard while Tab (or gamepad Select) is held.");

            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            GameController.GameStarted += Scoreboard.Rebuild;
            ModMenuBridge.AddToggle("Scoreboard (hold Tab)", () => Enabled.Value, v => Enabled.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. Hold Tab / gamepad Select to view. Enabled={Enabled.Value}.");
        }

        private void Update()
        {
            Scoreboard.Tick();   // keep real-time accumulation of deaths / alive-time

            bool show = Enabled != null && Enabled.Value && Scoreboard.HasRun && HeldThisFrame();
            ApplySlowmo(show);

            if (show)
            {
                // Refresh on open, then on the poll cadence. Use unscaled time so the slow-mo
                // doesn't stretch the interval.
                if (!_open || Time.unscaledTime - _lastPollUnscaled >= PollSeconds)
                {
                    BuildSnapshot();
                    _lastPollUnscaled = Time.unscaledTime;
                }
                _open = true;
            }
            else
            {
                _open = false;
            }
        }

        private void ApplySlowmo(bool wantSlow)
        {
            if (wantSlow == _slowed) return;
            try
            {
                var tm = ServiceLocator.Get<TimeManager>();
                if (tm == null) return;
                if (wantSlow) tm.SetTimeScale(SlowScale, this);
                else          tm.RemoveAllModifiers(this);
                _slowed = wantSlow;
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"Scoreboard slow-mo failed: {e.Message}"); }
        }

        private void OnDestroy()
        {
            // Be sure we never leave the game stuck in slow-mo.
            if (_slowed) { try { ServiceLocator.Get<TimeManager>()?.RemoveAllModifiers(this); } catch { } }
        }

        private static bool HeldThisFrame()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.tabKey.isPressed) return true;
            var pads = Gamepad.all;
            for (int i = 0; i < pads.Count; i++)
                if (pads[i].selectButton.isPressed) return true;
            return false;
        }

        // ---- Snapshot (polled) ----

        private void BuildSnapshot()
        {
            try
            {
                int n = Scoreboard.Ships.Count;
                _snapHeader = GlobalLine();
                var rows = new RowS[n];
                for (int i = 0; i < n; i++)
                {
                    var ship = Scoreboard.Ships[i];
                    string hex = ship != null ? ColorUtility.ToHtmlStringRGB(ship.color) : "ffffff";
                    bool dead = ship != null && ship.IsDead;
                    rows[i] = new RowS
                    {
                        name   = $"<color=#{hex}><b>Player {i + 1}</b>{(dead ? " <color=#ff6666>(dead)</color>" : "")}</color>",
                        kills  = Scoreboard.Kills[i].ToString(),
                        bosses = Scoreboard.Bosses[i].ToString(),
                        deaths = Scoreboard.Deaths[i].ToString(),
                        dmg    = Compact(Scoreboard.DamageDealt[i]),
                        time   = Clock(Scoreboard.TimeAlive[i]),
                        hp     = $"{Mathf.Max(0, Mathf.RoundToInt(Scoreboard.CurrentHealth(i)))}/{Mathf.RoundToInt(Scoreboard.MaxHealth(i))}",
                        fuel   = FuelPct(ship),
                    };
                }
                _snap = rows;
            }
            catch (Exception e) { Plugin.Log?.LogWarning($"Scoreboard snapshot failed: {e.Message}"); }
        }

        // ---- Render (reads only the cached snapshot) ----

        private void OnGUI()
        {
            if (!_open || _snap == null) return;
            EnsureStyles();

            int n = _snap.Length;
            float pad = 18f, headerH = 34f, globalH = 28f, colH = 28f, rowH = 32f;
            float panelH = pad + headerH + globalH + 8 + colH + n * rowH + pad;

            float px = (Screen.width - PanelW) * 0.5f;
            float py = Mathf.Max(40f, Screen.height * 0.18f);
            var panel = new Rect(px, py, PanelW, panelH);

            GUI.DrawTexture(panel, _bg);

            float ix = panel.x + pad, iw = PanelW - pad * 2, y = panel.y + pad;

            GUI.Label(new Rect(ix, y, iw, headerH), "<b><size=24>SCOREBOARD</size></b>", _style);
            y += headerH;
            GUI.Label(new Rect(ix, y, iw, globalH), $"<size=16><color=#bbbbbb>{_snapHeader}</color></size>", _style);
            y += globalH + 8;

            foreach (var c in Cols)
                Cell(ix, y, c.x, c.w, $"<size=14><color=#888888>{c.head}</color></size>", c.x > 0);
            y += colH;

            for (int i = 0; i < n; i++)
            {
                var r = _snap[i];
                Cell(ix, y, Cols[0].x, Cols[0].w, r.name,   false);
                Cell(ix, y, Cols[1].x, Cols[1].w, r.kills,  true);
                Cell(ix, y, Cols[2].x, Cols[2].w, r.bosses, true);
                Cell(ix, y, Cols[3].x, Cols[3].w, r.deaths, true);
                Cell(ix, y, Cols[4].x, Cols[4].w, r.dmg,    true);
                Cell(ix, y, Cols[5].x, Cols[5].w, r.time,   true);
                Cell(ix, y, Cols[6].x, Cols[6].w, r.hp,     true);
                Cell(ix, y, Cols[7].x, Cols[7].w, r.fuel,   true);
                y += rowH;
            }
        }

        private void Cell(float ix, float y, float cx, float cw, string text, bool right)
            => GUI.Label(new Rect(ix + cx, y, cw, 30f), text, right ? _styleR : _style);

        private static string GlobalLine()
        {
            try
            {
                var rd = ServiceLocator.Get<RunData>();
                var gc = ServiceLocator.Get<GameController>();
                string time = rd != null ? Clock(rd.TotalRunTime) : "0:00";
                int enemies = rd?.KilledEnemyCount ?? 0;
                int score = gc?.Score ?? 0;
                return $"RUN {time}   ·   ENEMIES {enemies}   ·   SCORE {score}";
            }
            catch { return ""; }
        }

        private static string FuelPct(Ship ship)
        {
            if (ship == null) return "-";
            try { return $"{Mathf.Max(0, Mathf.RoundToInt(ship.Fuel))}"; }
            catch { return "-"; }
        }

        private static string Clock(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int s = Mathf.FloorToInt(seconds);
            return $"{s / 60}:{(s % 60):00}";
        }

        private static string Compact(float v)
        {
            if (v >= 100000f) return $"{v / 1000f:0}k";
            if (v >= 1000f)   return $"{v / 1000f:0.0}k";
            return Mathf.RoundToInt(v).ToString();
        }

        private void EnsureStyles()
        {
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                { fontSize = 19, richText = true, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            if (_styleR == null)
                _styleR = new GUIStyle(_style) { alignment = TextAnchor.MiddleRight };
            if (_bg == null)
            {
                _bg = new Texture2D(1, 1);
                _bg.SetPixel(0, 0, new Color(0.03f, 0.03f, 0.05f, 0.62f));   // semi-transparent
                _bg.Apply();
            }
        }
    }

    // ---- Damage attribution: capture victim health before each hit handler, credit the delta after. ----

    [HarmonyPatch(typeof(DamagableResource), nameof(DamagableResource.ProjectileCollided))]
    internal static class Patch_Projectile
    {
        private static void Prefix(DamagableResource __instance, out float __state)
        { try { __state = __instance.CurrentHealth; } catch { __state = 0f; } }
        private static void Postfix(DamagableResource __instance, IProjectile projectile, float __state)
        { try { Scoreboard.CreditDamage(projectile?.Owner, __state - __instance.CurrentHealth); } catch { } }
    }

    [HarmonyPatch(typeof(DamagableResource), nameof(DamagableResource.OnHitByHitscanWeapon))]
    internal static class Patch_Hitscan
    {
        private static void Prefix(DamagableResource __instance, out float __state)
        { try { __state = __instance.CurrentHealth; } catch { __state = 0f; } }
        private static void Postfix(DamagableResource __instance, HitscanWeapon weapon, float __state)
        { try { Scoreboard.CreditDamage(weapon?.Owner, __state - __instance.CurrentHealth); } catch { } }
    }

    [HarmonyPatch(typeof(DamagableResource), nameof(DamagableResource.OnExplosion))]
    internal static class Patch_Explosion
    {
        private static void Prefix(DamagableResource __instance, out float __state)
        { try { __state = __instance.CurrentHealth; } catch { __state = 0f; } }
        private static void Postfix(DamagableResource __instance, Explosion explosion, float __state)
        { try { Scoreboard.CreditDamage(explosion.Owner, __state - __instance.CurrentHealth); } catch { } }
    }

    [HarmonyPatch(typeof(RunData), nameof(RunData.RegisterBossKilled))]
    internal static class Patch_BossKilled
    {
        private static void Postfix() => Scoreboard.OnBossKilled();
    }
}
