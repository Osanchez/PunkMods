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
    //  Everything is in-game (hotkeys) — this tool deliberately registers NOTHING in the Mods menu.
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
            RangeStep = cfg.Bind("Steps", "RangeStep", 5f,
                "How much the Numpad +/- hotkeys change the sight range per press.");
            DelayStep = cfg.Bind("Steps", "DelayStep", 0.05f,
                "How much the Numpad * // hotkeys change the scan interval per press.");
            ShowOverlay = cfg.Bind("Overlay", "ShowOverlay", true,
                "Show the on-screen HUD with current drone settings + toggle states. Toggle in-game with Alt+O.");

            SetupAudio();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(SpawnPatch));
            _harmony.PatchAll(typeof(TargetPatch));
            _harmony.PatchAll(typeof(OwnerThreatTargeting));
            _harmony.PatchAll(typeof(SitTogglePatch));
            _harmony.PatchAll(typeof(SitRedirectOrbit));

            Log.LogInfo($"{Name} v{Version} loaded. TargetNearest={TargetNearest.Value}, vision tuning " +
                        $"{(VisionRange.Value >= 0f || RefreshDelay.Value >= 0f ? "active" : "off (built-in)")}. " +
                        "Keys (hold Left Alt): O overlay; D dump live; S statemachine; R roster; =/- range; ]/[ delay; 0 reset vision; N nearest; B defend-owner; H hold-cmd. Re-press spawn = hold/release.");
        }

        // Hot-reload teardown: undo patches.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch { }
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

                int n = 0; string sample = "";
                foreach (var a in LiveMinions())
                {
                    var v = MinionOps.GetVision(a);
                    if (v == null) continue;
                    n++;
                    if (n == 1)
                    {
                        string tgt = "none";
                        try { if (a.HasTarget && a.Target != null) tgt = a.Target.name; } catch { }
                        string state = MinionOps.GetStateChain(a.Unit);
                        string hold = SitManager.IsSitting(a.Unit) ? "HOLD" : "follow";
                        sample = $"mode={hold}  state={state}  target={tgt}  range={v.Range:0.#} refresh={MinionOps.GetRefreshDelay(v):0.###}s";
                    }
                }

                _overlayLines = new List<string>
                {
                    "<b><color=#EBA845>MINION TUNING</color></b>   <size=11>(Alt+O hide)</size>",
                    $"Target nearest : {(TargetNearest.Value ? on : off)}   <size=11>Alt+N</size>",
                    $"Defend owner   : {(DefendOwner.Value ? on : off)}   <size=11>Alt+B (blacklist-driven)</size>",
                    $"Hold command   : {(SitCommand.Value ? on : off)}   <size=11>Alt+H · re-press spawn · {SitManager.HoldingCount} holding</size>",
                    $"Sight range    : {rng}   <size=11>Alt +/- (step {RangeStep.Value:0.###})</size>",
                    $"Scan delay     : {dly}   <size=11>Alt ]/[ (step {DelayStep.Value:0.###})</size>",
                    $"Live drones    : {n}" + (n > 0 ? $"   <size=11>[{sample}]</size>" : ""),
                    "<size=11><color=#9a9a9a>Alt+0 reset · Alt+D dump · Alt+R roster</color></size>",
                };
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
        // MoveAroundOwnerAction's private owner reference — used to detect a held drone and suppress orbit.
        internal static readonly FieldInfo MoveAroundUnitF = AccessTools.Field(typeof(MoveAroundOwnerAction), "unit");
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

                // Register for the hold-position command; new drones always start following (not held).
                if (agent != null)
                    SitManager.Register(agent, entity != null ? entity.GetComponentInChildren<Unit>(true) : null);

                // Apply whichever vision values are tuned (>= 0). Untuned (-1) leaves the built-in value.
                if (vision != null && (Plugin.VisionRange.Value >= 0f || Plugin.RefreshDelay.Value >= 0f))
                {
                    if (Plugin.VisionRange.Value >= 0f) vision.Range = Plugin.VisionRange.Value;
                    if (Plugin.RefreshDelay.Value >= 0f) MinionOps.SetRefreshDelay(vision, Plugin.RefreshDelay.Value);
                    Plugin.Log.LogInfo($"[minion:spawn] applied vision tuning to {__instance.name}: " +
                                       $"Range={(Plugin.VisionRange.Value >= 0f ? Plugin.VisionRange.Value.ToString("0.###") : "built-in")}, " +
                                       $"refreshDelay={(Plugin.RefreshDelay.Value >= 0f ? Plugin.RefreshDelay.Value.ToString("0.###") : "built-in")}.");
                }

                // Fix #2 — retaliate on owner damage: when the OWNER's health takes a hit, make this drone
                // angry at the attacker (HandleHitBy blacklists + targets it). `entity` is the owner passed
                // to SetOwner; its onGotAttacked event carries the attacking Unit.
                if (agent != null && entity != null)
                {
                    var ownerDmg = entity.GetComponentInChildren<DamagableResource>(true);
                    if (ownerDmg != null && ownerDmg.onGotAttacked != null)
                    {
                        ownerDmg.onGotAttacked.AddListener((Unit attacker) =>
                        {
                            if (Plugin.DefendOwner != null && Plugin.DefendOwner.Value && agent != null && attacker != null)
                                try { agent.HandleHitBy(attacker); } catch { }
                        });
                    }
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
        private sealed class Reg { public AIAgent agent; public Unit unit; public Unit owner; public UnitMovement movement; public int id; }
        private static readonly List<Reg> _drones = new List<Reg>();
        private static readonly Dictionary<int, Vector2> _sit = new Dictionary<int, Vector2>();

        internal static int HoldingCount => _sit.Count;

        private static int InstanceId(Unit u)
        {
            try { return (u != null && u.ComponentData != null && u.ComponentData.entity != null) ? u.ComponentData.entity.instanceId : 0; }
            catch { return 0; }
        }

        internal static void Register(AIAgent agent, Unit owner)
        {
            try
            {
                if (agent == null) return;
                var u = agent.Unit; if (u == null) return;
                int id = InstanceId(u); if (id == 0) return;
                if (_drones.Exists(r => r.id == id)) return;   // already tracked
                _drones.Add(new Reg { agent = agent, unit = u, owner = owner, movement = u.GetComponentInChildren<UnitMovement>(true), id = id });
            }
            catch (Exception e) { Plugin.WarnOnce("sit register failed", e); }
        }

        internal static bool IsSitting(Unit u)
        {
            int id = InstanceId(u);
            return id != 0 && _sit.ContainsKey(id);
        }

        internal static Vector2 GetHoldPos(Unit u) => _sit.TryGetValue(InstanceId(u), out var p) ? p : (Vector2)u.transform.position;

        // Debounce: Activate fires every frame the spawn button is held, so treat a continuous stream of
        // presses as ONE toggle — only act when there was a gap (button released + re-pressed).
        private static float _lastToggle = -999f;
        internal static void ToggleDebounced(Unit owner)
        {
            float now = Time.unscaledTime;
            if (now - _lastToggle > 0.25f) Toggle(owner);
            _lastToggle = now;
        }

        // Toggle hold for all of this owner's drones (any holding -> release all, else -> hold all here).
        internal static void Toggle(Unit owner)
        {
            try
            {
                if (owner == null) return;
                Prune();
                var mine = _drones.FindAll(r => r.owner == owner || (r.unit != null && r.unit.Owner != null && r.unit.Owner.instanceId == InstanceId(owner)));
                if (mine.Count == 0) return;
                if (mine.Exists(r => _sit.ContainsKey(r.id)))
                {
                    foreach (var r in mine) _sit.Remove(r.id);
                    Plugin.PlayRelease();
                    Plugin.Log.LogInfo($"[sit] released {mine.Count} drone(s) — following owner again.");
                }
                else
                {
                    Vector2 pos = owner.transform.position;
                    foreach (var r in mine) _sit[r.id] = pos;
                    Plugin.PlayEngage();
                    Plugin.Log.LogInfo($"[sit] {mine.Count} drone(s) holding near ({pos.x:0.#}, {pos.y:0.#}).");
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
        // SitRedirectOrbit). Tick only handles auto-release when the owner wanders too far, and pruning.
        internal static void Tick()
        {
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value) return;
                Prune();
                if (_sit.Count == 0) return;
                float release = Plugin.SitReleaseDistance.Value;
                foreach (var r in _drones)
                {
                    if (!_sit.ContainsKey(r.id) || r.unit == null || r.owner == null) continue;
                    if (Vector2.Distance((Vector2)r.owner.transform.position, (Vector2)r.unit.transform.position) > release)
                    {
                        _sit.Remove(r.id);   // owner too far -> auto-follow again
                        Plugin.PlayRelease();
                    }
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit tick failed", e); }
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
                    SitManager.ToggleDebounced(owner);
                    return false;   // this press is a hold toggle — skip vanilla spawn/block feedback
                }
            }
            catch (Exception e) { Plugin.WarnOnce("sit activate hook failed", e); }
            return true;   // not at max -> spawn normally
        }
    }

    // While a drone is holding, redirect the owner-orbit target to the HOLD POINT by briefly overriding
    // the owner's stored position during FindPath (restored immediately after). The drone then travels to
    // and orbits the hold point using the game's own follow movement — no fighting the movement system.
    [HarmonyPatch(typeof(MoveAroundOwnerAction), "FindPath")]
    internal static class SitRedirectOrbit
    {
        private static void Prefix(MoveAroundOwnerAction __instance, out object[] __state)
        {
            __state = null;
            try
            {
                if (Plugin.SitCommand == null || !Plugin.SitCommand.Value) return;
                var unit = MinionOps.MoveAroundUnitF?.GetValue(__instance) as Unit;
                if (unit == null || !SitManager.IsSitting(unit)) return;
                var owner = unit.Owner;
                if (owner == null) return;
                Vector2 hold = SitManager.GetHoldPos(unit);
                __state = new object[] { owner, owner.position };                 // save real owner position
                owner.position = new Vector3(hold.x, hold.y, owner.position.z);   // orbit the hold point instead
            }
            catch { __state = null; }
        }

        // Finalizer always runs (even on exception) so the owner's real position is restored.
        private static void Finalizer(object[] __state)
        {
            try { if (__state != null && __state[0] is EntityData owner) owner.position = (Vector3)__state[1]; }
            catch { }
        }
    }
}
