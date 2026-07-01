# Core Architecture: Bootstrap, DI, Managers & Registries
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK is wired together by a **tiny custom Service Locator**, *not* Zenject/VContainer/Reflex. The
DI framework lives in its own assembly, `ServiceLocator.dll` (~9.7 KB, package id
`com.bmaczak.servicecontainer`), which sits next to `Punk.Main.dll` in
`PUNK Playtest/Punk_Data/Managed/`. Game code never news-up its dependencies; instead almost
everything is fetched globally via `ServiceLocator.Get<T>()` (used in ~418 call sites across the
codebase) or `ServiceLocator.TryGet<T>(out T)` (~16 sites).

The moving parts:

- **`ServiceLocator`** (static): one global `Dictionary<Type, object>` of singletons. Register, Get,
  TryGet, Unregister, Clear. There is **one shared registry for the whole app** — no per-scene
  scoping at the lookup level.
- **`ServiceContainer`**: a per-`MonoServiceContainer` helper that runs installers and tracks which
  services it installed (so it can uninstall them).
- **`IServiceInstaller`**: implemented by every `*Installer` MonoBehaviour. Its `InstallServices`
  method calls `container.Install(...)` for each binding.
- **`MonoServiceContainer`** and its two subclasses **`GlobalServiceContainer`** (app-wide, loaded
  before the first scene) and **`SceneServiceContainer`** (per-scene, `[DefaultExecutionOrder(-1)]`).
  These are the actual bootstrap drivers.
- **Registries**: ScriptableObject (or `ConfigFile`) lookup tables that hold the game's content
  database — modules, weapons, resources, consumables, ingredients, PoIs, etc. — keyed by id.
- **Managers**: the runtime systems (entities, time, navigation, audio, ships, fog, electricity…),
  most installed as services and resolved through the locator.
- **Scenes**: `SplashScreen` -> `MainMenu`/`DemoMenu` -> `LoadoutSelector` -> `Game` ->
  `HighscoresScreen`/`GameOverMenu`, driven by small static "scene" helper classes.

Note: `Singleton<T>` exists in the assembly but **no class actually derives from it** — it is
vestigial. The real singleton mechanism is the ServiceLocator. The only independent static-`Instance`
holdout is `SteamManager` (standard Steamworks.NET pattern).

## Dependency Injection & Installers

### The DI library (`ServiceLocator.dll`)

Decompiled core (verbatim signatures):

```csharp
public static class ServiceLocator
{
    public static IReadOnlyDictionary<Type, object> AllInstalledServices { get; }
    public static T Get<T>();                       // logs error + returns default if missing
    public static bool TryGet<T>(out T result);     // safe, no error log
    public static void Register(object service);    // registers under concrete type + IGameService interfaces
    public static void Register(Type type, object service);
    public static void Unregister(object service);
    public static void Unregister<T>();
    public static void Clear();
}

public class ServiceContainer
{
    public void Install(object service);                                  // -> ServiceLocator.Register + track
    public void InstallServices(IEnumerable<IServiceInstaller> installers);// run installers, then Initialize()
    public void UninstallServices();                                      // Dispose() then Unregister all
}

public interface IServiceInstaller { void InstallServices(ServiceContainer container); }
public interface IGameService { }            // marker; registers a service under its interface too
public interface IInitializable { void Initialize(); }
```

**How registration works** (important for modders): `ServiceContainer.Install(obj)` calls
`ServiceLocator.Register(obj)`, which registers the object under **its concrete type** *and* under
**every interface it implements that derives from `IGameService`** (except `IGameService` itself).
That is why `LevelGenerator` can be fetched as `ServiceLocator.Get<ILevelGenerator>()` and
`UnityAnalyticsService` as `ServiceLocator.Get<IAnalyticsService>()`. Registering the same type twice
logs an error and is ignored — so you cannot silently overwrite an existing binding; you must
`Unregister` first.

