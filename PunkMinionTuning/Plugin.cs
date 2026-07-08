using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono).
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PunkMinionTuning
{
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  PUNK Minion Tuning — dev diagnostics + live tuning for MINIONS ("drones").
    //
    //  WHY: A minion's Vision.range / Vision.refreshDelay are [SerializeField] floats with no C#
    //  initializer, so their real values live on the prefab and can't be read statically from the
    //  DLL. This mod patches spawn to read them live, logs them, and lets you rewrite them on live
    //  drones (config + hotkeys) so you can dial in values that fix "slow to aggro" without a rebuild.
    //
    //  Driven by in-game hotkeys; the only Mods-menu row is a switch for the on-screen diagnostics
    //  overlay ("Diagnostics overlay"), which is OFF by default.
    //
    //  HOTKEYS — hold LEFT ALT and press a key (main-keyboard only, no numpad; Alt keeps them from
    //  clashing with the game's bare keys and the other dev-tool mods' F-keys):
    //    Alt + D   Dump every live drone's current Vision/power/faction values NOW.
    //    Alt + R   Dump the FULL unit roster (every Unit prefab's powerLevel/maxToFight/vision/faction)
    //              from SavablesCollection — the reference for power-level tables. Open the debug spawn
    //              menu once first so the collection asset is loaded.
    //    Alt + =   Sight range += RangeStep   (live + new drones; seeds from the real value on first press)
    //    Alt + -   Sight range -= RangeStep
    //    Alt + ]   Scan delay  += DelayStep   (live + new drones; lower = reacts faster)
    //    Alt + [   Scan delay  -= DelayStep
    //    Alt + 0   Reset vision tuning -> new drones use their built-in sight range / scan interval.
    //    Alt + N   Toggle TargetNearest (drones target the nearest visible enemy). ON by default.
    //    Alt + B   Toggle DefendOwner — drones attack units whose blacklist has the owner (provoked
    //              neutrals like the crawler) AND retaliate against anything that damages the owner.
    //    Alt + H   Toggle the Hold-position command feature (re-press spawn to make drones hold/release).
    //    Alt + O   Show/hide the on-screen HUD (current settings + toggle states + a live drone sample).
    //
    //  HOLD POSITION: with a drone already out, pressing the spawn button again makes it HOLD near where
    //  the player is (still dodging/attacking, returning there after fights). Press again — or move far
    //  away — to release it back to following. Respawned drones start following.
    //    Alt + S   Log the first live drone's FULL state machine (states, actions, transitions, conditions).
    //
    //  Vision tuning is implicit: bumping range/delay applies to all drones and sticks for new spawns;
    //  there is no separate on/off switch — Alt+0 clears it back to the drones' built-in values.
    //  Every reflection/lookup failure logs a warning instead of throwing — safe to leave installed.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.miniontuning";
        public const string Name = "PUNK Minion Tuning";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        // ── Config (BepInEx/plugins/PunkMinionTuning/config.cfg) ──────────────────────────────────
        internal static ConfigEntry<bool> DumpOnSpawn;
        internal static ConfigEntry<float> VisionRange;    // -1 = leave the drone's built-in value alone
        internal static ConfigEntry<float> RefreshDelay;   // -1 = leave the drone's built-in value alone
        internal static ConfigEntry<bool> TargetNearest;
        internal static ConfigEntry<bool> DefendOwner;
        internal static ConfigEntry<bool> SitCommand;
        internal static ConfigEntry<float> SitReleaseDistance;
        internal static ConfigEntry<float> SitDeadzone;
        internal static ConfigEntry<float> SitHoldSpeed;
        internal static ConfigEntry<float> RangeStep;
        internal static ConfigEntry<float> DelayStep;
        internal static ConfigEntry<bool> ShowOverlay;

        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;

            var cfg = new BepInEx.Configuration.ConfigFile(Path.Combine(ModFolder.Dir, "config.cfg"), true);
            DumpOnSpawn = cfg.Bind("General", "DumpOnSpawn", true,
                "On each drone spawn, log its live Vision.Range, refreshDelay, powerLevel, maxPowerLevelToFight and faction name.");
            VisionRange = cfg.Bind("Vision", "VisionRange", -1f,
                "Drone sight range. -1 = leave the built-in value untouched. Set it in-game with Numpad +/- (seeds from the real value on first press); applies live and to new drones.");
            RefreshDelay = cfg.Bind("Vision", "RefreshDelay", -1f,
                "Drone scan interval in seconds (lower = reacts faster). -1 = leave the built-in value untouched. Set it in-game with Numpad * // ; applies live and to new drones.");
            TargetNearest = cfg.Bind("Targeting", "TargetNearest", true,
                "Drones target the NEAREST visible enemy instead of the game's arbitrary pick. Drones only — enemy targeting is untouched. Toggle in-game with Numpad 2.");
            DefendOwner = cfg.Bind("Targeting", "DefendOwner", true,
                "Drones also attack units hostile to their OWNER: (a) any visible unit whose enemy-blacklist contains the owner (e.g. a crawler you provoked), and (b) drones retaliate against anything that damages the owner. Blacklist-driven; mirrors the owner's threats. Toggle in-game with Alt+B.");
            SitCommand = cfg.Bind("SitCommand", "Enabled", true,
                "Re-pressing the spawn button while a drone is already out toggles HOLD POSITION: the drone holds near where the player was, still dodges/attacks, and returns there after fights. Press again (or move far away) to release it back to following. New drones spawn following. Toggle the feature with Alt+H.");
            SitReleaseDistance = cfg.Bind("SitCommand", "AutoFollowDistance", 45f,
                "If the owner gets farther than this from a holding drone, the drone auto-releases and follows again.");
            SitDeadzone = cfg.Bind("SitCommand", "HoldRadius", 1f,
                "A holding drone snaps to the exact hold point once within this distance (smaller = tighter pin).");
            SitHoldSpeed = cfg.Bind("SitCommand", "HoldSpeed", 16f,
                "How fast a holding drone travels to the hold point (units/sec). Higher = snappier repositioning.");
            RangeStep = cfg.Bind("Steps", "RangeStep", 5f,
                "How much the Numpad +/- hotkeys change the sight range per press.");
            DelayStep = cfg.Bind("Steps", "DelayStep", 0.05f,
                "How much the Numpad * // hotkeys change the scan interval per press.");
            ShowOverlay = cfg.Bind("Overlay", "ShowOverlay", false,
                "Show the on-screen HUD with current drone settings + toggle states. Off by default; toggle it from the MODS menu (\"Diagnostics overlay\") or in-game with Alt+O.");

            SetupAudio();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(SpawnPatch));
            _harmony.PatchAll(typeof(TargetPatch));
            _harmony.PatchAll(typeof(OwnerThreatTargeting));
            _harmony.PatchAll(typeof(SitTogglePatch));
            _harmony.PatchAll(typeof(SitRedirectOrbit));
            _harmony.PatchAll(typeof(HoldOwnerRange));

            // The only Mods-menu row: a switch for the on-screen diagnostics overlay (off by default).
            // Everything else stays on the Alt-hotkeys; this just gives a discoverable way to turn the
            // dev HUD on without remembering Alt+O.
            ModMenuBridge.AddToggle("Diagnostics overlay", () => ShowOverlay.Value, v => ShowOverlay.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. TargetNearest={TargetNearest.Value}, vision tuning " +
                        $"{(VisionRange.Value >= 0f || RefreshDelay.Value >= 0f ? "active" : "off (built-in)")}. " +
                        "Keys (hold Left Alt): O overlay; D dump live; S statemachine; R roster; =/- range; ]/[ delay; 0 reset vision; N nearest; B defend-owner; H hold-cmd. Re-press spawn = hold/release.");
        }

        // Hot-reload teardown: undo patches and drop our Mods-menu row.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
            try { ModMenuBridge.RemoveAll(); } catch { }
        }

        // ── Audio feedback (procedural beeps; no game-asset dependency) ───────────────────────────
        private static AudioSource _sfx;
        private static AudioClip _engageClip, _releaseClip;

        private void SetupAudio()
        {
            try
            {
                _sfx = gameObject.AddComponent<AudioSource>();
                _sfx.spatialBlend = 0f; _sfx.playOnAwake = false;   // 2D, audible everywhere
                _engageClip = MakeBeep(880f, 0.12f);                // engage: high
                _releaseClip = MakeBeep(440f, 0.12f);               // release: low
            }
            catch (Exception e) { WarnOnce("audio setup failed", e); }
        }

        private static AudioClip MakeBeep(float freq, float dur)
        {
            const int rate = 44100;
            int n = (int)(rate * dur);
            var clip = AudioClip.Create("mt_beep", n, 1, rate, false);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * i / rate) * 0.35f * Mathf.Exp(-4f * t);  // quick decay
            }
            clip.SetData(data, 0);
            return clip;
        }

        internal static void PlayEngage()  { try { _sfx?.PlayOneShot(_engageClip); }  catch { } }
        internal static void PlayRelease() { try { _sfx?.PlayOneShot(_releaseClip); } catch { } }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            try
            {
                // Drive held drones toward their hold point every frame (gated internally by SitCommand).
                SitManager.Tick();

                // Refresh the HUD snapshot (throttled) regardless of any modifier so it stays live.
                if (ShowOverlay != null && ShowOverlay.Value && Time.unscaledTime >= _nextOverlayRefresh)
                {
                    _nextOverlayRefresh = Time.unscaledTime + 0.2f;
                    RefreshOverlay();
                }

                // Everything else is gated behind Left Alt so bare game keys / other mods' F-keys never clash.
                if (!kb.leftAltKey.isPressed) return;
                if (kb.oKey.wasPressedThisFrame) { ShowOverlay.Value = !ShowOverlay.Value; RefreshOverlay(); }  // Alt+O
                if (kb.dKey.wasPressedThisFrame) DumpAllLiveMinions();            // Alt+D
                if (kb.sKey.wasPressedThisFrame) DumpDroneStateMachine();         // Alt+S
                if (kb.rKey.wasPressedThisFrame) DumpUnitRoster();                // Alt+R
                if (kb.equalsKey.wasPressedThisFrame) BumpRange(+RangeStep.Value);   // Alt+=
                if (kb.minusKey.wasPressedThisFrame) BumpRange(-RangeStep.Value);    // Alt+-
                if (kb.rightBracketKey.wasPressedThisFrame) BumpDelay(+DelayStep.Value);  // Alt+]
                if (kb.leftBracketKey.wasPressedThisFrame) BumpDelay(-DelayStep.Value);   // Alt+[
                if (kb.digit0Key.wasPressedThisFrame) ResetVisionTuning();        // Alt+0
                if (kb.nKey.wasPressedThisFrame) ToggleTargetNearest();           // Alt+N
                if (kb.bKey.wasPressedThisFrame)                                  // Alt+B
                {
                    DefendOwner.Value = !DefendOwner.Value;
                    Log.LogInfo($"[minion] DefendOwner -> {DefendOwner.Value} (drones {(DefendOwner.Value ? "attack units hostile to the owner + retaliate on owner damage" : "ignore owner threats")}).");
                }
                if (kb.hKey.wasPressedThisFrame)                                  // Alt+H
                {
                    SitCommand.Value = !SitCommand.Value;
                    Log.LogInfo($"[minion] SitCommand (hold-position) -> {SitCommand.Value}.");
                }
            }
            catch (Exception e) { WarnOnce("hotkey handler failed", e); }
        }

        // ── On-screen HUD ─────────────────────────────────────────────────────────────────────────
        private float _nextOverlayRefresh;
        private List<string> _overlayLines;
        private GUIStyle _overlayStyle;

        // Build the HUD text once per refresh tick (cheap to redraw every OnGUI, expensive to gather).
        private void RefreshOverlay()
        {
            try
            {
                string on = "<color=#7CFC00>ON</color>", off = "<color=#9a9a9a>OFF</color>";
                string rng = VisionRange.Value >= 0f ? VisionRange.Value.ToString("0.###") : "<color=#9a9a9a>built-in</color>";
                string dly = RefreshDelay.Value >= 0f ? RefreshDelay.Value.ToString("0.###") + "s" : "<color=#9a9a9a>built-in</color>";

                // One row PER live drone (not just the first) — with 3 drones out you see all 3, each
                // with its own mode/state/target/vision. The header keeps the live count.
                int n = 0; var droneRows = new List<string>();
                foreach (var a in LiveMinions())
                {
                    var v = MinionOps.GetVision(a);
                    if (v == null) continue;
                    n++;
                    string tgt = "none";
                    try { if (a.HasTarget && a.Target != null) tgt = a.Target.name; } catch { }
                    string state = MinionOps.GetStateChain(a.Unit);
                    string hold = SitManager.IsSitting(a.Unit) ? "<color=#EBA845>HOLD</color>" : "follow";
                    droneRows.Add($"<size=11>  #{n}  mode={hold}  state={state}  target={tgt}  range={v.Range:0.#} refresh={MinionOps.GetRefreshDelay(v):0.###}s</size>");
                }

                _overlayLines = new List<string>
                {
                    "<b><color=#EBA845>MINION TUNING</color></b>   <size=11>(Alt+O hide)</size>",
                    $"Target nearest : {(TargetNearest.Value ? on : off)}   <size=11>Alt+N</size>",
                    $"Defend owner   : {(DefendOwner.Value ? on : off)}   <size=11>Alt+B (blacklist-driven)</size>",
                    $"Hold command   : {(SitCommand.Value ? on : off)}   <size=11>Alt+H · re-press spawn · {SitManager.HoldingCount} holding</size>",
                    $"Sight range    : {rng}   <size=11>Alt +/- (step {RangeStep.Value:0.###})</size>",
                    $"Scan delay     : {dly}   <size=11>Alt ]/[ (step {DelayStep.Value:0.###})</size>",
                    $"Live drones    : {n}",
                };
                _overlayLines.AddRange(droneRows);
                _overlayLines.Add("<size=11><color=#9a9a9a>Alt+0 reset · Alt+D dump · Alt+R roster</color></size>");
            }
            catch (Exception e) { WarnOnce("overlay refresh failed", e); }
        }

        private void OnGUI()
        {
            if (ShowOverlay == null || !ShowOverlay.Value) return;
            var lines = _overlayLines;
            if (lines == null || lines.Count == 0) return;
            if (_overlayStyle == null)
                _overlayStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, normal = { textColor = Color.white } };

            const float pad = 10f, lh = 20f, w = 560f;
            float h = pad * 2 + lh * lines.Count;
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(10f, 10f, w, h), Texture2D.whiteTexture);   // dim backdrop
            GUI.color = prev;
            for (int i = 0; i < lines.Count; i++)
                GUI.Label(new Rect(20f, 10f + pad + i * lh, w - 20f, lh), lines[i], _overlayStyle);
        }

        // ── Hotkey actions ────────────────────────────────────────────────────────────────────────

        private void BumpRange(float delta)
        {
            // First press from "off" seeds from a live drone's REAL range so you nudge reality, not a guess.
            float baseVal = VisionRange.Value >= 0f ? VisionRange.Value : SeedFromLive(v => v.Range, 20f);
            VisionRange.Value = Mathf.Max(0f, baseVal + delta);
            int n = 0;
            foreach (var a in LiveMinions())
            {
                var v = MinionOps.GetVision(a);
                if (v != null) { v.Range = VisionRange.Value; n++; }
            }
            Log.LogInfo($"[minion] sight range -> {VisionRange.Value:0.###} (applied to {n} live drone(s); new drones use this too).");
        }

        private void BumpDelay(float delta)
        {
            float baseVal = RefreshDelay.Value >= 0f ? RefreshDelay.Value : SeedFromLive(MinionOps.GetRefreshDelay, 0.2f);
            RefreshDelay.Value = Mathf.Max(0f, baseVal + delta);
            int n = 0;
            foreach (var a in LiveMinions())
            {
                var v = MinionOps.GetVision(a);
                if (v != null && MinionOps.SetRefreshDelay(v, RefreshDelay.Value)) n++;
            }
            Log.LogInfo($"[minion] scan interval -> {RefreshDelay.Value:0.###}s (applied to {n} live drone(s); new drones use this too).");
        }

        // Clear vision tuning so new drones fall back to their built-in values. Live drones keep the
        // values already written to them (we don't snapshot originals) until they respawn.
        private void ResetVisionTuning()
        {
            VisionRange.Value = -1f;
            RefreshDelay.Value = -1f;
            Log.LogInfo("[minion] vision tuning cleared — new drones use their built-in sight range / scan interval. (Live drones keep current values until they respawn.)");
        }

        private void ToggleTargetNearest()
        {
            TargetNearest.Value = !TargetNearest.Value;
            // Targeting is re-evaluated every frame in AIAgent.Update, so no re-apply is needed —
            // the target-selection prefix reads this flag live.
            Log.LogInfo($"[minion] TargetNearest -> {TargetNearest.Value} (takes effect immediately; targeting runs per-frame).");
        }

        // First live drone's value via the given reader, or a fallback if none are spawned yet — so the
        // first +/- press nudges from the drone's REAL value instead of a guess.
        private static float SeedFromLive(Func<Vision, float> read, float fallback)
        {
            foreach (var a in LiveMinions())
            {
                var v = MinionOps.GetVision(a);
                if (v == null) continue;
                float val = read(v);
                if (!float.IsNaN(val)) return val;
            }
            return fallback;
        }

        private void DumpAllLiveMinions()
        {
            int n = 0;
            foreach (var a in LiveMinions())
            {
                var u = a.Unit;
                var v = MinionOps.GetVision(a);
                MinionOps.DumpValues("live", u, v, a);
                n++;
            }
            if (n == 0) Log.LogInfo("[minion] dump: no live minions found (spawn one first).");
            else Log.LogInfo($"[minion] dump: {n} live minion(s) listed above.");
        }

        // Log the full behaviour tree (states, actions, transitions, conditions) of the first live drone.
        private void DumpDroneStateMachine()
        {
            foreach (var a in LiveMinions())
            {
                var root = a.Unit;
                if (root == null) continue;
                string tgt = "none";
                try { if (a.HasTarget && a.Target != null) tgt = a.Target.name; } catch { }
                Log.LogInfo($"[sm] ===== {root.name}  (current={MinionOps.GetStateChain(root)}  target={tgt}) =====");
                MinionOps.DumpStateMachines(root);
                Log.LogInfo("[sm] ===== end =====");
                return;
            }
            Log.LogInfo("[sm] no live drones (spawn one first).");
        }

        // Dump every Unit PREFAB's static power/vision values, read straight off the prefabs in
        // SavablesCollection (the same registry the debug spawn menu uses). No spawning needed — this is
        // the source for the power-level reference tables. These values are [SerializeField] on the
        // prefab, so this is the only way to read them without instantiating each unit.
        private void DumpUnitRoster()
        {
            try
            {
                var colls = Resources.FindObjectsOfTypeAll<SavablesCollection>();
                if (colls == null || colls.Length == 0)
                {
                    Log.LogInfo("[roster] no SavablesCollection loaded — open the debug spawn menu once, then press Num3.");
                    return;
                }

                var seen = new HashSet<string>();
                var rows = new List<string>();
                foreach (var coll in colls)
                {
                    if (coll?.savableObjectInfos == null) continue;
                    foreach (var info in coll.savableObjectInfos)
                    {
                        var se = info.prefab;
                        if (se == null) continue;
                        Unit u = null;
                        try { u = se.GetComponentInChildren<Unit>(true); } catch { }
                        if (u == null) continue;

                        string id = string.IsNullOrEmpty(info.entityId) ? se.name : info.entityId;
                        if (!seen.Add(id)) continue;   // dedup across collections

                        int power = MinionOps.GetInt(MinionOps.PowerLevelF, u, -1);
                        int maxFight = MinionOps.GetInt(MinionOps.MaxPowerF, u, -1);
                        Vision v = null;
                        try { v = u.GetComponentInChildren<Vision>(true); } catch { }
                        string range = v != null ? v.Range.ToString("0.###") : "-";
                        string delay = v != null ? MinionOps.GetRefreshDelay(v).ToString("0.###") : "-";
                        string faction = "<none>";
                        try { if (u.Faction != null) faction = u.Faction.name; } catch { }

                        rows.Add($"{id} ;; power={power} ;; maxToFight={maxFight} ;; range={range} ;; refreshDelay={delay} ;; faction={faction}");
                    }
                }

                rows.Sort(StringComparer.OrdinalIgnoreCase);
                Log.LogInfo($"[roster] {rows.Count} Unit prefab(s). Columns: id ;; powerLevel ;; maxPowerLevelToFight ;; Vision.range ;; refreshDelay ;; faction");
                foreach (var r in rows) Log.LogInfo($"[roster] {r}");
                Log.LogInfo($"[roster] end ({rows.Count} units).");
            }
            catch (Exception e) { WarnOnce("roster dump failed", e); }
        }

        // ── Live minion enumeration ────────────────────────────────────────────────────────────────
        // A "minion" is an AIAgent whose Unit.Owner != null, on a real loaded scene object (not a prefab
        // asset). FindObjectsOfTypeAll includes assets, so we filter by scene validity.
        internal static IEnumerable<AIAgent> LiveMinions()
        {
            AIAgent[] agents;
            try { agents = Resources.FindObjectsOfTypeAll<AIAgent>(); }
            catch (Exception e) { WarnOnce("FindObjectsOfTypeAll<AIAgent> failed", e); yield break; }

            foreach (var a in agents)
            {
                if (a == null) continue;
                Unit u;
                try
                {
                    if (!a.gameObject.scene.IsValid()) continue;   // skip prefab / asset instances
                    u = a.Unit;
                    if (u == null || u.Owner == null) continue;    // minions only (owned units)
                }
                catch { continue; }
                yield return a;
            }
        }

        private static bool _warned;
        internal static void WarnOnce(string what, Exception e)
        {
            if (_warned) return;
            _warned = true;
            Log?.LogWarning($"[minion] {what}: {e.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Shared reflection + helpers.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    internal static class MinionOps
    {
        // Vision.refreshDelay is a private [SerializeField] float ON Vision (not the base scanner).
        internal static readonly FieldInfo RefreshDelayF = AccessTools.Field(typeof(Vision), "refreshDelay");
        // AIAgent's private serialized reference to its Vision, and its private visible-enemy set.
        internal static readonly FieldInfo VisionF = AccessTools.Field(typeof(AIAgent), "vision");
        internal static readonly FieldInfo VisibleEnemiesF = AccessTools.Field(typeof(AIAgent), "visibleEnemies");
        internal static readonly FieldInfo VisibleEnemyCountF = AccessTools.Field(typeof(AIAgent), "visibleEnemyCount");
        // MoveAroundOwnerAction's private owner reference + orbit distance — for the hold redirect.
        internal static readonly FieldInfo MoveAroundUnitF = AccessTools.Field(typeof(MoveAroundOwnerAction), "unit");
        internal static readonly FieldInfo MoveAroundDistF = AccessTools.Field(typeof(MoveAroundOwnerAction), "distance");
        // OwnerIsWithinRangeCondition's private unit + maxDistance — to re-home "owner in range" onto the hold point.
        internal static readonly FieldInfo OwnerRangeUnitF = AccessTools.Field(typeof(OwnerIsWithinRangeCondition), "unit");
        internal static readonly FieldInfo OwnerRangeMaxF  = AccessTools.Field(typeof(OwnerIsWithinRangeCondition), "maxDistance");
        // SpawnMinionModule's private connection-type — to count this slot's minions for the "at max" check.
        internal static readonly FieldInfo SpawnConnTypeF = AccessTools.Field(typeof(SpawnMinionModule), "spawnMinionsConnectionType");
        // Unit's private serialized power fields (default 10 / 100 unless the prefab overrides them).
        internal static readonly FieldInfo PowerLevelF = AccessTools.Field(typeof(Unit), "powerLevel");
        internal static readonly FieldInfo MaxPowerF = AccessTools.Field(typeof(Unit), "maxPowerLevelToFight");
        // StateMachine's private active-state field (a State; its GameObject name is the state name).
        internal static readonly FieldInfo CurrentStateF = AccessTools.Field(typeof(StateMachine), "currentState");

        // The active behaviour-state name(s) for a drone: walk its StateMachine(s) that are currently
        // active in the hierarchy and read their currentState. Nested machines are joined "A > B".
        internal static string GetStateChain(Component root)
        {
            try
            {
                if (root == null) return "?";
                var names = new List<string>();
                foreach (var sm in root.GetComponentsInChildren<StateMachine>(true))
                {
                    if (sm == null || !sm.isActiveAndEnabled) continue;   // stale currentState on inactive machines
                    if (CurrentStateF?.GetValue(sm) is State cs && cs != null) names.Add(cs.name);
                }
                return names.Count > 0 ? string.Join(" > ", names) : "?";
            }
            catch { return "?"; }
        }

        // Transition/StateMachine private serialized fields, for dumping a drone's behaviour tree.
        internal static readonly FieldInfo StartStateF = AccessTools.Field(typeof(StateMachine), "startState");
        internal static readonly FieldInfo TransStateF = AccessTools.Field(typeof(Transition), "state");
        internal static readonly FieldInfo TransCondsF = AccessTools.Field(typeof(Transition), "conditions");
        internal static readonly FieldInfo TransModeF  = AccessTools.Field(typeof(Transition), "evaluationMode");

        private static string StateName(State s) { try { return s != null ? s.name : "?"; } catch { return "?"; } }

        // Walk a drone's StateMachine(s) at RUNTIME and log the full behaviour tree — each state's
        // Actions and its Transitions (target state + gating Conditions). Runtime carries the merged
        // prefab-variant hierarchy that static prefab extraction can't see.
        internal static void DumpStateMachines(Component root)
        {
            var machines = root.GetComponentsInChildren<StateMachine>(true);
            if (machines.Length == 0) { Plugin.Log.LogInfo("[sm]   (no StateMachine under this unit)"); return; }
            foreach (var sm in machines)
            {
                string start = StateName(StartStateF?.GetValue(sm) as State);
                string cur = sm.isActiveAndEnabled ? StateName(CurrentStateF?.GetValue(sm) as State) : "(inactive)";
                Plugin.Log.LogInfo($"[sm] StateMachine '{sm.name}'  start={start}  current={cur}");
                foreach (Transform childT in sm.transform)
                {
                    var st = childT.gameObject;
                    var actions = new List<string>();
                    foreach (var c in st.GetComponents<Component>())
                        if (c != null && c.GetType().Name.EndsWith("Action")) actions.Add(c.GetType().Name);
                    Plugin.Log.LogInfo($"[sm]   State: {st.name}" + (actions.Count > 0 ? "  Actions: " + string.Join(", ", actions) : ""));
                    foreach (var tr in st.GetComponents<Transition>())
                    {
                        string tgt = StateName(TransStateF?.GetValue(tr) as State);
                        var conds = new List<string>();
                        var list = TransCondsF?.GetValue(tr) as List<Condition>;
                        if (list != null)
                            foreach (var co in list)
                                if (co != null) conds.Add((co.IsInverted ? "!" : "") + co.GetType().Name);
                        string mode = TransModeF?.GetValue(tr)?.ToString() ?? "And";
                        string cs = conds.Count > 0 ? " [" + string.Join($" {mode} ", conds) + "]" : " [always]";
                        Plugin.Log.LogInfo($"[sm]     -> {tgt}{cs}");
                    }
                }
            }
        }

        // The Vision on this agent — its serialized field first, else a child-component lookup.
        internal static Vision GetVision(AIAgent a)
        {
            if (a == null) return null;
            try
            {
                var v = VisionF?.GetValue(a) as Vision;
                if (v != null) return v;
                return a.GetComponentInChildren<Vision>();
            }
            catch { return null; }
        }

        internal static float GetRefreshDelay(Vision v)
        {
            try { return v != null && RefreshDelayF != null ? (float)RefreshDelayF.GetValue(v) : float.NaN; }
            catch { return float.NaN; }
        }

        internal static bool SetRefreshDelay(Vision v, float value)
        {
            try
            {
                if (v == null || RefreshDelayF == null) return false;
                RefreshDelayF.SetValue(v, value);
                return true;
            }
            catch (Exception e) { Plugin.WarnOnce("failed to set refreshDelay", e); return false; }
        }

        internal static int GetInt(FieldInfo f, Unit u, int fallback)
        {
            try { return f != null && u != null ? (int)f.GetValue(u) : fallback; }
            catch { return fallback; }
        }

        internal static void DumpValues(string tag, Unit u, Vision v, AIAgent a)
        {
            try
            {
                string name = u != null ? u.name : (a != null ? a.name : "<null>");
                float range = v != null ? v.Range : float.NaN;
                float delay = GetRefreshDelay(v);
                int power = GetInt(PowerLevelF, u, -1);
                int maxPow = GetInt(MaxPowerF, u, -1);
                string faction = "<none>";
                try { if (u != null && u.Faction != null) faction = u.Faction.name; } catch { }
                string target = "<none>";
                try { if (a != null && a.HasTarget && a.Target != null) target = a.Target.name; } catch { }

                Plugin.Log.LogInfo($"[minion:{tag}] {name}  Vision.Range={range:0.###}  refreshDelay={delay:0.###}  " +
                                   $"powerLevel={power}  maxPowerLevelToFight={maxPow}  faction={faction}  " +
                                   $"visibleEnemies={SafeEnemyCount(a)}  currentTarget={target}");
            }
            catch (Exception e) { Plugin.WarnOnce("dump failed", e); }
        }

        private static int SafeEnemyCount(AIAgent a)
        {
            try
            {
                var set = VisibleEnemiesF?.GetValue(a) as HashSet<Unit>;
                return set?.Count ?? -1;
            }
            catch { return -1; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Spawn hook: Unit.SetOwner(SavableEntity, OwnerConnectionType) fires the moment a minion is
    //  assigned to its owner. Postfix it to catch each new minion, dump its live Vision, and (if
    //  enabled) apply the override values.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(Unit), nameof(Unit.SetOwner), new Type[] { typeof(SavableEntity), typeof(Unit.OwnerConnectionType) })]
    internal static class SpawnPatch
    {
        private static void Postfix(Unit __instance, SavableEntity entity)
        {
            if (__instance == null) return;
            try
            {
                var vision = __instance.GetComponentInChildren<Vision>();
                var agent = __instance.GetComponentInChildren<AIAgent>();

                if (Plugin.DumpOnSpawn.Value)
                    MinionOps.DumpValues("spawn", __instance, vision, agent);

                // Register for the hold-position command (+ owner-damage retaliation). Derives owner,
                // connection type and the damage hook from the drone itself, so it works the same for a
                // fresh spawn and a restored-from-save drone. New drones always start following (not held).
                if (agent != null)
                    SitManager.Register(agent);

                // Apply whichever vision values are tuned (>= 0). Untuned (-1) leaves the built-in value.
                if (vision != null && (Plugin.VisionRange.Value >= 0f || Plugin.RefreshDelay.Value >= 0f))
                {
                    if (Plugin.VisionRange.Value >= 0f) vision.Range = Plugin.VisionRange.Value;
                    if (Plugin.RefreshDelay.Value >= 0f) MinionOps.SetRefreshDelay(vision, Plugin.RefreshDelay.Value);
                    Plugin.Log.LogInfo($"[minion:spawn] applied vision tuning to {__instance.name}: " +
                                       $"Range={(Plugin.VisionRange.Value >= 0f ? Plugin.VisionRange.Value.ToString("0.###") : "built-in")}, " +
                                       $"refreshDelay={(Plugin.RefreshDelay.Value >= 0f ? Plugin.RefreshDelay.Value.ToString("0.###") : "built-in")}.");
                }
            }
            catch (Exception e) { Plugin.WarnOnce("SetOwner postfix failed", e); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Target-nearest: prefix AIAgent.SelectTargetFromVisibleEnemies. When TargetNearest is on AND
    //  the agent's Unit is owned (a minion), pick the nearest visible enemy and skip the original
    //  arbitrary-First() pick. Enemy targeting (Owner == null) is never touched.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(AIAgent), "SelectTargetFromVisibleEnemies")]
    internal static class TargetPatch
    {
        // return false -> skip original (we set the target); return true -> run original unchanged.
        private static bool Prefix(AIAgent __instance)
        {
            try
            {
                if (Plugin.TargetNearest == null || !Plugin.TargetNearest.Value) return true;

                var self = __instance?.Unit;
                if (self == null || self.Owner == null) return true;   // minions only

                var enemies = MinionOps.VisibleEnemiesF?.GetValue(__instance) as HashSet<Unit>;
                if (enemies == null || enemies.Count == 0) return true; // nothing to do; let original no-op

                Vector2 pos = __instance.Position;
                Unit nearest = null;
                float best = float.PositiveInfinity;
                foreach (var e in enemies)
                {
                    if (e == null) continue;
                    float d = ((Vector2)e.transform.position - pos).sqrMagnitude;
                    if (d < best) { best = d; nearest = e; }
                }

                if (nearest == null) return true;
                __instance.SetTarget(nearest);
                return false;   // handled — skip the game's First() pick
            }
            catch (Exception e)
            {
                Plugin.WarnOnce("SelectTargetFromVisibleEnemies prefix failed", e);
                return true;    // on any error, fall back to the original behaviour
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Fix #1 — owner-threat targeting. Postfix AIAgent.RefreshEnemyAndFriendLists (runs each frame just
    //  before target selection). For a DRONE (Owner != null), promote any visible unit that is hostile to
    //  the OWNER into the visibleEnemies set, so the drone will target it — even if it's faction-neutral to
    //  the drone. "Hostile to owner" = the unit's own enemy-blacklist contains the owner's instanceId
    //  (e.g. a crawler you provoked). Faction enemies are already handled by the game. Drones only.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    [HarmonyPatch(typeof(AIAgent), "RefreshEnemyAndFriendLists")]
    internal static class OwnerThreatTargeting
    {
        private static void Postfix(AIAgent __instance)
        {
            try
            {
                if (Plugin.DefendOwner == null || !Plugin.DefendOwner.Value) return;

                var self = __instance?.Unit;
                var owner = self != null ? self.Owner : null;
                if (owner == null) return;                          // minions only
                int ownerId = owner.instanceId;

                if (!(MinionOps.VisibleEnemiesF?.GetValue(__instance) is HashSet<Unit> vis)) return;
                var vision = MinionOps.GetVision(__instance);
                if (vision == null) return;

                bool added = false;
                foreach (var u in vision.VisibleUnits)
                {
                    if (u == null || vis.Contains(u)) continue;
                    AIAgent ua = null;
                    try { ua = u.GetComponent<AIAgent>(); } catch { }
                    var data = ua != null ? ua.ComponentData : null;      // AIAgent.Data (public)
                    if (data?.enemyBlackList == null) continue;
                    if (data.enemyBlackList.Contains(ownerId))            // this unit is angry at our owner
                    {
                        vis.Add(u);
                        added = true;
                    }
                }
                if (added) MinionOps.VisibleEnemyCountF?.SetValue(__instance, vis.Count);
            }
            catch (Exception e) { Plugin.WarnOnce("owner-threat targeting failed", e); }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    //  Hold-position ("sit here") command. Re-pressing the spawn button at max drones fires
    //  Unit.TriggerMaximumMinionReached; we treat that as a toggle: hold the owner's drones near where
    //  the player is, or release them back to following. A held drone still chases/attacks (we don't
    //  touch it while it has a target); when idle it steers back toward the hold point via
    //  UnitMovement.MoveTo, and the owner-orbit action is suppressed so the two don't fight.
    // ─────────────────────────────────────────────────────────────────────────────────────────────
    internal static class SitManager
    {
        private sealed class Reg
        {
            public AIAgent agent; public Unit unit; public Unit owner; public UnitMovement movement;
            public Rigidbody2D rb; public int id; public Unit.OwnerConnectionType connection;   // rb: for the direct hold drive
        }
        private static readonly List<Reg> _drones = new List<Reg>();
        private static readonly Dictionary<int, Vector2> _sit = new Dictionary<int, Vector2>();   // drone id -> hold point

        internal static int HoldingCount => _sit.Count;

        private static int InstanceId(Unit u)
        {
            try { return (u != null && u.ComponentData != null && u.ComponentData.entity != null) ? u.ComponentData.entity.instanceId : 0; }
            catch { return 0; }
        }

        private static int OwnerId(Unit drone)
        {
            try { return (drone != null && drone.Owner != null) ? drone.Owner.instanceId : 0; }
            catch { return 0; }
        }

        // Resolve the owner's Unit (for a reliable live transform) from the drone's Owner EntityData.
        private static Unit ResolveOwnerUnit(Unit drone)
        {
            try
            {
                int oid = OwnerId(drone);
                if (oid == 0) return null;
                foreach (var un in Resources.FindObjectsOfTypeAll<Unit>())
                {
                    if (un == null) continue;
                    try { if (un.gameObject.scene.IsValid() && InstanceId(un) == oid) return un; } catch { }
                }
            }
            catch { }
            return null;
        }

        // Register a drone — fresh spawn OR restored-from-save. Everything is derived from the drone
        // itself (owner, connection type, owner-damage hook), so both paths behave identically. Idempotent.
        internal static void Register(AIAgent agent)
        {
            try
            {
                if (agent == null) return;
                var u = agent.Unit; if (u == null || u.Owner == null) return;   // minions only
                int id = InstanceId(u); if (id == 0) return;
                if (_drones.Exists(r => r.id == id)) return;   // already tracked

                var ownerUnit = ResolveOwnerUnit(u);
                var conn = Unit.OwnerConnectionType.Undefined;
                try { conn = u.ComponentData.ConnectionToOwner; } catch { }

                _drones.Add(new Reg
                {
                    agent = agent, unit = u, owner = ownerUnit,
                    movement = u.GetComponentInChildren<UnitMovement>(true),
                    rb = u.GetComponentInChildren<Rigidbody2D>(true),
                    id = id, connection = conn
                });
                SubscribeOwnerDamage(agent, ownerUnit);

                // Auto-dump the first drone's state machine once, so the behaviour graph (re-group/idle/
                // etc.) lands in the log without needing the Alt+S hotkey.
                if (!_dumpedSM)
                {
                    _dumpedSM = true;
                    try
                    {
                        Plugin.Log.LogInfo($"[sm] ===== auto-dump (first drone): {u.name} =====");
                        MinionOps.DumpStateMachines(u);
                        Plugin.Log.LogInfo("[sm] ===== end =====");
                    }
                    catch { }
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit register failed", e); }
        }
        private static bool _dumpedSM;

        // Owner-damage retaliation (DefendOwner fix #2): when the owner is hit, this drone gets angry at
        // the attacker. Driven off the resolved owner so it works for restored drones too.
        private static void SubscribeOwnerDamage(AIAgent agent, Unit ownerUnit)
        {
            try
            {
                if (agent == null || ownerUnit == null) return;
                var dr = ownerUnit.GetComponentInChildren<DamagableResource>(true);
                if (dr == null || dr.onGotAttacked == null) return;
                dr.onGotAttacked.AddListener((Unit attacker) =>
                {
                    if (Plugin.DefendOwner != null && Plugin.DefendOwner.Value && agent != null && attacker != null)
                        try { agent.HandleHitBy(attacker); } catch { }
                });
            }
            catch (Exception e) { Plugin.WarnOnce("owner-damage hook failed", e); }
        }

        // Pick up drones the spawn hook never saw — restored-from-save drones (re-owned via the nested
        // Unit.Data.SetOwner, which we don't patch) and any we missed. Throttled from Tick; Register is
        // idempotent so re-scanning is cheap and safe.
        private static void DiscoverDrones()
        {
            try
            {
                foreach (var a in Resources.FindObjectsOfTypeAll<AIAgent>())
                {
                    if (a == null) continue;
                    try
                    {
                        if (!a.gameObject.scene.IsValid()) continue;
                        var u = a.Unit;
                        if (u == null || u.Owner == null) continue;              // minions only
                        if (_drones.Exists(r => r.id == InstanceId(u))) continue; // already tracked
                        Register(a);
                    }
                    catch { }
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit discover failed", e); }
        }

        internal static bool IsSitting(Unit u)
        {
            int id = InstanceId(u);
            return id != 0 && _sit.ContainsKey(id);
        }

        internal static Vector2 GetHoldPos(Unit u) => _sit.TryGetValue(InstanceId(u), out var p) ? p : (Vector2)u.transform.position;

        // Debounce keyed per (owner, connection): the spawn button fires Activate every frame it's held,
        // so only the first press in a burst toggles. Keying by owner+connection means multiple players —
        // and multiple drone modules on ONE player — never swallow each other's toggles.
        private static readonly Dictionary<(int owner, int conn), float> _lastToggle = new Dictionary<(int, int), float>();
        internal static void ToggleDebounced(Unit owner, Unit.OwnerConnectionType ct)
        {
            var key = (InstanceId(owner), (int)ct);
            float now = Time.unscaledTime;
            if (!_lastToggle.TryGetValue(key, out var last) || now - last > 0.25f) Toggle(owner, ct);
            _lastToggle[key] = now;
        }

        // Toggle hold for ONLY this owner's drones of THIS connection type — the drones from the module
        // whose button was pressed. So one player's press never touches another player's drones, and a
        // player with multiple drone modules holds/releases each group independently.
        internal static void Toggle(Unit owner, Unit.OwnerConnectionType ct)
        {
            try
            {
                if (owner == null) return;
                Prune();
                int oid = InstanceId(owner);
                var mine = _drones.FindAll(r => r.connection == ct && OwnerId(r.unit) == oid);
                if (mine.Count == 0) return;
                if (mine.Exists(r => _sit.ContainsKey(r.id)))
                {
                    foreach (var r in mine) _sit.Remove(r.id);
                    Plugin.PlayRelease();
                    HoldHighlight.Set(owner, ct, false);
                    Plugin.Log.LogInfo($"[sit] released {mine.Count} drone(s) [{ct}] — following owner again.");
                }
                else
                {
                    Vector2 pos = owner.transform.position;
                    foreach (var r in mine) _sit[r.id] = pos;
                    Plugin.PlayEngage();
                    HoldHighlight.Set(owner, ct, true);
                    Plugin.Log.LogInfo($"[sit] {mine.Count} drone(s) [{ct}] holding near ({pos.x:0.#}, {pos.y:0.#}).");
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit toggle failed", e); }
        }

        private static void Prune()
        {
            for (int i = _drones.Count - 1; i >= 0; i--)
                if (_drones[i].agent == null || _drones[i].unit == null) { _sit.Remove(_drones[i].id); _drones.RemoveAt(i); }
        }

        // The drone travels to and orbits the hold point via the game's own follow movement (see
        // SitRedirectOrbit). Tick discovers restored drones (throttled), auto-releases when the owner
        // wanders too far, and prunes dead ones.
        private static float _nextDiscover;
        internal static void Tick()
        {
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value) return;
                if (Time.unscaledTime >= _nextDiscover) { _nextDiscover = Time.unscaledTime + 1f; DiscoverDrones(); }
                Prune();
                if (_sit.Count == 0) return;
                // Re-homing the follow logic (SitRedirectOrbit + HoldOwnerRange) keeps the drone AT the hold
                // point instead of chasing the owner, but the drone's own push-movement is too lazy to get
                // there fast or exactly. So while it isn't fighting, we drive its rigidbody straight to the
                // point (fast approach + exact snap). Tick also auto-releases when the real owner is far.
                float release = Plugin.SitReleaseDistance.Value;
                float dead = Plugin.SitDeadzone.Value;
                float speed = Plugin.SitHoldSpeed.Value;
                float dt = Time.deltaTime;
                foreach (var r in _drones)
                {
                    if (!_sit.ContainsKey(r.id) || r.unit == null) continue;
                    Vector2 hold = _sit[r.id];
                    Vector2 dronePos = r.unit.transform.position;

                    Vector2 ownerPos = r.owner != null ? (Vector2)r.owner.transform.position
                                     : (r.unit.Owner != null ? (Vector2)r.unit.Owner.position : dronePos);
                    if (Vector2.Distance(ownerPos, dronePos) > release)
                    {
                        _sit.Remove(r.id);   // owner too far -> auto-follow again
                        Plugin.PlayRelease();
                        HoldHighlight.Set(r.owner, r.connection, false);
                        continue;
                    }

                    bool fighting = r.agent != null && r.agent.HasTarget;
                    if (!fighting && r.rb != null)
                    {
                        Vector2 toHold = hold - dronePos;
                        float d = toHold.magnitude;
                        if (d > dead) r.rb.velocity = toHold.normalized * speed;               // decisive travel to the spot
                        else { r.rb.velocity = Vector2.zero; r.rb.position = Vector2.MoveTowards(r.rb.position, hold, speed * dt); }  // snap exactly
                    }
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit tick failed", e); }
        }
    }

    // Amber highlight on the drone's ability-slot button, on the HUD of the specific owner that's
    // holding. Maps (owner, connection type) -> that owner's ShipHud -> the matching AbilitySlot, and
    // toggles an amber border overlay. Per-player: only the holding player's own drone button lights up.
    internal static class HoldHighlight
    {
        private static readonly Color Amber = new Color(1f, 0.55f, 0.06f, 1f);
        private const float Thickness = 4f;   // border strip width, px
        private const float Inset = 10f;      // pull the frame in from the icon rect so it hugs the octagon

        // Keyed by (owner instanceId, connection type) -> our amber frame parented onto that button.
        // The frame is parented to the button (not the drone) so it survives drone death/respawn.
        private static readonly Dictionary<(int, int), GameObject> _frames = new Dictionary<(int, int), GameObject>();

        internal static void Set(Unit owner, Unit.OwnerConnectionType ct, bool on)
        {
            try
            {
                int oid = OwnerId(owner);
                if (oid == 0) return;
                var key = (oid, (int)ct);

                if (on)
                {
                    if (_frames.TryGetValue(key, out var existing) && existing != null)
                    {
                        existing.SetActive(true);
                        return;
                    }
                    // The button's own "Highlight" is the FIRST child of Visual -> renders BEHIND the opaque
                    // slot background + icon, so recolouring it shows nothing; the game only reveals it by
                    // scaling (the screen-fill we saw via SetHighlighted's animator). So we build our own
                    // top-most amber frame instead: a hollow 4-strip border on the LAST sibling, sized to
                    // the icon. Nothing animates it, so it just stays until we hide it.
                    var slot = FindSlot(owner, ct);
                    var parent = slot != null && slot.icon != null ? FindVisual(slot.icon.transform) : null;
                    if (parent == null) return;
                    _frames[key] = BuildFrame(parent);
                }
                else
                {
                    if (_frames.TryGetValue(key, out var f) && f != null) f.SetActive(false);
                }
            }
            catch (Exception e) { Plugin.WarnOnce("hold highlight failed", e); }
        }

        // Hollow amber frame stretched over the icon: four solid strips (top/bottom/left/right) anchored
        // to the parent's edges, so it's exactly the button's size regardless of resolution.
        private static GameObject BuildFrame(RectTransform parent)
        {
            var root = new GameObject("ModHoldBorder", typeof(RectTransform));
            var rt = root.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(Inset, Inset); rt.offsetMax = new Vector2(-Inset, -Inset);   // compress onto the octagon
            rt.SetAsLastSibling();   // draw on top of the icon layers

            // (anchorMin, anchorMax, offsetMin, offsetMax) per edge strip.
            AddStrip(rt, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -Thickness), new Vector2(0, 0)); // top
            AddStrip(rt, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, Thickness));  // bottom
            AddStrip(rt, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0), new Vector2(Thickness, 0));  // left
            AddStrip(rt, new Vector2(1, 0), new Vector2(1, 1), new Vector2(-Thickness, 0), new Vector2(0, 0)); // right
            return root;
        }

        private static void AddStrip(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
        {
            var go = new GameObject("edge", typeof(RectTransform), typeof(Image));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = aMin; rt.anchorMax = aMax;
            rt.offsetMin = oMin; rt.offsetMax = oMax;
            var img = go.GetComponent<Image>();
            img.sprite = null;            // null sprite -> Image draws a solid amber quad
            img.color = Amber;
            img.raycastTarget = false;
        }

        private static int OwnerId(Unit owner)
        {
            try { return owner != null && owner.ComponentData?.entity != null ? owner.ComponentData.entity.instanceId : 0; }
            catch { return 0; }
        }

        // The "Visual" child holds the layered icon graphics (102x102); fall back to the icon root.
        private static RectTransform FindVisual(Transform iconRoot)
        {
            if (iconRoot == null) return null;
            var v = iconRoot.Find("Visual") as RectTransform;
            return v != null ? v : iconRoot as RectTransform;
        }

        private static AbilitySlot FindSlot(Unit owner, Unit.OwnerConnectionType ct)
        {
            int oid = 0;
            try { oid = owner != null && owner.ComponentData != null && owner.ComponentData.entity != null ? owner.ComponentData.entity.instanceId : 0; } catch { }
            if (oid == 0) return null;

            foreach (var hud in Resources.FindObjectsOfTypeAll<ShipHud>())
            {
                if (hud == null) continue;
                try
                {
                    if (!hud.gameObject.scene.IsValid() || hud.Ship == null || hud.abilitySlotsPanel == null) continue;
                    var u = hud.Ship.GetComponent<Unit>();
                    if (u == null || u.ComponentData?.entity?.instanceId != oid) continue;
                    var p = hud.abilitySlotsPanel;
                    switch (ct)
                    {
                        case Unit.OwnerConnectionType.PrimaryWeapon:   return p.primaryWeaponSlot;
                        case Unit.OwnerConnectionType.SecondaryWeapon: return p.secondaryWeaponSlot;
                        case Unit.OwnerConnectionType.Active1:         return p.activeSlot1;
                        case Unit.OwnerConnectionType.Active2:         return p.activeSlot2;
                        case Unit.OwnerConnectionType.Active3:         return p.activeSlot3;
                        default: return null;
                    }
                }
                catch { }
            }
            return null;
        }

    }

    // Re-pressing the spawn button while already at max drones for this slot toggles hold-position.
    // We prefix Activate itself (which runs on every press, BEFORE the resource check that would
    // otherwise swallow it with an "insufficient" sound), debounce so a held button = one toggle, and
    // return false at-max to suppress the vanilla can't-spawn feedback.
    [HarmonyPatch(typeof(SpawnMinionModule), nameof(SpawnMinionModule.Activate))]
    internal static class SitTogglePatch
    {
        private static bool Prefix(SpawnMinionModule __instance, Unit owner)
        {
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value || owner == null || owner.ComponentData == null)
                    return true;
                var ct = (Unit.OwnerConnectionType)(MinionOps.SpawnConnTypeF?.GetValue(__instance) ?? Unit.OwnerConnectionType.Undefined);
                int count = 0;
                foreach (var m in owner.ComponentData.Minions)
                    if (m != null && m.ConnectionToOwner == ct) count++;
                if (count > 0 && count >= __instance.Level)   // at max for this drone slot
                {
                    SitManager.ToggleDebounced(owner, ct);   // scope the toggle to THIS module's drones
                    return false;   // this press is a hold toggle — skip vanilla spawn/block feedback
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit activate hook failed", e); }
            return true;   // not at max -> spawn normally
        }
    }

    // Re-home the drone's follow movement onto the HOLD POINT. MoveAroundOwnerAction (used by both the
    // Idle and Regroup states) orbits unit.Owner.position at a random `distance`. While holding, we
    // briefly override the owner position to the hold point AND the orbit distance to ~0 during FindPath,
    // so the drone paths to the EXACT hold point using its own proven movement (restored right after).
    [HarmonyPatch(typeof(MoveAroundOwnerAction), "FindPath")]
    internal static class SitRedirectOrbit
    {
        private static void Prefix(MoveAroundOwnerAction __instance, out object[] __state)
        {
            __state = null;
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value || SitManager.HoldingCount == 0) return;
                var unit = MinionOps.MoveAroundUnitF?.GetValue(__instance) as Unit;
                if (unit == null || !SitManager.IsSitting(unit)) return;
                var owner = unit.Owner;
                if (owner == null) return;

                Vector2 hold = SitManager.GetHoldPos(unit);
                object oldDist = MinionOps.MoveAroundDistF?.GetValue(__instance);
                // Zeroed MinMaxFloat -> RandomInRange() == 0 -> the orbit target collapses onto the point.
                object zeroDist = MinionOps.MoveAroundDistF != null ? Activator.CreateInstance(MinionOps.MoveAroundDistF.FieldType) : null;

                __state = new object[] { owner, owner.position, oldDist };
                owner.position = new Vector3(hold.x, hold.y, owner.position.z);
                if (zeroDist != null) MinionOps.MoveAroundDistF.SetValue(__instance, zeroDist);
            }
            catch { __state = null; }
        }

        // Finalizer always runs (even on exception) so the owner position + orbit distance are restored.
        private static void Finalizer(MoveAroundOwnerAction __instance, object[] __state)
        {
            try
            {
                if (__state == null) return;
                if (__state[0] is EntityData owner) owner.position = (Vector3)__state[1];
                if (__state[2] != null) MinionOps.MoveAroundDistF?.SetValue(__instance, __state[2]);
            }
            catch { }
        }
    }

    // Re-home "is the owner within range?" onto the hold point for a holding drone. The Idle/Regroup
    // transitions key off this; pointing it at the hold point means the drone regards the HOLD POINT as
    // home (and never tries to Regroup back to the real owner), so it settles there instead of chasing you.
    [HarmonyPatch(typeof(OwnerIsWithinRangeCondition), "IsFulfilled")]
    internal static class HoldOwnerRange
    {
        private static bool Prefix(OwnerIsWithinRangeCondition __instance, ref bool __result)
        {
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value || SitManager.HoldingCount == 0) return true;
                var unit = MinionOps.OwnerRangeUnitF?.GetValue(__instance) as Unit;
                if (unit == null || !SitManager.IsSitting(unit)) return true;
                Vector2 hold = SitManager.GetHoldPos(unit);
                float maxD = MinionOps.OwnerRangeMaxF != null ? (float)MinionOps.OwnerRangeMaxF.GetValue(__instance) : 0f;
                __result = ((Vector2)unit.transform.position - hold).sqrMagnitude < maxD * maxD;
                return false;   // skip original (which measures against the real owner)
            }
            catch { }
            return true;
        }
    }
}
