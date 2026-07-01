using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PunkFourPlayer
{
    /// <summary>
    /// Phase-1 proof of concept for local 4-player. Rides on the existing co-op flow: you start
    /// a co-op run (pick 2 devices as normal) and this adds players 3-4 — placing/spawning extra
    /// ships, auto-assigning the next free gamepads, cloning HUDs, and assigning themes.
    ///
    /// Scope/limits (PoC): NEW co-op runs only (not single-player, not continued saves). Extra
    /// players are auto-assigned spare GAMEPADS only; if you don't have enough, those ships still
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
                if (runArguments.isContinue || !runArguments.isCoop) return;   // new co-op runs only

                // Prefer the explicit device->player picks from the join screen; otherwise fall
                // back to the configured count + auto-grabbed spare gamepads.
                var picks = FourPlayerRuntime.SlotDevices;
                int target = (picks != null && picks.Count >= 2) ? Mathf.Clamp(picks.Count, 2, 4) : Plugin.Target;
                if (target <= 2 || __instance.Ships.Count >= target) return;
                if (Stored.Loadout == null) { Plugin.Log.LogWarning("No starting loadout captured; skipping extras."); return; }

                var smT = typeof(ShipManager);
                var sm = __instance;
                var entityManager = AccessTools.Field(smT, "entityManager").GetValue(sm);
                var shipsConfig = AccessTools.Field(smT, "shipsConfig").GetValue(sm) as ShipsConfig;
                var placeM = AccessTools.Method(smT, "PlaceShipEntity");
                var spawnM = AccessTools.Method(smT, "Spawn", new[] { typeof(EntityData), typeof(Ship), typeof(InputDevice), typeof(bool) });
                var assignThemeM = AccessTools.Method(smT, "AssignTheme");
                var getShipsM = AccessTools.Method(entityManager.GetType(), "GetShips");

                int already = sm.Ships.Count;            // 2 in co-op
                int toAdd = target - already;

                // 1) place the extra ship entities near the start node
                Vector2 c = level.graph.StartNode.center;
                Vector2[] off = { Vector2.up, Vector2.down, Vector2.left * 2f, Vector2.right * 2f };
                for (int i = 0; i < toAdd; i++)
                {
                    var p = c + off[i % off.Length];
                    placeM.Invoke(sm, new object[] { new Vector3(p.x, p.y, 0f), Stored.Loadout });
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
                    InputDevice dev = (picks != null && picks.TryGetValue(idx, out var picked))
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

                Plugin.Log.LogInfo($"Four-Player: {sm.Ships.Count} ships total; {devices.Count} spare gamepad(s) for extras.");
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

    // Give players 3-4 a HUD by cloning the last vanilla HUD (rough placement — PoC).
    [HarmonyPatch(typeof(GameController), "AssignHuds")]
    internal static class ExtraHuds
    {
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
                    bool left = (extraNo % 2) == 0;

                    // Parent the clone directly under the HUD canvas so we can pin it to the SCREEN
                    // corner. (The vanilla HUDs live in per-player containers anchored to the bottom,
                    // so anchoring within that container only reached the bottom area — not the screen.)
                    var canvas = src.GetComponentInParent<Canvas>();
                    Transform parent = canvas != null ? canvas.transform : src.transform.parent;
                    var clone = UnityEngine.Object.Instantiate(src.gameObject, parent);
                    clone.name = $"ShipHud_P{i + 1}";
                    clone.SetActive(true);

                    // P3 -> top-left, P4 -> top-right (pivot at the top corner; content reads downward).
                    if (clone.GetComponent<RectTransform>() is RectTransform rt)
                    {
                        float ax = left ? 0f : 1f;
                        rt.localScale = Vector3.one;
                        rt.anchorMin = new Vector2(ax, 1f);
                        rt.anchorMax = new Vector2(ax, 1f);
                        rt.pivot     = new Vector2(ax, 1f);
                        rt.anchoredPosition = new Vector2((left ? 1f : -1f) * 40f, -40f);
                    }

                    var hudComp = clone.GetComponent(src.GetType());
                    src.GetType().GetMethod("AssignShip")?.Invoke(hudComp, new object[] { sm.Ships[i] });

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
                Plugin.Log.LogInfo($"Four-Player: HUDs assigned for {sm.Ships.Count} ships (P3 top-left, P4 top-right).");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"ExtraHuds failed: {ex}"); }
        }
    }
}