**Initialization order**: after *all* installers on a container have run, `ServiceContainer` iterates
its installed services and calls `Initialize()` on any that implement `IInitializable`. So during
`InstallServices(...)`/constructors you may **not** yet have other services; but inside an
`Initialize()` (or in a MonoBehaviour `Awake`/`Start`) all services from the same container are
available. Registries use `Initialize()` to build their id->item dictionary; `EntityManager`,
`SettingsManager`, `ShipManager`, `RunData` etc. use it to grab their own dependencies.

**Teardown**: `UninstallServices()` calls `Dispose()` on `IDisposable` services then unregisters
them. `GlobalServiceContainer` does this on `Application.quitting`; `SceneServiceContainer` does it in
`OnDestroy` (i.e. when its scene unloads).

### The two bootstrap drivers

```csharp
public class GlobalServiceContainer : MonoServiceContainer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
        => Resources.Load<GlobalServiceContainer>("GlobalServices").OnAppStart();
    // OnAppStart -> ServiceLocator.Clear(); Install(); Application.quitting += OnAppQuit;
}

[DefaultExecutionOrder(-1)]
public class SceneServiceContainer : MonoServiceContainer
{
    private void Awake()     => Install();    // runs before normal components
    private void OnDestroy() => Uninstall();
}
```

`MonoServiceContainer.Install()` does `container.InstallServices(GetComponents<IServiceInstaller>())`
— meaning **installers must be components on the same GameObject** as the container. So the global
installers live on the `Resources/GlobalServices` prefab, and each scene's installers live on a
GameObject carrying a `SceneServiceContainer`.

### Every installer and what it binds

Grouping into *Global* (on the `GlobalServices` prefab, persist for the whole app) vs *Game scene*
(installed by a `SceneServiceContainer` when the `Game` scene loads) is inferred from each
installer's contents — the exact GameObject placement lives in Unity scene/prefab assets that are not
part of the decompiled C#. All are `MonoBehaviour, IServiceInstaller` unless noted.

| Installer | Scope (inferred) | Installs / binds |
|---|---|---|
| `DataInstaller` | Global | `LevelElementsCollection`, `ModuleRegistry`, `WeaponRegistry`, `ModuleSlotTypeRegistry`, `SavablesCollection`, `ResourceRegistry`, `IngredientRegistry`, `PoIRegistry`, `MergedCellsRegistry`, `ConsumableRegistry`, `ShopItemsConfig`, `LocalConfig` (all serialized SO refs) |
| `SettingsInstaller` | Global | `new SettingsManager()`, `new MetaProgressManager()` |
| `AudioInstaller` | Global | Instantiates `AudioManager` prefab (`DontDestroyOnLoad`), installs it + its `MusicManager` |
| `UtilityInstaller` | Global | `new LastUsedDeviceTracker()`; instantiates `UiManager` + `CursorController` prefabs (`DontDestroyOnLoad`) |
| `AnalyticsInstaller` | Global | `new UnityAnalyticsService()` or `new DummyAnalyticsService()` (`useDummy` flag) — bound under `IAnalyticsService` |
| `PlatformFeaturesInstaller` | Global | `new SteamPlatformFeatures()` (bound under `IPlatformFeatures`) |
| `GameSaverInstaller` | Global | `new GameSaver()` (`Punk.SaveLoad`) |
| `GlobalFactoriesInstaller` | Global | `new WeaponFactory()` |
| `GameSystemsInstaller` | Game scene | The big one — see below |
| `LevelInstaller` | Game scene | `new Level()` |
| `RunDataInstaller` | Game scene | `new Seed(seed)` (from `GameScene.arguments.seed`, random if 0), `new RunData()` |
| `LevelGeneratorInstaller` | Game scene | `HeightmapGenerator`, `JRasterizator`, `ILevelGenerator` (`LevelGenerator` or `DummyLevelGenerator`), `EntityGenerator`, `EnemyGenerator`, `PlantGenerator`, `SubBiomGenerator`, `RequiredRoomsGenerator`, `RegularRoomsGenerator`, `GraphEdgeGenerator`, `BiomeSpreader`, `GraphGenerator`, `DungeonGenerator`, `BorderGenerator`, `PoIGenerator`, `StationGenerator`, `ScannerGenerator`, `ScannerAreaGenerator`, `MergedCellsGenerator`, `BiomeCrustGenerator`, `BackgroundGenerator`, `EdgeColliderBuilder`, a `LevelGeneratorConfig` (editor vs runtime variant) |
| `ShipInstaller` | — | **Empty stub** — `MonoBehaviour`, does *not* implement `IServiceInstaller`, installs nothing |
| `ElectricityTestInstaller` | Test scene | `ElectricityManager` (serialized ref) |
| `GeneratorTestInstaller` | Test scene | Standalone level-gen sandbox: `Seed`, `EntityManager`, `Level`, all generators + `SpatialGrid` + a `LevelGeneratorConfig` |

