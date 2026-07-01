# PUNK — Modding Overview & Setup

> Reverse-engineering notes for modding **PUNK** (Steam Playtest build).
> Source: decompiled `Punk_Data/Managed/Punk.Main.dll`.
> Engine: **Unity 6000.3.4f1** (Unity 6.3), **Mono** scripting backend.

This is the entry point for the docs in this folder. Start here, then dive into the
per-subsystem files listed in the [Index](#documentation-index).

---

## 1. Is PUNK moddable? — Yes

Everything needed for runtime code modding via **Harmony** is present:

| Factor | Finding | Implication |
|---|---|---|
| Scripting backend | **Mono** (`MonoBleedingEdge/` present, **no** `GameAssembly.dll`) | Game code ships as real .NET assemblies you can patch at runtime. This is the decisive factor. |
| Game assembly | `Punk_Data/Managed/Punk.Main.dll` (~900 KB, **776 types**) | One readable assembly holds essentially all game logic. |
| Obfuscation | **None** — full descriptive names (`Health`, `EnemyGenerator`, `ModuleGrid`, `DealDamages`, …) | Patch targets are easy to find and reason about. |
| Unity version | 6000.3.4f1 (very new) | Needs a current BepInEx/loader build — see caveats below. |
| Existing loader | None installed (`no winhttp/doorstop/bepinex`) | Clean slate; you install the loader. |
| Networking | Single-player; Steamworks.NET present (leaderboards/achievements) | No anti-cheat netcode to fight, but **leaderboard integrity** is a real concern — see §6. |

If you've written Harmony mods for **7 Days to Die**, this is the same model. That game is
also Mono Unity with Harmony patches against `Assembly-CSharp.dll`; here your target is
`Punk.Main.dll`. The workflow you already know transfers directly.

---

## 2. The game in one picture (for orienting mods)

PUNK is a **top-down roguelike** where you pilot a **Ship** assembled from a **grid of
Modules and Weapons**. You fight **Enemies** (driven by a state-machine AI) across
**procedurally generated** worlds built from **destructible Cells** (with burning /
electricity / drag cellular simulation), populated by **Stations, POIs, Entities and
Plants**. You manage a **Resource/energy** economy, pick up **Consumables, Ingredients and
Loot**, spend currency in **Shops** and on **Upgrades**, and carry **meta-progression**
across runs. State is saved via a **memento/snapshot** system; Steam handles leaderboards.

The decompiled code reveals a clean, **dependency-injected** architecture (installers +
registries + managers). Read [`01-core-architecture.md`](01-core-architecture.md) first —
it explains the bootstrap sequence and the cleanest places to hook a plugin.

---

## 3. Recommended toolchain

**Loader — BepInEx** (closest analog to your 7DTD experience):

- BepInEx drops a `winhttp.dll` proxy + Doorstop that injects the .NET runtime before
  Unity boots, then loads your plugin DLLs from `BepInEx/plugins/`.
- It bundles **`0Harmony.dll`** (the Harmony library) for runtime IL patching.

> ⚠️ **Unity 6.3 caveat.** BepInEx **5.x** (the long-time stable line) predates Unity 6 and
> may not hook 6000.3.x cleanly. Try a current **BepInEx 6 (BE / bleeding-edge, Mono build)**
> first. If BepInEx misbehaves on this Unity version, **MelonLoader** is the fallback — it
> targets newer Unity releases aggressively and also bundles Harmony.

**Other tools you'll want:**

- **ILSpy / dnSpyEx** — browse/decompile `Punk.Main.dll` (this doc set was generated with
  ILSpy's `ilspycmd`). dnSpyEx additionally lets you set breakpoints in the running game.
- **AssetRipper / UABEA** — only if you want to mod *assets* (sprites, audio, levels) which
  live in `resources.assets`, `sharedassets*`, `level*` — separate from Harmony code mods.

---

## 4. Minimal plugin skeleton (BepInEx + Harmony)

```csharp
using BepInEx;
using HarmonyLib;

[BepInPlugin("com.you.punk.mymod", "PUNK My Mod", "1.0.0")]
public class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        var harmony = new Harmony("com.you.punk.mymod");
        harmony.PatchAll();
        Logger.LogInfo("PUNK My Mod loaded.");

        // PUNK uses a custom Service Locator (NOT Zenject). Don't grab managers in Awake —
        // game-scene services are (re)registered per run and unregistered on scene unload.
        // Re-acquire them each run via the static GameController events instead:
        GameController.LevelGenerated += OnLevelGenerated;
    }

    private void OnLevelGenerated()
    {
        // Now the run's services exist; fetch what you need, e.g.:
        // var entities = ServiceLocator.Get<EntityManager>();
        // var run      = ServiceLocator.Get<RunData>();
    }
}

// Example: god mode. DamagableResource.Damage(float) is the universal damage chokepoint
// for units (player + enemies). Returning false from a Prefix cancels the damage.
// Gate it to the player only — see 06-combat-damage.md for identifying the player unit.
[HarmonyPatch(typeof(DamagableResource), nameof(DamagableResource.Damage))]
class Patch_GodMode
{
    static bool Prefix(DamagableResource __instance) =>
        !IsPlayer(__instance);   // false = skip original = no damage
    static bool IsPlayer(DamagableResource d) => /* your check */ false;
}
```

Reference assemblies for your project: `Punk.Main.dll`, `ServiceLocator.dll`, the
`UnityEngine.*.dll` modules you use, and `0Harmony.dll` (from BepInEx). Copy them out of
`Punk_Data/Managed/`.

> Confirm every signature against the per-subsystem docs / live DLL before shipping — names
> reflect the build inspected and the dev may rename things between playtest updates.

### Built-in developer debug menu — **Ctrl + Alt + D** (no mod needed)

The devs left a full cheat/debug menu (`DebugMenu`) wired into the shipped playtest
**GameScene** (`level3`). It has **no development-build gating** — it's live in normal play.
The key bindings were read directly from the scene file:

| Action | Binding | Notes |
|---|---|---|
| **Open** debug menu | **Ctrl + Alt + D** | `TwoModifiers` composite (mod1=Ctrl, mod2=Alt, button=D). Slows time to 0.1× and locks the ship while open. |
| **Close** | **Esc** | |
| Spawn fake Eye | **J** | only when that toggle is enabled in the menu |

> ⚠️ **Gotcha — the modifiers must be pressed *first*.** Unity's `TwoModifiers` composite
> only fires if Ctrl+Alt are already held *before* D is pressed. If you're flying with **D
> held** (movement), the combo won't trigger. Fully release movement keys, **hold Ctrl+Alt,
> then tap D**.

Buttons in the menu just call public `DebugMenu` methods you can also invoke from a mod:

| Menu toggle | Method / effect |
|---|---|
| Invincibility | `ToggleInvincibility()` → `DamagableResource.IsInvincible` on all ships |
| Infinite Resource | `ToggleInfiniteResource()` → `Unit.HasInfiniteResource` |
| Free Shop | `ToggleFreeShop()` → `RunData.AllShopItemsAreFree` (+ refills shop) |
| Noclip | `ToggleNoclip()` → disables ship colliders |
| Invisibility | `ToggleInvisibility()` → `Unit.ComponentData.IsInvisible` (enemies stop seeing you) |
| Add Money / Refill | `AddMoney(int)` / `RefillResources()` |
| Give all modules | `AddAllModuleToVault()` / `ShowModulePickupList()` |
| Teleport anywhere / to boss / discover map | `ToggleTeleportAnywhere()` / `TeleportToBoss()` / `DiscoverMap()` |
| Slow-mo / global light / free camera / hide HUD / FPS | trailer/capture helpers |

If Ctrl+Alt+D does nothing even with the modifier-order fix, the menu object may be disabled
in your build — force it from a plugin:

```csharp
var dbg = Resources.FindObjectsOfTypeAll<DebugMenu>().FirstOrDefault();
dbg?.gameObject.SetActive(true);   // wake it if inactive
dbg?.ToggleInvincibility();        // or call any toggle directly
```

### Verified high-value hooks (from the decompile)

These were confirmed by reading the actual code. Details + exact fields in the linked docs.

| Goal | Hook / field | Doc |
|---|---|---|
| **Run mod init at the right time** | static events `GameController.GameStarted` / `LevelGenerated` / `GameOver` / `GameWon` | 01 |
| **Get a manager/registry** | `ServiceLocator.Get<T>()` (custom DI; ~418 call sites) — call after `LevelGenerated`, not in `Awake` | 01 |
| **Player god mode** | Prefix `DamagableResource.Damage(float)` → return false for player. (The game's own `IsInvincible` only floors HP at 1 — weaker.) | 06 |
| **Infinite fuel/energy** | built-in flag `Unit.Data.HasInfiniteResource = true` (tanks reject decreases) | 02, 10 |
| **Free shops** | built-in flag `RunData.AllShopItemsAreFree = true` (used by the dev's own debug menu) | 11 |
| **Unlock all loadouts** | `PlayerPrefs` key `META_UNLOCKED_LOADOUTS` via `MetaProgressManager` | 04, 11 |
| **More damage / fire rate** | public fields on `WeaponBase` (force-based, no Burst) | 03 |
| **Enemy scaling / disable AI** | `EnemyGenerator` power budget; `StateMachine`/`State` toggles | 05 |
| **Fixed/custom world seed** | `RunDataInstaller.GetSeed()` (0 ⇒ random) | 08 |
| **Toggle a mod menu / hide HUD** | `UIScreen.Open/Close`, `InGameHud.SetHudVisible` | 12 |
| **Persist custom mod data** | register a `ComponentData` + `SavableComponent<T>` in `SavablesCollection` (auto-serialized, no save patch needed) | 13 |

> ⚠️ Several heavy systems run as **Burst-compiled `IJob` structs** (cell fire/electricity
> simulation, world generation, lightmaps, fog) — their `Execute` bodies are native and
> **cannot be Harmony-patched**. Patch the *managed* schedulers and tune their input config
> instead. The affected docs (07, 08, 09, 14) flag exactly which types these are.

---

## 5. Documentation index

Each file documents one subsystem: an overview, a class index, per-class fields/methods, and
a **Modding Notes** section with concrete Harmony targets.

| # | Doc | Covers |
|---|---|---|
| 00 | `00-overview.md` (this file) | Moddability assessment, setup, index |
| 01 | [`01-core-architecture.md`](01-core-architecture.md) | Bootstrap, dependency injection, installers, registries, managers, scenes — **read first** |
| 02 | [`02-player-ship.md`](02-player-ship.md) | The player ship: movement, input, legs, hud, units |
| 03 | [`03-weapons-projectiles.md`](03-weapons-projectiles.md) | Weapons, barrels, shooters, projectiles, modifiers |
| 04 | [`04-modules-grid.md`](04-modules-grid.md) | Module grid, clusters, effects, loadouts |
| 05 | [`05-enemies-ai.md`](05-enemies-ai.md) | Enemies, AI state machine, actions/conditions, bosses |
| 06 | [`06-combat-damage.md`](06-combat-damage.md) | Health, damage pipeline, shields, explosions |
| 07 | [`07-cells-systems.md`](07-cells-systems.md) | Destructible cells + burning/electricity/drag/regrow sim |
| 08 | [`08-world-generation.md`](08-world-generation.md) | Level/dungeon/biome/noise generation, maps, seeds |
| 09 | [`09-stations-poi-entities.md`](09-stations-poi-entities.md) | Stations, POIs, entities, plants |
| 10 | [`10-resources-consumables-loot.md`](10-resources-consumables-loot.md) | Resource economy, ingredients, consumables, pickups, loot |
| 11 | [`11-shop-upgrades-meta.md`](11-shop-upgrades-meta.md) | Shops, upgrades, currency, run lifecycle, leaderboards |
| 12 | [`12-ui-hud-menus.md`](12-ui-hud-menus.md) | HUD, screens, menus, widgets, input maps |
| 13 | [`13-save-load-config.md`](13-save-load-config.md) | Save/load, mementos, config, settings, Steam/platform |
| 14 | [`14-audio-camera-rendering.md`](14-audio-camera-rendering.md) | Audio/music, cameras, URP render features & FX |

---

## 6. Caveats & etiquette

- **It's a Playtest build.** Code churns between updates; patches keyed to method signatures
  may break when the dev ships a new build. Expect to re-check signatures after each update.
- **Burst-compiled jobs can't be Harmony-patched at the IL level.** Several heavy systems
  (cell simulation, world generation, lightmaps) run as Burst `IJob` structs compiled to
  native code. You can patch the *managed* code that schedules them and tweak their input
  config/fields, but not the job bodies themselves. The relevant docs flag these.
- **Leaderboards / Steam.** The game submits scores to Steam leaderboards. Mods that alter
  difficulty, damage, or progression and then submit scores will pollute public boards and
  may get you flagged. Keep cheat-style mods to offline/local play, or disable score
  submission (see [`11-shop-upgrades-meta.md`](11-shop-upgrades-meta.md) and
  [`13-save-load-config.md`](13-save-load-config.md)).
- **Respect the developers.** This is an unreleased playtest. Be considerate about
  publicly distributing mods, datamined content, or spoilers before release.

---

*Generated from a static decompile. Method/field names reflect the build inspected and may
change. Always confirm signatures against the live `Punk.Main.dll` before shipping a patch.*
