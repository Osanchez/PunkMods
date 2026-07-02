using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PunkFourPlayer
{
    /// <summary>
    /// Phase-1 proof of concept for local 4-player. Rides on the existing co-op flow: you start
    /// a co-op run (pick 2 devices as normal) and this adds players 3-4 — placing/spawning extra
    /// ships, auto-assigning the next free gamepads, cloning HUDs, and assigning themes.
    ///
    /// Scope/limits (PoC): co-op runs only (not single-player). Works for NEW runs and for CONTINUED
    /// co-op saves — on continue the extra ship entities are restored from the suspend-save (with their
    /// persisted builds) but vanilla only spawns GameObjects for the first two, so we spawn the rest.
    /// Extra players are auto-assigned spare GAMEPADS only; if you don't have enough, those ships still
    /// spawn but are uncontrolled — which is fine for proving spawn/camera/economy/HUD flow.
    /// The camera (ProCamera2D) already frames N targets, so 4 ships auto-fit.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.fourplayer";
        public const string Name = "PUNK Four Player (PoC)";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> PlayerCount;

        private void Awake()
        {
            Log = Logger;
            // Per-mod config in this DLL's own folder (BepInEx/plugins/PunkFourPlayer/config.cfg).
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), saveOnInit: true);
            PlayerCount = cfg.Bind("General", "PlayerCount", 4,
                new ConfigDescription("Target local players in a NEW co-op run.", new AcceptableValueRange<int>(2, 4)));
            RumbleConfig.Init(cfg);   // P3/P4 gamepad rumble (also shown as sliders in Gameplay settings)
            new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} v{Version} loaded. Target co-op players: {Target}.");
        }

        internal static int Target => Mathf.Clamp(PlayerCount?.Value ?? 4, 2, 4);
    }

    internal static class Stored
    {
        internal static LoadoutTemplate Loadout;   // captured starting loadout for the extra ships
    }

    // Capture the starting loadout (sync prefix on the async placement method — no async pitfalls).
    [HarmonyPatch(typeof(ShipManager), "PlaceShipEntitiesToStartPosition")]
    internal static class CaptureLoadout
    {
        private static void Prefix(LoadoutTemplate loadoutTemplate) => Stored.Loadout = loadoutTemplate;
    }

    // After the game spawns the vanilla 1-2 ships, place + spawn the extra ships up to Target.
    [HarmonyPatch(typeof(ShipManager), "SpawnShipGameObjects")]
    internal static class SpawnExtraShips
    {
        private static void Postfix(ShipManager __instance, Level level, RunArguments runArguments)
        {
            try
            {
                if (!runArguments.isCoop) return;   // co-op runs only (NEW or CONTINUED)
                bool isContinue = runArguments.isContinue;

                // On a NEW run, drop any slot mapping left in memory from a previous continue (Load,
                // which repopulates it, only runs on continue). On continue it was already read by the
                // GameSaver.Load hook. See SlotRegistry.
                if (!isContinue) SlotRegistry.ResetForNewRun();

                var smT = typeof(ShipManager);
                var sm = __instance;
                var entityManager = AccessTools.Field(smT, "entityManager").GetValue(sm);
                var getShipsM = AccessTools.Method(entityManager.GetType(), "GetShips");

                // How many player ships do we want in total?
                //  - NEW run: prefer the join-screen device picks; else the configured count.
                //  - CONTINUE: however many ship entities the suspend-save restored. On continue the
                //    ship EntityData are reloaded into the EntityManager by GameSaver.Load, but vanilla
                //    ShipManager.SpawnShipGameObjects only ever builds GameObjects for the first two
                //    (list[0]/list[1]) — so a saved P3/P4 exist as *data* but never get a Ship. We
                //    re-spawn GameObjects for every restored ship entity. (A 2-ship save stays 2 — we
                //    never invent players the save didn't contain.)
                var picks = FourPlayerRuntime.SlotDevices;
                int target;
                if (isContinue)
                {
                    int savedShips = (getShipsM.Invoke(entityManager, null) as IEnumerable<EntityData>).Count();
                    target = Mathf.Clamp(savedShips, 2, 4);
                }
                else
                {
                    target = (picks != null && picks.Count >= 2) ? Mathf.Clamp(picks.Count, 2, 4) : Plugin.Target;
                }
                if (target <= 2 || sm.Ships.Count >= target) return;

                var shipsConfig = AccessTools.Field(smT, "shipsConfig").GetValue(sm) as ShipsConfig;
                var placeM = AccessTools.Method(smT, "PlaceShipEntity");
                var spawnM = AccessTools.Method(smT, "Spawn", new[] { typeof(EntityData), typeof(Ship), typeof(InputDevice), typeof(bool) });
                var assignThemeM = AccessTools.Method(smT, "AssignTheme");

                int already = sm.Ships.Count;            // 2 in co-op

                // 1) NEW run only: place the extra ship entities near the start node. On CONTINUE the
                //    extra entities were already restored from the save (with their persisted builds),
                //    so we must NOT place new ones — that would duplicate ships. We only spawn the
                //    GameObjects for the restored entities in step 2 below.
                if (!isContinue)
                {
                    if (Stored.Loadout == null) { Plugin.Log.LogWarning("No starting loadout captured; skipping extras."); return; }
                    int toAdd = target - already;
                    Vector2 c = level.graph.StartNode.center;
                    Vector2[] off = { Vector2.up, Vector2.down, Vector2.left * 2f, Vector2.right * 2f };
                    for (int i = 0; i < toAdd; i++)
                    {
                        var p = c + off[i % off.Length];
                        placeM.Invoke(sm, new object[] { new Vector3(p.x, p.y, 0f), Stored.Loadout });
                    }
                }

                // 2) spawn the ship entities that don't yet have a Ship (robust vs. ordering)
                var alreadySpawned = new HashSet<EntityData>(sm.Ships.Select(s => s.SavableEntity.EntityData));
                var entities = (getShipsM.Invoke(entityManager, null) as IEnumerable<EntityData>)
                    .OrderBy(e => e.instanceId).ToList();
                var devices = ExtraGamepads(runArguments);
                var themes = shipsConfig != null ? shipsConfig.shipThemes : null;
                var prefab = shipsConfig != null ? shipsConfig.AutoSwichShipPrefab : null;

                foreach (var e in entities)
                {
                    if (sm.Ships.Count >= target) break;
                    if (alreadySpawned.Contains(e)) continue;

                    int idx = sm.Ships.Count;                                  // index this ship will take
                    int extraNo = idx - already;                              // 0-based among the extras
                    // Join-screen picks are only meaningful on a NEW run; on continue they'd be stale
                    // (no join screen runs), so fall back to spare gamepads there.
                    InputDevice dev = (!isContinue && picks != null && picks.TryGetValue(idx, out var picked))
                        ? picked
                        : (extraNo < devices.Count ? devices[extraNo] : null);
                    bool isP2 = (idx % 2) == 1;                               // alternate UI side (binary IsPlayerTwo)

                    spawnM.Invoke(sm, new object[] { e, prefab, dev, isP2 });
                    // P3/P4 get their own distinct colors (green/purple, matching the join screen);
                    // beyond that, fall back to cycling the two vanilla themes.
                    ShipTheme theme = ExtraTheme(idx);
                    if (theme == null && themes != null && themes.Length > 0) theme = themes[idx % themes.Length];
                    if (theme != null) assignThemeM.Invoke(sm, new object[] { sm.Ships[idx], theme });
                }

                // ---- Persistent slot remap: make ShipManager.Ships index == the stable player slot ----
                // Every identity source (vanilla AssignHuds -> huds[0]/huds[1]; our HUD clones + "PLAYER N"
                // labels + colors; PunkPlayerHighlight's in-world "PN") keys off the Ships index, so
                // ordering the list by the persistent slot makes them all agree and stay stable across
                // continue. On a NEW run (and legacy saves with no slot file) the slot order already
                // equals today's spawn order, so this is a no-op there — behavior is unchanged; only a
                // remembered continue actually reorders. All failure paths fall back to the spawn order.
                try
                {
                    var slotByShip = SlotRegistry.Resolve(sm.Ships);
                    var shipsList = AccessTools.Field(smT, "ships").GetValue(sm) as List<Ship>;
                    if (shipsList != null)
                    {
                        var desired = shipsList.OrderBy(s => slotByShip.TryGetValue(s, out var sl) ? sl : int.MaxValue).ToList();
                        if (!desired.SequenceEqual(shipsList))
                        {
                            shipsList.Clear();
                            shipsList.AddRange(desired);
                            // Re-apply per-slot VISUAL identity now that index == slot: colour/theme and
                            // the P1/P2-side flag. Devices are intentionally NOT reassigned here: on
                            // continue the game passes null devices to the primaries (auto-switch) and
                            // controllers have no persisted identity, so each pad simply stays on the ship
                            // it was bound to at spawn — which, after this reorder, is a correctly-labelled
                            // ship. (Reassigning by slot risked double-binding one pad to two ships.)
                            for (int slot = 0; slot < shipsList.Count; slot++)
                            {
                                var ship = shipsList[slot];
                                if (ship == null) continue;
                                ship.IsPlayerTwo = (slot % 2) == 1;

                                ShipTheme theme = ExtraTheme(slot);
                                if (theme == null && themes != null && themes.Length > 0) theme = themes[slot % themes.Length];
                                if (theme != null) assignThemeM.Invoke(sm, new object[] { ship, theme });
                            }
                            Plugin.Log.LogInfo("Four-Player: reordered ships to persistent slot order (continue remap applied).");
                        }
                    }
                }
                catch (Exception se) { Plugin.Log.LogWarning($"slot remap failed (keeping spawn order): {se.Message}"); }

                Plugin.Log.LogInfo($"Four-Player{(isContinue ? " (continue)" : "")}: {sm.Ships.Count} ships total; {devices.Count} spare gamepad(s) for extras.");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"SpawnExtraShips failed: {ex}"); }
        }

        private static List<InputDevice> ExtraGamepads(RunArguments args)
        {
            var used = new HashSet<InputDevice>();
            if (args.leftDevice != null) used.Add(args.leftDevice);
            if (args.rightDevice != null) used.Add(args.rightDevice);
            return Gamepad.all.Where(g => !used.Contains(g)).Cast<InputDevice>().ToList();
        }

        // New ship themes (created at runtime, no extra assets needed) so P3/P4 don't reuse P1/P2's
        // colors. Greens/purples match the join screen's P3/P4 label colors.
        private static ShipTheme _p3Theme, _p4Theme;

        private static ShipTheme ExtraTheme(int playerIndex)
        {
            if (playerIndex == 2) return _p3Theme != null ? _p3Theme : (_p3Theme = MakeTheme(new Color(0.49f, 0.86f, 0.49f)));
            if (playerIndex == 3) return _p4Theme != null ? _p4Theme : (_p4Theme = MakeTheme(new Color(0.78f, 0.55f, 0.92f)));
            return null;
        }

        private static ShipTheme MakeTheme(Color c)
        {
            var t = ScriptableObject.CreateInstance<ShipTheme>();
            t.spriteColor = c;
            t.boostParticleColor1 = c;
            t.boostParticleColor2 = Color.Lerp(c, Color.white, 0.4f);
            return t;
        }
    }

    // Give players 3-4 a HUD by cloning the vanilla HUD, mirrored to the TOP corners.
    [HarmonyPatch(typeof(GameController), "AssignHuds")]
    internal static class ExtraHuds
    {
        // Per-player label colors. P3/P4 match the green/purple we assign their ships (and the join
        // screen); P1/P2 are neutral (edit freely). Index = 0-based player. Internal so other parts of
        // the mod (e.g. the Gameplay rumble sliders) can tint their labels with the same palette.
        internal static readonly Color[] PlayerColors =
        {
            new Color(1.00f, 0.82f, 0.30f),   // P1 amber
            new Color(0.35f, 0.72f, 1.00f),   // P2 blue
            new Color(0.49f, 0.86f, 0.49f),   // P3 green  (matches ExtraTheme / join screen)
            new Color(0.78f, 0.55f, 0.92f),   // P4 purple (matches ExtraTheme / join screen)
        };

        // Corner-label placement (canvas px, from the near screen corner). Tunable.
        // Y sits OUTSIDE the controls row toward the screen edge: for top HUDs (controls at top)
        // that puts the label ABOVE the controls; for bottom HUDs (controls at bottom) BELOW them.
        private const float LabelInsetX = 300f;   // horizontal: past the ability-slot row
        private const float LabelInsetY = 36f;    // vertical: near the screen edge, past the controls

        // One-time full hierarchy dump of an extra HUD (to shiphud_dump.txt) for precise layout tuning.
        private static bool _hierDumped;

        private static void Postfix(GameController __instance)
        {
            try
            {
                var huds = AccessTools.Field(typeof(GameController), "huds").GetValue(__instance) as Array;
                if (huds == null || huds.Length == 0) return;
                var sm = ServiceLocator.Get<ShipManager>();
                if (sm.Ships.Count <= huds.Length) return;

                // one-time diagnostic: how are the vanilla P1/P2 HUDs anchored/sized?
                for (int h = 0; h < huds.Length && h < 2; h++)
                {
                    var hc = huds.GetValue(h) as Component;
                    var hrt = hc?.GetComponent<RectTransform>();
                    if (hrt == null) continue;
                    string chain = "";
                    for (var t = hc.transform; t != null; t = t.parent) chain = t.name + "/" + chain;
                    Plugin.Log.LogInfo($"[hud-src] vanilla[{h}] anchorMin={hrt.anchorMin} anchorMax={hrt.anchorMax} pivot={hrt.pivot} " +
                        $"sizeDelta={hrt.sizeDelta} rectSize={hrt.rect.size} anchoredPos={hrt.anchoredPosition} scale={hrt.localScale} chain={chain}");
                }

                // Extra ships reuse a vanilla HUD layout, mirrored to the matching TOP corner:
                // P3 clones P1's (bottom-left) HUD -> top-left; P4 clones P2's (bottom-right) -> top-right.
                for (int i = huds.Length; i < sm.Ships.Count; i++)
                {
                    int extraNo = i - huds.Length;                              // 0 = P3, 1 = P4, ...
                    var src = huds.GetValue(extraNo % huds.Length) as Component; // P1 (left) / P2 (right) source
                    if (src == null) continue;

                    // Parent the clone directly under the HUD canvas so we can lift it to the SCREEN
                    // top. (The vanilla HUDs live in per-player containers anchored to the bottom.)
                    var canvas = src.GetComponentInParent<Canvas>();
                    Transform parent = canvas != null ? canvas.transform : src.transform.parent;
                    var clone = UnityEngine.Object.Instantiate(src.gameObject, parent);
                    clone.name = $"ShipHud_P{i + 1}";
                    clone.SetActive(true);

                    // Keep the clone's vanilla full-height column anchoring (an exact Instantiate copy
                    // already matches the source's left/right side + vertical stretch). We reposition
                    // the CONTENT, not the column, below.
                    if (clone.GetComponent<RectTransform>() is RectTransform rt)
                        rt.localScale = Vector3.one;

                    var hudComp = clone.GetComponent(src.GetType());
                    src.GetType().GetMethod("AssignShip")?.Invoke(hudComp, new object[] { sm.Ships[i] });

                    // Mirror the content to the TOP corner. "Scaler" (VerticalLayoutGroup +
                    // ContentSizeFitter) holds everything and is pinned to the BOTTOM corner in vanilla,
                    // which is what makes P1/P2 grow upward. Flip its Y anchor/pivot to the top and
                    // negate its Y offset -> same edge margins as vanilla, but pinned to the top so it
                    // grows DOWNWARD. reverseArrangement flips the stack order so the controls sit on
                    // top and the resource bars stack beneath them.
                    var scaler = clone.transform.Find("Scaler") as RectTransform;
                    if (scaler != null)
                    {
                        scaler.anchorMin = new Vector2(scaler.anchorMin.x, 1f);
                        scaler.anchorMax = new Vector2(scaler.anchorMax.x, 1f);
                        scaler.pivot     = new Vector2(scaler.pivot.x, 1f);
                        var ap = scaler.anchoredPosition;
                        scaler.anchoredPosition = new Vector2(ap.x, -Mathf.Abs(ap.y));

                        var vlg = scaler.GetComponent<VerticalLayoutGroup>();
                        if (vlg != null)
                        {
                            vlg.reverseArrangement = true;   // controls on top, bars grow down
                            vlg.spacing *= 0.5f;             // tighten the controls-to-bars gap
                        }
                    }

                    // one-time: dump the full clone hierarchy so P3/P4 layout can be tuned precisely
                    if (!_hierDumped)
                    {
                        _hierDumped = true;
                        try { UiDump.Write("shiphud_dump.txt", clone.transform, $"ShipHud clone P{i + 1} (full hierarchy)"); } catch { }
                    }

                    // ---- diagnostics: where did this HUD actually land? ----
                    try
                    {
                        var crt = clone.GetComponent<RectTransform>();
                        var corners = new Vector3[4];
                        crt?.GetWorldCorners(corners);
                        var cg = clone.GetComponentInParent<CanvasGroup>();
                        string chain = "";
                        for (var t = clone.transform; t != null; t = t.parent) chain = t.name + "/" + chain;
                        Plugin.Log.LogInfo(
                            $"[hud-diag] P{i + 1} active={clone.activeInHierarchy} canvas={(canvas != null ? canvas.name : "null")} " +
                            $"canvasScale={(canvas != null ? canvas.scaleFactor : 0f)} anchoredPos={crt?.anchoredPosition} " +
                            $"rectSize={crt?.rect.size} lossyScale={crt?.lossyScale} worldBL={corners[0]} worldTR={corners[2]} " +
                            $"screen={Screen.width}x{Screen.height} cgAlpha={(cg != null ? cg.alpha : 1f)} chain={chain}");
                    }
                    catch { }
                }
                // Colored "PLAYER N" label in each HUD's corner so it's clear which ship it belongs to.
                var labelCanvas = (huds.GetValue(0) as Component)?.GetComponentInParent<Canvas>();
                if (labelCanvas != null)
                {
                    var font = UnityEngine.Object.FindObjectOfType<TMP_Text>()?.font;
                    for (int p = 0; p < sm.Ships.Count; p++) AddPlayerLabel(labelCanvas.transform, p, font);
                }

                Plugin.Log.LogInfo($"Four-Player: HUDs assigned for {sm.Ships.Count} ships (P3 top-left, P4 top-right).");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"ExtraHuds failed: {ex}"); }
        }

        // A per-player corner label ("PLAYER N") tinted with that player's color. P1 bottom-left,
        // P2 bottom-right, P3 top-left, P4 top-right — matching where each HUD's controls sit.
        private static void AddPlayerLabel(Transform canvas, int player, TMP_FontAsset font)
        {
            string name = $"ModPlayerLabel_P{player + 1}";
            var existing = canvas.Find(name);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            bool top = player >= 2;
            bool right = (player % 2) == 1;
            var corner = new Vector2(right ? 1f : 0f, top ? 1f : 0f);

            var go = new GameObject(name, typeof(RectTransform));
            var rtl = go.GetComponent<RectTransform>();
            rtl.SetParent(canvas, false);
            rtl.anchorMin = rtl.anchorMax = rtl.pivot = corner;
            rtl.sizeDelta = new Vector2(200f, 22f);
            rtl.anchoredPosition = new Vector2(right ? -LabelInsetX : LabelInsetX, top ? -LabelInsetY : LabelInsetY);

            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = $"PLAYER {player + 1}";
            t.fontSize = 15;
            t.fontStyle = FontStyles.Bold;
            t.alignment = right ? TextAlignmentOptions.Right : TextAlignmentOptions.Left;
            t.color = PlayerColors[Mathf.Clamp(player, 0, PlayerColors.Length - 1)];
            t.enableWordWrapping = false;
            t.raycastTarget = false;
        }
    }
}