`GameSystemsInstaller.InstallServices` binds the gameplay backbone (order matters; abbreviated):
`gameController`, `new SpatialGrid()`, `new EntityManager()`, `entityGameObjectManager`,
`levelSegmentManager`, `levelSegmentComponentManager`, `levelChangeBuffer`, `shipsConfig`,
`new ShipManager()`, `new Vault()`, `new ModulePickupFactory(...)`, `new IngredientPickupFactory(...)`,
`new ConsumablePickupFactory(...)`, `new LootFactory()`, `moduleGridScreen`, `enumsCollection`,
`FindObjectOfType<HealthbarManager>()`, `FindObjectOfType<ShopWidget>()`,
`FindObjectOfType<VaultGridWidget>()`, `FindObjectOfType<ModuleGridWidget>()`, `new LootSelector()`,
`FindObjectOfType<TimeManager>()`, `shopUpgradeData`, `mapDrawer`, `mapMover`, `mapIconManager`,
`stationLightManager`, `cellBurningManager`, `new OnScreenCellsTracker()`,
`statusEffectParticleManager`, `lightmapGenerator`, `tilemapAnimator`, `electricityManager`,
`inGameHud`, `explosionManager`, `fogManager`, `shipMenu`, `plantDestructor`,
`new BossStateManager()`.

## Startup / Bootstrap Sequence

1. **App start (before any scene):** Unity calls `GlobalServiceContainer.Initialize()` via
   `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`. It loads the `Resources/GlobalServices` prefab
   and calls `OnAppStart()`:
   - `ServiceLocator.Clear()` (wipe any stale registry),
   - `Install()` -> runs every `IServiceInstaller` component on the GlobalServices prefab (Data,
     Settings, Audio, Utility, Analytics, PlatformFeatures, GameSaver, GlobalFactories),
   - then `Initialize()` is invoked on all installed `IInitializable` services (registries build their
     dictionaries, `SettingsManager` loads `options.json`, etc.),
   - registers `OnAppQuit` to uninstall on `Application.quitting`.
   After this point the **global services and all content registries are live** even before the menu
   appears.
2. **Splash -> Menu:** `SplashScreen` (build index 0) fades, waits `defaultDuration` or any button,
   then `LoadScene(activeScene.buildIndex + 1)`. `MainMenuScene.Load()` loads `MainMenu` (or
   `DemoMenu` if `demoMode`).
3. **Run setup:** `RunSetupScene.GoToLoadoutSelector(coop, isContinue)` builds a `RunArguments`
   (`RunArguments.NewRun` / `.Continue`) into `RunSetupScene.arguments` and loads `LoadoutSelector`.
4. **Enter game:** `GameScene.GoToGameScene(RunArguments)` (or `GameScene.Continue(coop)`) stores the
   args in the **static `GameScene.arguments`** and async-loads the `Game` scene, then sets it active.
5. **Game scene installers:** the `Game` scene's `SceneServiceContainer` (`[DefaultExecutionOrder(-1)]`,
   so its `Awake` runs first) installs `GameSystemsInstaller`, `LevelInstaller`, `RunDataInstaller`,
   `LevelGeneratorInstaller`. `RunDataInstaller` reads `GameScene.arguments.seed`. After installation,
   `Initialize()` runs on the scene services (e.g. `EntityManager.Initialize` grabs `SpatialGrid` +
   `Seed`; `ShipManager.Initialize`; `RunData.Initialize`).
6. **GameController wakes:** `GameController.Awake()` resolves its dependencies from the locator
   (`RunData`, `Level`, `ILevelGenerator`, `ShipManager`, `EntityGameObjectManager`,
   `IAnalyticsService`, `TimeManager`) and reads `GameScene.arguments`.
7. **Level build:** `GameController.Start()` (coroutine, yields one frame) calls `BuildLevel()` (new
   run) or `LoadLevel()` (continue). `BuildLevel` awaits `levelGenerator.GenerateLevel(level, Seed)`,
   places ship entities, then `OnLevelGenerated()` spawns GameObjects, connects stations, assigns
   HUDs, fires the static **`GameController.LevelGenerated`** action, and pauses time via
   `timeManager.Pause(this)` until the player starts.
8. **Gameplay starts:** `GameController.StartGame()` removes the pause modifier, switches input to
   `ShipControl`, sets `gameStarted = true`, and fires the static **`GameController.GameStarted`**
   action. End-of-run fires `GameOver` / `GameWon`.

Key static hook points exposed by `GameController`: `public static Action<Level> LevelGenerated`,
`public static Action GameStarted`, `public static Action GameOver`, `public static Action GameWon`.

## Registries & Managers

### Registries

All concrete registries derive from `ScriptableObjectRegistry<TItem, TId>` (a `ScriptableObject`
implementing `IInitializable, IRegistry<TItem,TId>, IGameService, IValidatable`). API:
`IEnumerable<TItem> AllItems`, `TItem Get(TId id)` (returns null/default on miss), `Initialize()`
(builds `itemList.ToDictionary(i => i.Id)`). The item list is a **serialized private `List<TItem>`** —
there is **no public Add/Register at runtime**. `ConfigRegistry<TItem,TId>` is the same shape but
derives from `ConfigFile` instead of `ScriptableObject`.

| Registry | Base | Item type | Id type | Holds |
|---|---|---|---|---|
| `ModuleRegistry` | SO | `ModuleData` | `string` | All ship module definitions |
| `WeaponRegistry` | SO | `WeaponData` | `string` | All weapon definitions |
| `ResourceRegistry` | SO | `Resource` | `string` | Resources (fuel, score, currencies…) |
| `ConsumableRegistry` | SO | `Consumable` | `string` | Consumable items |
| `IngredientRegistry` | SO | `Ingredient` | `string` | Crafting/cooking ingredients |
| `ModuleSlotTypeRegistry` | SO | `ModuleSlotType` | `string` | Module slot type definitions |
| `PoIRegistry` | SO | `PoI` | `int` | Points of interest for level gen |
| `MergedCellsRegistry` | SO | `MergedCellData` | `byte` | Merged-cell tile definitions |
| `ConfigRegistry<,>` | `ConfigFile` | (abstract) | (generic) | Base for JSON-backed config registries |

All are bound by `DataInstaller` and fetched with e.g. `ServiceLocator.Get<ModuleRegistry>()`.
`ScriptableObjectRegistry` is also an `IRegistry<TItem>` / `IRegistry<TItem, TId>` (both
`IGameService`), so it is additionally registered under those interfaces.

### Key managers (and how to get them)

Every entry below is obtained via `ServiceLocator.Get<T>()` unless noted.

| Manager | Type | Notes |
|---|---|---|
| `EntityManager` | plain class, `IInitializable` | Authoritative entity store over `SpatialGrid`. Events `EntityDestroyed`, `EntityMovedToNewSegment`. `GetAllEntities()`, `GetEntitiesWithComponent<T>()`, `GetEntity(instanceId)`, `Add(entity,pos)`, `CreateInstanceId()`. |
| `EntityGameObjectManager` | `MonoBehaviour` | Maps entity `instanceId` -> spawned `SavableEntity` GameObjects per active segment. `CreateEntity`, `TryGetSavableEntity`. |
| `TimeManager` | `MonoBehaviour` | Global time-scale stack. `AddModifier(TimeScaleModifier, owner)`, `RemoveAllModifiers(owner)`, `Pause(owner)`, `SetTimeScale(scale, owner)`. Drives `Time.timeScale` (min of all modifiers) + ducks the audio mixer when paused. Nested `TimeManager.TimeScaleModifier` struct. |
| `NavigationManager` | `MonoBehaviour` | A* (`AstarPath`) pathfinding. Rescans on `GameController.LevelGenerated`, updates graph on cell changes if `LocalConfig.data.enableNavmeshRefresh`. |
| `FastTravelManager` | `MonoBehaviour` | Station-to-station and arbitrary teleport sequences (`TravelBetweenStations`, `TravelToEntity`, `TravelTo`). Pulls `EntityManager`, `ShipManager`, `EntityGameObjectManager` in `Awake`. |
| `ShipManager` | plain class, `IInitializable` | Owns `Ships` list, spawns/places ships, `OnGameStarted`. |
| `SettingsManager` | plain class, `IGameService, IInitializable, IDisposable` | Loads/saves `options.json` (Newtonsoft). `GetCurrentSettings`, `VideoOptions`, `GameplayOptions`. |
| `MetaProgressManager` | plain class | Cross-run meta progress; `RegisterDeath()`. |
| `AudioManager` / `MusicManager` | `MonoBehaviour, IGameService` | `DontDestroyOnLoad`; instantiated by `AudioInstaller`. |
| `UiManager`, `CursorController` | `MonoBehaviour` | `DontDestroyOnLoad` UI roots from `UtilityInstaller`. |
| `ElectricityManager`, `FogManager`, `ExplosionManager`, `CellBurningManager`, `StationLightManager`, `MapIconManager`, `StatusEffectParticleManager`, `BossStateManager`, `LevelSegmentManager`, `LevelSegmentComponentManager` | mostly `MonoBehaviour` | Gameplay subsystems bound by `GameSystemsInstaller`. |
| `SteamManager` | `MonoBehaviour` | **Not** a ServiceLocator service — standard Steamworks.NET `protected static Instance` / `public static bool Initialized`. |

Bootstrap data services: `RunData` (`IInitializable` run-wide state + Memento save), `Seed`
(readonly struct wrapping the run seed, implicit cast to `int`), `Level`, `GameSaver`,
`SteamPlatformFeatures` (`IPlatformFeatures`), `WeaponFactory`/`LootFactory`/`*PickupFactory`
(`IFactory<...>`).

## Class Index

| Class / Type | Kind | Role |
|---|---|---|
| `ServiceLocator` | static (ServiceLocator.dll) | Global Type->object service registry; `Get`/`TryGet`/`Register`/`Unregister`/`Clear`/`AllInstalledServices` |
| `ServiceContainer` | class (ServiceLocator.dll) | Runs installers, tracks + initializes + disposes services |
| `IServiceInstaller` | interface (ServiceLocator.dll) | `InstallServices(ServiceContainer)` — implemented by all `*Installer` |
| `IGameService` | marker interface | Causes a service to also register under that interface |
| `IInitializable` | interface | `Initialize()` called after all installs on a container |
| `MonoServiceContainer` | MonoBehaviour | Base driver: `Install()` over sibling `IServiceInstaller`s |
| `GlobalServiceContainer` | MonoBehaviour | App bootstrap (`BeforeSceneLoad`), loads `Resources/GlobalServices` |
| `SceneServiceContainer` | MonoBehaviour `[ExecOrder -1]` | Per-scene bootstrap (`Awake`/`OnDestroy`) |
| `GameController` | MonoBehaviour | Game-scene orchestrator; static `LevelGenerated`/`GameStarted`/`GameOver`/`GameWon` |
| `GameSystemsInstaller` | Installer | Binds the gameplay backbone (Game scene) |
| `DataInstaller` | Installer | Binds all content registries + configs (Global) |
| `SettingsInstaller`, `AudioInstaller`, `UtilityInstaller`, `AnalyticsInstaller`, `PlatformFeaturesInstaller`, `GameSaverInstaller`, `GlobalFactoriesInstaller` | Installers | Global services |
| `LevelInstaller`, `RunDataInstaller`, `LevelGeneratorInstaller` | Installers | Game-scene services |
| `ElectricityTestInstaller`, `GeneratorTestInstaller` | Installers | Test-scene sandboxes |
| `ShipInstaller` | MonoBehaviour | Empty stub (not an installer) |
| `ScriptableObjectRegistry<TItem,TId>` | abstract SO | Base for content registries |
| `ConfigRegistry<TItem,TId>` | abstract (ConfigFile) | Base for config registries |
| `ModuleRegistry`/`WeaponRegistry`/`ResourceRegistry`/`ConsumableRegistry`/`IngredientRegistry`/`ModuleSlotTypeRegistry`/`PoIRegistry`/`MergedCellsRegistry` | SO registries | Content databases |
| `IRegistry<TItem>` / `IRegistry<TItem,TId>` | interfaces | `AllItems` / `Get(id)` (extend `IGameService`) |
| `IIdentifiable<T>` | interface | `T Id { get; }` for registry items |
| `IValidatable` | interface | `int Validate()` |
| `IFactory` / `IFactory<...>` | interfaces | Generic factory contracts (`Create(...)`) |
| `EntityManager` | class | Entity store over SpatialGrid |
| `EntityGameObjectManager` | MonoBehaviour | Entity<->GameObject mapping |
| `TimeManager` (+ `TimeScaleModifier` struct) | MonoBehaviour | Time-scale stack |
| `TimeScaleModifier` | MonoBehaviour | Component that pushes a `TimeScaleModifierSetup` to `TimeManager` |
| `TimeScaleModifierSetup` | SO | Holds a `TimeManager.TimeScaleModifier` |
| `NavigationManager` | MonoBehaviour | A* pathfinding |
| `FastTravelManager` | MonoBehaviour | Teleport/fast-travel sequences |
| `ShipManager` | class | Owns/spawns ships |
| `SettingsManager`, `MetaProgressManager`, `AudioManager`, `MusicManager`, `UiManager` | various | See managers table |
| `SteamManager` | MonoBehaviour | Steamworks singleton (static `Instance`) |
| `Singleton<T>` | abstract MonoBehaviour | Generic singleton base — **unused** in PUNK |
| `RunArguments` | struct | Run config passed via `GameScene.arguments` / `RunSetupScene.arguments` |
| `RunData` | class | Run-wide mutable state + Memento save |
| `Seed` | readonly struct | Run seed, implicit `int` |
| `GameScene` / `MainMenuScene` / `RunSetupScene` / `LeaderboardScene` | static helpers | Scene navigation |
| `SplashScreen` | MonoBehaviour | First-scene fade then advance |

## Modding Notes (where to hook a BepInEx/Harmony plugin)

**The single most important fact:** every shared system is reachable from a static method —
`ServiceLocator.Get<T>()`. Once the relevant container has installed, `Get<T>` returns the live
instance. You do not need to find GameObjects; you ask the locator.

### Best entry points (in order of preference)

1. **Hook `GameController.GameStarted` / `LevelGenerated` (cleanest for in-game logic).** These are
   `public static Action`s. From your plugin you can simply subscribe with no Harmony at all:
   ```csharp
   GameController.GameStarted += () => {
       var ships   = ServiceLocator.Get<ShipManager>();
       var entities= ServiceLocator.Get<EntityManager>();
       var time    = ServiceLocator.Get<TimeManager>();
       // ... all Game-scene services are guaranteed registered + initialized here
   };
   GameController.LevelGenerated += level => { /* level just built, GameObjects spawned */ };
   ```
   Subscribe to these once (e.g. in your `BepInEx.BaseUnityPlugin.Awake`) — they are static so the
   subscription survives scene reloads.

2. **Harmony-postfix a container/installer to react exactly when DI is ready.** Good anchor points:
   - `MonoServiceContainer.Install` (postfix) — fires after *any* container finishes installing +
     `Initialize()`-ing. Runs for both the global and every scene container.
   - `GlobalServiceContainer.OnAppStart` (postfix) — global registries/services are now live; earliest
     safe point to read content registries. (It is `private`; patch by reflection / `AccessTools`.)
   - `GameSystemsInstaller.InstallServices` (postfix, `__instance`/`container`) — gameplay services
     just bound for the Game scene.
   - `GameController.Awake` or `Start` (postfix) — the Game scene is fully wired.

3. **Register your own services into the locator.** Inside any installer-time hook you can call
   `ServiceLocator.Register(myService)` (or `container.Install(myService)` if you patched an
   installer and have the `ServiceContainer`). Implement `IGameService` on an interface to make your
   service resolvable by interface, and `IInitializable` to get an `Initialize()` callback. Remember
   re-registering an existing type logs an error and is ignored — `ServiceLocator.Unregister<T>()`
   first if you want to **replace** a vanilla service (e.g. swap `ILevelGenerator`).

### Getting manager / registry instances from a plugin

```csharp
var modules  = ServiceLocator.Get<ModuleRegistry>();
var weapons  = ServiceLocator.Get<WeaponRegistry>();
if (ServiceLocator.TryGet<FastTravelManager>(out var ft)) { /* may be Game-scene only */ }
```
Use `TryGet` (not `Get`) for anything that may not exist yet — `Get` logs a Unity error and returns
`default` when missing. You can also enumerate everything currently registered via
`ServiceLocator.AllInstalledServices` (an `IReadOnlyDictionary<Type, object>`), handy for discovery
while developing a mod.

### Registering custom content into the registries

Registries are **closed at runtime** — `ScriptableObjectRegistry`/`ConfigRegistry` expose only
`Get`/`AllItems` and build their dictionary once in `Initialize()`; there is no public `Add`. To
inject custom modules/weapons/etc. you have two practical routes:

- **Mutate the serialized list before/at `Initialize()`.** The backing field is a private
  `List<TItem> itemList` (and a private `Dictionary<TId,TItem> itemDictionary`). Use reflection
  (`AccessTools.Field`) to add your `ScriptableObject` items to `itemList`, then either let
  `Initialize()` run (Harmony-prefix `ScriptableObjectRegistry<,>.Initialize` to add before it builds
  the dict) or, if it already initialized, also insert into `itemDictionary`. Your `TItem` must derive
  from `ScriptableObject` and implement `IIdentifiable<TId>` with a unique `Id`.
- **Replace the whole registry service.** `Unregister` the vanilla registry and `Register` your own
  subclass instance — only worthwhile for wholesale content overhauls.

Because all registries are bound in `DataInstaller` (global) and initialized during
`GlobalServiceContainer.OnAppStart`, do your content injection from a prefix on
`ScriptableObjectRegistry<,>.Initialize` or right after `OnAppStart`, *before* gameplay reads them.

### Gotchas

- **No per-scene scoping at lookup.** There is one global dictionary. Game-scene services are
  registered on scene load and **unregistered on scene unload** (`SceneServiceContainer.OnDestroy` ->
  `UninstallServices`). So a cached reference to e.g. `EntityManager` becomes stale after leaving the
  Game scene — re-`Get` each run, or re-acquire on `GameController.LevelGenerated`.
- **Installers must be siblings of their container** (`GetComponents<IServiceInstaller>()`), so you
  cannot add an installer by dropping a loose component into a scene; add your service from a Harmony
  hook instead.
- **`Singleton<T>` is a red herring** — nothing uses it; don't build your hooks around it. The only
  static-`Instance` type is `SteamManager`.
- `RunArguments`/`Seed` flow through the **static** `GameScene.arguments` (and
  `RunSetupScene.arguments`); read/patch those if you need to influence run setup (seed, coop,
  starting loadout, daily-challenge data).
