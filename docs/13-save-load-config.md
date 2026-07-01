# Save/Load, Persistence, Config & Platform
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK persists a run across four independent layers, each with its own mechanism:

1. **Run save (the "continue" feature)** — the full mid-run world state. Driven by `Punk.SaveLoad.GameSaver`, which writes a *folder of separate files* (one per subsystem) under `Application.persistentDataPath/saves/`. Most files are Odin-serialized (Sirenix) **binary**, LZF-compressed via `CLZF2`; map textures are raw PNG. There is exactly one normal slot (`save001`) and one co-op slot (`coop_save001`). Saving is triggered by **Save & Exit** (`GameController.SaveAndExit`), and the save is **deleted** when a run ends (game over / win / abandon). It is not an autosave-everywhere system — it's a suspend/resume snapshot.

2. **The memento / snapshot model** — what actually gets serialized. Game logic lives in plain-C# data objects (`EntityData` + `ComponentData` subclasses), not on the `MonoBehaviour`s. Anything persistable implements `IMementoOriginator<TMemento>` and produces an `IMemento` value object (`CreateMemento()` / `RestoreFromMemento()`). The world grid itself is captured by the struct `LevelSnapshot` (packed byte arrays). This is the layer modders most often want to extend.

3. **Meta-progression** — small cross-run unlocks (`MetaProgressManager`) stored in Unity **`PlayerPrefs`** (registry on Windows), not in the save folder. Death count and unlocked loadouts live here.

4. **Settings & config** — player options (`SettingsManager` → `options.json` in the **game root**), an optional dev/perf override (`LocalConfig` → JSON file in the game root), and live ScriptableObject config registries (`ConfigFile`/`ConfigRegistry`). Platform integration (`SteamManager`, `SteamPlatformFeatures`) handles leaderboards/daily-challenge; analytics (`IAnalyticsService`) and `BugReporter` round out the platform layer.

The game uses a service-locator DI pattern: services are registered by `IServiceInstaller`s (e.g. `GameSaverInstaller`, `SettingsInstaller`, `PlatformFeaturesInstaller`, `AnalyticsInstaller`) and resolved at runtime via `ServiceLocator.Get<T>()`. This is the primary seam a mod uses to reach the save system.

## Save File Format & Location

**Location (run saves):**
```
Application.persistentDataPath/saves/<slot>/
```
On Windows that resolves to (company/product taken from `Punk_Data/app.info` = `DefaultCompany` / `Punk`):
```
C:\Users\<user>\AppData\LocalLow\DefaultCompany\Punk\saves\
```
Slot folder names (`GameSaver.GetSaveFolderName`):
- `save001` — normal single-player
- `coop_save001` — co-op
- In the Unity editor a `_editor` suffix is appended (`save001_editor`). The `Save(string)` overload also allows arbitrary names; the debug menu uses `"debug"`.

**Files written per slot** (names from `SaveFolder`, all in the slot directory, no extensions):

| File | Producer | Format |
|------|----------|--------|
| `entities` | `List<EntityData.Memento>` for every entity in `EntityManager` | Odin binary + CLZF2 |
| `world` | `LevelSnapshot.ToByteArray()` (biomes, cell types, heightmap, fog, merged cells, plants) | Raw packed bytes + CLZF2 |
| `graph` | `GraphSnapshot` of the level graph (nodes/edges) | Odin binary + CLZF2 |
| `levelinfo` | `LevelInfo` struct (width, height) | Odin binary + CLZF2 |
| `mapicons` | `MapIconManager` memento | Odin binary + CLZF2 |
| `rundata` | `RunData.Memento` (shop, resources, kill counts, run time) | Odin binary + CLZF2 |
| `vault` | `Vault.Memento` (stored modules, ingredients, consumables) | Odin binary + CLZF2 |
| `fow` | Fog-of-war render texture | **PNG** (uncompressed by CLZF2) |
| `map` | Map texture | **PNG** |
| `scanner` | Scanner/area-state lookup texture | **PNG** |

**Serialization mechanics:**
- Binary payloads use Sirenix Odin `SerializationUtility.SerializeValue(data, DataFormat.Binary)`, then `CLZF2.Compress` (LibLZF / LZF, a fast byte-oriented LZ77 variant — *not* gzip/zlib). Load is the exact inverse (`CLZF2.Decompress` then `DeserializeValue<T>(..., DataFormat.Binary)`).
- The three texture files are written via `ImageConversion`/`EncodeToPNG` and read back with `File.ReadAllBytes` — they are **not** CLZF2-compressed, so they open as normal PNGs.
- `world` is special: it is **not** Odin-serialized. `LevelSnapshot` hand-packs parallel arrays (`Buffer.BlockCopy` / `MemoryMarshal.Cast`) into one byte blob, then only CLZF2-compresses it. Loading requires knowing `width*height` from `levelinfo` first.
- The data save runs on a thread pool (`UniTask.SwitchToThreadPool`); the FoW textures save via async GPU readback. `GameSaver.IsSaveInProgress` is true until both finish — callers `await` it before changing scenes.

**Settings file (separate from saves):** `SettingsManager` reads/writes `options.json` in the **game working directory** (`Path.GetFullPath("./options.json")`, i.e. next to `Punk.exe`), as **plain JSON** via Newtonsoft (ASCII, indented). This file already exists in the game root. It is loaded on init and re-written on `Dispose()`. It is **skipped entirely in the editor**. Example (live in this install):
```json
{
  "gameplayOptions": { "p1GamepadRumble": 1.0, "p2GamepadRumble": 1.0, "slowMoOptions": 0,
    "disableCameraSway": false, "disableAimAssist": true },
  "videoOptions": { "width": 3440, "height": 1440, "refreshRate": 240.085,
    "screenMode": 1, "vsyncEnabled": true, "fpsCap": 0.0 },
  "audioOptions": { "masterVolume": 1.0, "sfxVolume": 1.0, "musicVolume": 1.0 }
}
```

**Local config file (optional, dev/perf):** `LocalConfig` reads a JSON file (name set in the `fileName` SerializeField) from the game root via Odin `DataFormat.JSON`, also only outside the editor. It toggles internal systems (loot dropper, particles, fog update, navmesh refresh, etc.).

## Class Index

| Class | Kind | Role |
|-------|------|------|
| `Punk.SaveLoad.GameSaver` | service class | Orchestrates run save/load; owns paths, threading, compression |
| `Punk.SaveLoad.SaveFolder` | readonly struct | Computes per-file paths inside a slot folder |
| `Punk.SaveLoad.LevelInfo` | struct | Level width/height header |
| `Punk.SaveLoad.GraphSnapshot` | struct | Serializable copy of the level graph |
| `LevelSnapshot` | struct | Packed byte-array snapshot of the world grid |
| `GameSaverInstaller` | MonoBehaviour / `IServiceInstaller` | Registers `GameSaver` |
| `IMemento` | abstract class | Marker base for all memento value objects |
| `IMementoOriginator` / `IMementoOriginator<T>` | interface | `CreateMemento()` / `RestoreFromMemento()` contract |
| `ComponentMemento` | abstract class : `IMemento` | Memento that records its `componentType` |
| `EntityData` | class : `IMementoOriginator<EntityData.Memento>` | Runtime data container for a savable entity |
| `EntityData.Memento` | class : `IMemento` | Serialized entity (id, transform, component mementos) |
| `ComponentData` | abstract class | Base for per-component runtime data |
| `SavableComponent<T>` | abstract MonoBehaviour | Binds a MonoBehaviour to a `ComponentData` of type T |
| `SavableEntity` | MonoBehaviour | Prefab marker; creates `EntityData`, binds children |
| `SavablesCollection` | ScriptableObject | Maps `entityId` → prefab for load-time reconstruction |
| `SaveDestroyedObjects` | `SavableComponent<Data>` | Persists which tracked child objects were destroyed |
| `ComponentScanner<T>` | MonoBehaviour | (generic physics scanner; not part of save IO) |
| `CLZF2` | static class | LZF compress/decompress for save blobs |
| `ConfigFile` | abstract ScriptableObject | Base for CSV/URL-backed config assets |
| `ConfigRegistry<TItem,TId>` | abstract : `ConfigFile`, `IRegistry`, `IGameService` | Id→item lookup config asset |
| `LocalConfig` | ScriptableObject / `IInitializable` | Optional JSON perf/dev overrides from game root |
| `LocalConfig.LocalConfigData` | serializable class | The toggle fields |
| `LocalConfigEditor` | MonoBehaviour | In-game UI to flip fog-related local config toggles |
| `OptionsData` | struct | Player settings POCO (video/audio/gameplay) |
| `SettingsManager` | `IGameService`, `IInitializable`, `IDisposable` | Loads/saves `options.json`, applies settings |
| `SettingsInstaller` | `IServiceInstaller` | Registers `SettingsManager` + `MetaProgressManager` |
| `MetaProgressManager` | class | PlayerPrefs-backed meta unlocks/death count |
| `RunData` | `IInitializable`, `IMementoOriginator<RunData.Memento>` | Run-scoped shop/resource/kill state |
| `Vault` | `IMementoOriginator<Vault.Memento>` | Stored modules, ingredients, consumables |
| `PlatformFeatures` | abstract `IGameService` | Platform abstraction (leaderboards, daily challenge) |
| `SteamPlatformFeatures` | : `PlatformFeatures` | Steamworks implementation |
| `DummyPlatformFeatures` | : `PlatformFeatures` | No-op fallback |
| `PlatformFeaturesInstaller` | `IServiceInstaller` | Installs `SteamPlatformFeatures` |
| `SteamManager` | MonoBehaviour | Steamworks.NET init/shutdown (AppId 2707980) |
| `IAnalyticsService` | interface : `IGameService` | Run/level analytics events |
| `UnityAnalyticsService` | : `IAnalyticsService`, `IInitializable` | Unity Gaming Services analytics |
| `DummyAnalyticsService` | : `IAnalyticsService` | No-op analytics |
| `AnalyticsInstaller` | `IServiceInstaller` | Installs dummy or Unity analytics |
| `BugReporter` | MonoBehaviour | Saves game, uploads log + screenshot to Discord webhook |
| `RunArguments` | struct | Carries new-run vs continue + save-folder name between scenes |

## Classes

### GameSaver: service class (in namespace `Punk.SaveLoad`)
**Purpose:** Central save/load orchestrator for an in-progress run. Registered via `GameSaverInstaller`; resolved with `ServiceLocator.Get<GameSaver>()`.
**Key fields/consts:** `NORMAL_SAVE_FOLDER_NAME = "save001"`, `COOP_SAVE_FOLDER_NAME = "coop_save001"`; `savesRootDirectory = Path.Combine(Application.persistentDataPath, "saves")`; bools `dataSaveInProgress`, `fowSaveInProgress`.
**Key methods:**
- `SaveFolder Save(bool coop)` / `Save(string folderName)` — entry point; sets both in-progress flags, calls `SaveOnThread` + `SaveFoW`.
- `async void SaveOnThread(SaveFolder)` — writes entities, `LevelInfo`, `LevelSnapshot` (world), `GraphSnapshot`, `MapIconManager`, `RunData`, `Vault` mementos.
- `SaveFoW(SaveFolder)` — async GPU readback → PNG for fow/map/scanner.
- `SaveWithOdin<T>(T, path)` → `CompressAndSave(SerializationUtility.SerializeValue(data, DataFormat.Binary), path)`.
- `CompressAndSave(byte[], path)` → `File.WriteAllBytes(path, CLZF2.Compress(data))`.
- `bool Load(string folderName)` — returns false if `entities` file missing; otherwise reconstructs level size, world, graph, entities, RunData, Vault, map.
- `LoadEntities` — rebuilds each `EntityData` by looking up `entityId` in `SavablesCollection`, restores data then components (two passes).
- `List<string> GetAllSavedGames()`, `DeleteSave(bool coop)`, `SavedGameExists(...)`, `static GetSaveFolderName(bool coop)`.
- Property `bool IsSaveInProgress` (data OR fow still saving).
**Relationships:** Pulls every subsystem from `ServiceLocator` (`Level`, `EntityManager`, `MapIconManager`, `RunData`, `Vault`, `MapDrawer`, `SavablesCollection`). Callers: `GameController.SaveAndExit`/`Load`/`DeleteSave`, `MainMenu`, `PauseScreen`, `DebugMenu`, `DebugSaveSlotWidget`, `BugReporter`.

### SaveFolder: readonly struct
**Purpose:** Given a slot root directory, exposes the absolute path of each save file.
**Key members:** private const file names (`entities`, `world`, `graph`, `levelinfo`, `fow`, `map`, `scanner`, `mapicons`, `rundata`, `vault`); `RootDirectory`; path properties `WorldFilePath`, `EntitiesFilePath`, `GraphFilePath`, `LevelInfoFilePath`, `FoWFilePath`, `MapTextureFilePath`, `ScannerTextureFilePath`, `MapIconsFilePath`, `RunDataFilePath`, `VaultFilePath`.

### LevelInfo: struct
**Purpose:** Small header storing the grid dimensions so the world blob can be reinflated.
**Key fields:** `int width`, `int height`. Ctor copies from `Level.Width`/`Level.Height`.

### GraphSnapshot: struct
**Purpose:** Serializable copy of the `LevelGraph` (the room/node connectivity).
**Key fields:** `int startingNodeIndex`, `List<LevelGraphNode> nodes`, `List<LevelGraphEdge> edges`.
**Key methods:** ctor from `LevelGraph`; `LevelGraph ToGraph()` rebuilds it.

### LevelSnapshot: struct
**Purpose:** Compact binary snapshot of the entire world grid. Bypasses Odin for speed/size.
**Key fields (parallel arrays sized width*height):** `byte[] mainBioms`, `bioms`, `cellTypes`, `backGroundCellTypes`, `byte[] fogLevels`, `scannerAreas`, `containingMergedCellRelativePosition`; `float[] heightMap`; `MergedCell[] mergedCells` (private struct: `dataId`, `rotation`, `mirror`); `PlantCell[] plants`.
**Key methods:** ctor from `Level`; ctor from `(byte[] bytes, int levelSize)`; `byte[] ToByteArray()` (sequential `Buffer.BlockCopy`); `void Apply(Level)` (writes arrays back, rebuilds merged cells via `MergedCellsRegistry`). Uses `MemoryMarshal.Cast` for struct arrays.

### IMemento / IMementoOriginator / IMementoOriginator&lt;TMemento&gt;
**Purpose:** The persistence contract. `IMemento` is an empty abstract base (a marker). `IMementoOriginator` exposes `IMemento CreateMemento()` and `void RestoreFromMemento(IMemento)`. The generic interface provides default implementations that down-cast to `TMemento` (throwing `ArgumentException` on type mismatch) so implementers only write the typed `CreateMemento()`/`RestoreFromMemento(TMemento)`.

### ComponentMemento: abstract class : IMemento
**Purpose:** Base for a component's serialized state. Adds `Type componentType` so that on load, `EntityData.RestoreComponentsFromMemento` can find the matching `ComponentData` by type. `EntityData.CreateMemento` stamps this field automatically.

### EntityData / EntityData.Memento: class
**Purpose:** The plain-C# runtime model for a savable game object; lives independent of its `GameObject`.
**Key fields:** `string entityId`, `int instanceId`, `Vector3 position`, `Quaternion rotation`, `bool isInitialized`, `bool isUnloadable`, `[OdinSerialize] Dictionary<Type,ComponentData> components`.
**Memento fields:** same scalars plus `List<IMemento> componentMementos`.
**Key methods:** `CreateMemento()` (collects mementos from every `ComponentData` that is an `IMementoOriginator`, stamping `componentType`), `RestoreFromMemento` (scalars), `RestoreComponentsFromMemento` (matches mementos to components by `Type`), `AddComponent`, `TryGetComponent<T>`, `Clone`, `Destroy`. Events `Moved`, `Destroyed`.

### ComponentData: abstract class
**Purpose:** Base for per-feature runtime data carried by an `EntityData`. Members: `EntityData entity`; abstract `Clone()`; virtual `OnCreate()`/`OnDestroy()`. A `ComponentData` becomes savable by *also* implementing `IMementoOriginator<TMemento where TMemento : ComponentMemento>`.

### SavableComponent&lt;T&gt;: abstract MonoBehaviour (T : ComponentData)
**Purpose:** Bridges a scene `MonoBehaviour` to its `ComponentData`. Implements `IEntityBindingListener`, `IComponentDataCreator`.
**Key members:** `T ComponentData`; `Type DataType`; events `OnBindToData`/`OnUnbindToData`; `Bind(EntityData)` / `Unbind(EntityData)`; virtual `OnFirstBind`, `Bind(T)`, `Unbind(T)`; abstract `T CreateData()`. Pattern: on save, the manager calls each component's data-producing path; mutation happens on the `ComponentData`, not the MonoBehaviour.

### SavableEntity: MonoBehaviour
**Purpose:** Scene/prefab marker for a savable entity. Implements `ISeedProvider`.
**Key fields:** `string entityId` (the key used in `SavablesCollection`), `bool isUnloadable`, `bool destroyWhenUnloaded`; `EntityData EntityData`.
**Key methods:** `Bind(EntityData, bool firstTime)` (binds all child `IEntityBindingListener`s), `Unbind`, `EntityData CreateData()` (instantiates `EntityData`, adds a `ComponentData` from each child `IComponentDataCreator`), `int GetSeed()`. Syncs transform ↔ `EntityData` each `Update`.

### SavablesCollection: ScriptableObject
**Purpose:** Asset mapping `entityId` → `SavableEntity` prefab, used by `GameSaver.LoadEntities` to reconstruct entities. `[CreateAssetMenu "Punk/Collections/Savables"]`.
**Key members:** nested serializable struct `EntityPrefab { string entityId; SavableEntity prefab; }`; `List<EntityPrefab> savableObjectInfos`; `SetEntities(List<SavableEntity>)` (rebuilds, warns on missing id).

### SaveDestroyedObjects: SavableComponent&lt;SaveDestroyedObjects.Data&gt;
**Purpose:** Concrete example of a savable component. Persists which tracked child GameObjects (by transform path) have been destroyed, and re-destroys them on load.
**Key members:** nested `Data : ComponentData { List<string> destroyedObjects; Clone() }`; `GameObject[] trackedObjects`; `TrackedObject[] trackedObjectPaths` (struct `{GameObject trackedObject; string path;}`); overrides `CreateData`, `Bind(Data)` (destroys saved paths), `Unbind(Data)` (records nulls). `OnValidate` builds paths via `Transform.GetPath`.

### CLZF2: static class
**Purpose:** LibLZF compression for save blobs. **Algorithm: LZF (LZ77 family), NOT gzip/zlib/deflate** — a custom byte stream. Tools decompressing PUNK saves must implement LZF.
**Key members:** consts `HLOG=14`, `HSIZE=16384`, `MAX_LIT=32`, `MAX_OFF=8192`, `MAX_REF=264`; `byte[] Compress(byte[])`, `byte[] Decompress(byte[])`, and the core `lzf_compress`/`lzf_decompress`.

### ConfigFile / ConfigRegistry&lt;TItem,TId&gt;: ScriptableObject config
**Purpose:** `ConfigFile` is an abstract ScriptableObject with a `url` and abstract `Parse(string csv)` (CSV-from-URL config authoring). `ConfigRegistry<TItem,TId>` extends it into an id→item lookup (`IRegistry`, `IGameService`, `IInitializable`): holds `List<TItem> itemList`, builds a dictionary in `Initialize()`, `Get(id)` and `AllItems`. These are content/balance configs, loaded as assets — not player save data.

### LocalConfig / LocalConfig.LocalConfigData: ScriptableObject, IInitializable
**Purpose:** Optional developer/performance override file. On `Initialize()` (skipped in editor), if a file named by the `fileName` SerializeField exists at `Path.GetFullPath("./" + fileName)` (game root), it is read and Odin-deserialized as `DataFormat.JSON` into `data`.
**`LocalConfigData` fields (all bool, default true):** `enableLootDropper`, `enableCellDeathParticles`, `enableNavmeshRefresh`, `enableCellRefresh`, `enableColliderRefresh`, `enableFireParticles`, `renderFogOverlay`, `enableFogUpdate`, `enableFogBufferUpdate`.

### LocalConfigEditor: MonoBehaviour
**Purpose:** In-game UI hooking three fog toggles (`fogSimulationToggle`→`enableFogUpdate`, `fogOverlayToggle`→`renderFogOverlay`, `fogGpuUpdateToggle`→`enableFogBufferUpdate`) to the live `LocalConfig.data` from `ServiceLocator.Get<LocalConfig>()`. Note: edits the in-memory object; it does not write the file back.

### OptionsData: struct
**Purpose:** The serialized player-settings POCO (matches `options.json`).
**Nested:** `VideoOptions { int width, height; double refreshRate; ScreenMode screenMode (Windowed|Borderless); bool vsyncEnabled; float fpsCap; }`; `AudioOptions { float masterVolume, sfxVolume, musicVolume; }`; `GameplayOptions { float p1GamepadRumble, p2GamepadRumble; SlowMoOptions slowMoOptions (Default|Always|Never); bool disableCameraSway; bool disableAimAssist; }`.
**Fields:** `gameplayOptions`, `videoOptions`, `audioOptions`.

### SettingsManager: IGameService, IInitializable, IDisposable
**Purpose:** Loads/persists `options.json` and applies settings to engine/audio.
**Key members:** static `fileName = "options.json"`; `OptionsData optionsData`; `string filePath => Path.GetFullPath("./options.json")`; properties `GetCurrentSettings`, `VideoOptions`, `GameplayOptions`.
**Key methods:** `Initialize()` (load or default, then apply all three groups), `CreateDefaultOptions()`, three `Apply(...)` overloads (audio→`AudioManager.ApplySettings`, video→`QualitySettings.vSyncCount` + `Screen.fullScreenMode`, gameplay→store), `bool LoadOptions()` (Newtonsoft `JsonConvert.DeserializeObject<OptionsData>`, skipped in editor), `Dispose()` (Newtonsoft serialize → `File.WriteAllBytes`, skipped in editor).
**Relationship:** Registered by `SettingsInstaller`. Editor builds never read or write the file.

### SettingsInstaller: IServiceInstaller (MonoBehaviour)
Installs `new SettingsManager()` **and** `new MetaProgressManager()` into the `ServiceContainer`.

### MetaProgressManager: class
**Purpose:** Cross-run meta progression via Unity `PlayerPrefs` (NOT the save folder).
**Keys:** `META_UNLOCKED_LOADOUTS`, `META_TOTAL_DEATH_COUNT`.
**Methods:** `int GetTotalDeathCount()`, `RegisterDeath()`, `string[] GetUnlockedLoadouts()` (semicolon-split), `bool UnlockLoadout(LoadoutTemplate)`, private `SetUnlockedLoadouts`, static `ResetUnlockedLoadouts()`.

### RunData / RunData.Memento: IInitializable, IMementoOriginator
**Purpose:** Run-scoped persistent state (saved to the `rundata` file).
**Memento fields:** `Dictionary<string,float> sharedResources`, `ShopItemList.Memento shopItems`, `List<ConsumableShopItem.Memento> consumableShopItems`, `int unlockedShopCount`, `List<string> ingredientsEverOwned`, `droppedModuleIds`, `moduleIdsAddedToShop`, `moduleIdsPickedUp`, `float totalRunTime`, `int killedBossCount`, `int killedEnemyCount`.
**Key methods:** `CreateMemento()`/`RestoreFromMemento(Memento)` (re-resolve ids via `ModuleRegistry`/`IngredientRegistry`), plus run-tracking helpers (`RegisterModuleDropped`, `RegisterShopUnlock`, `RegisterEnemyKilled`, etc.). Note mementos store **string ids**, not object refs — registries rehydrate them.

### Vault / Vault.Memento: IMementoOriginator
**Purpose:** The player's persistent stash (saved to the `vault` file).
**Memento fields:** `List<Module.Memento> modules`, `List<string> ingredientIds`, `List<int> ingredientCounts`, `List<ConsumableMento> consumables` (struct `{string consumableId; int amount;}`).
**Key methods:** `CreateMemento()`, `RestoreFromMemento()` (rehydrates via `IRegistry<ModuleData,string>`, `IRegistry<Ingredient,string>`, `IRegistry<Consumable,string>` then `Module.DeepCopy()` + `RestoreFromMemento`). Runtime API: `Store/Remove(Module)`, `Add/Remove(Consumable,int)`, `Add/Remove(Ingredient,int)`, events `ConsumableAmountChanged`/`IngredientAmountChanged`/`NewModuleSeen`.

### PlatformFeatures: abstract IGameService
**Purpose:** Platform abstraction for leaderboards & daily challenge.
**Members:** `Action Initialized`, `Action<int> LeaderboardEntriesChanged`, `bool IsInitialized`; abstract `Initialize`, `Authenticate(Action, Action<string>)`, `SubmitScoreToLeaderboard(name, score, isCoop)`, `LoadLeaderboardEntries(name)`, `HasPlayerParticipateOnDailyChallenge(...)`, `LeaderboardAdapter GetLeaderboardAdapter()`.

### SteamPlatformFeatures : PlatformFeatures
**Purpose:** Steamworks.NET implementation. `const LEADERBOARD_VERSION = 1`. Uses `UploadSteamLeaderboardRequest`/`LoadSteamLeaderboardRequest`, checks `SteamManager.Initialized`, returns a `SteamLeaderboardAdapter`. Daily-challenge check loads `DailyChallengeInfo.latestData.leaderboardId`.
**Installed by:** `PlatformFeaturesInstaller` (always installs the Steam variant; `DummyPlatformFeatures` is the no-op alternative).

### SteamManager: MonoBehaviour [DisallowMultipleComponent]
**Purpose:** Steamworks.NET lifecycle. **AppId `2707980`** (`SteamAPI.RestartAppIfNecessary`). `Awake` runs packsize/DLL checks then `SteamAPI.Init()`; `Update` pumps `SteamAPI.RunCallbacks()`; `OnDestroy` calls `SteamAPI.Shutdown()`. Singleton via static `s_instance` + `Initialized` property; `DontDestroyOnLoad`.

### IAnalyticsService / UnityAnalyticsService / DummyAnalyticsService
**Purpose:** Telemetry. `IAnalyticsService : IGameService` defines `SendRunStarted()`, `SendRunEnded(int runDuration, string endType, float fuelLevel)`, `SendLevelGenerated(float)`, plus `Constants` (`win`/`gameOver`/`restart`/`quit`). `UnityAnalyticsService` uses Unity Gaming Services (`UnityServices.InitializeAsync`, `AnalyticsService.Instance.RecordEvent`, `RunEndedEvent`/`LevelGeneratedEvent`), choosing `development`/`production` env. `DummyAnalyticsService` no-ops. `AnalyticsInstaller` picks one via its `useDummy` SerializeField.

### BugReporter: MonoBehaviour
**Purpose:** Pauses + finishes any in-progress save (`await WaitWhile(gameSaver.IsSaveInProgress)`), then posts a forum thread to a hardcoded **Discord webhook** with the system info markdown, console log (`Application.consoleLogPath`) and a screenshot, zipped. (`DiscordWebhook` is an external referenced library, not a game type.)

### RunArguments: struct
**Purpose:** Passed between scenes to tell `GameController` whether to start fresh or continue. Fields: input devices, `bool isCoop`, `int seed`, `DailyChallengeInfo.DailyChallengeData`, `LoadoutTemplate startingLoadout`, `bool isContinue`, `string saveFolder`. Factories `NewRun(coop)` and `Continue(coop, saveFolder)`. `GameController.Load` calls `GameSaver.Load(runArguments.saveFolder)` when continuing.

## Modding Notes

### How the save cycle is driven (hook points)
- **Save** is triggered only by `GameController.SaveAndExit()` (and the debug widgets). It calls `ServiceLocator.Get<GameSaver>().Save(IsCoop)` then `await`s `IsSaveInProgress`. The actual writes happen inside `GameSaver.SaveOnThread`/`SaveFoW` on background threads.
- **Load** is triggered by `GameController.Load()` → `GameSaver.Load(folderName)` when `RunArguments.isContinue` is true. The continue button only appears if `GameSaver.SavedGameExists(coop)` (checked in `MainMenu`).
- **Delete** happens on run-end (`GameController` line ~213, `PauseScreen`, `MainMenu`).

Best Harmony targets to hook the cycle:
- `GameSaver.SaveOnThread` (Postfix) — write your own extra files into the same slot. You have the `SaveFolder folder` argument, so use `folder.RootDirectory` to place a sibling file. (Prefix/Postfix on `Save(string)` gives you the `SaveFolder` return value too.)
- `GameSaver.Load` (Postfix, `string folderName`) — reload your sibling file. Reconstruct the `SaveFolder` as `new SaveFolder(Path.Combine(persistentDataPath, "saves", folderName))` or just `Path.Combine(Application.persistentDataPath, "saves", folderName, "mymod.dat")`.
- `GameSaver.CompressAndSave` / `SaveWithOdin` (private; patch via `AccessTools.Method`) if you want to intercept every blob.

### Persisting your own mod data
Three viable strategies, easiest first:
1. **PlayerPrefs** (like `MetaProgressManager`) for small cross-run values — trivially Harmony-free; just `PlayerPrefs.SetString("MyMod_x", ...)`. Survives independent of save slots.
2. **Sibling file in the save folder** — Postfix `GameSaver.SaveOnThread`/`Load` and read/write `Path.Combine(folder.RootDirectory, "mymod")`. Match the engine's scheme by reusing `CLZF2.Compress`/`Decompress` + `Sirenix.Serialization.SerializationUtility.SerializeValue(obj, DataFormat.Binary)`, or just use plain JSON if you don't need to interop. This is automatically slot-scoped (and auto-deleted with the slot, since `DeleteSave` removes the directory recursively).
3. **Per-entity component memento** — if your data belongs to a specific entity, add a `ComponentData` subclass that implements `IMementoOriginator<YourMemento>` where `YourMemento : ComponentMemento`, attached via a `SavableComponent<YourData>` on the prefab. `EntityData.CreateMemento`/`RestoreComponentsFromMemento` will then serialize/restore it automatically through the existing `entities` file — no GameSaver patch needed. The matching prefab must be registered in the `SavablesCollection` asset for load-time reconstruction (the entity is rebuilt by `entityId`).

Caveat: the binary blobs are Odin-serialized; if you add fields to existing memento types via patching you risk breaking deserialization of pre-existing saves. Prefer additive sibling files over mutating engine memento layouts. Also note saving runs on a thread pool — keep Harmony postfix work thread-safe (no Unity API calls off the main thread except `File`/byte work, mirroring how `GameSaver` itself behaves).

### Forcing config / settings values
- **Player options:** Harmony-Postfix `SettingsManager.LoadOptions` (or `Initialize`) and overwrite `optionsData` (private field via `AccessTools.FieldRefAccess`), or Prefix the `Apply(...)` overloads to clamp values. Remember the manager **skips file IO in the editor** and writes on `Dispose()` — patch `Dispose` if you want to prevent your forced values from being persisted back. The on-disk file is plain JSON (`options.json` next to `Punk.exe`), so a launcher/mod can also just edit it directly.
- **Perf/dev toggles:** `LocalConfig.data` (a `LocalConfigData`) is a live object resolved from `ServiceLocator.Get<LocalConfig>()`. A Postfix on `LocalConfig.Initialize` can set any of the `enable*`/`render*` bools. There is no editor guard on reading the object itself (only on file loading), so overriding in memory is reliable.
- **Content/balance:** `ConfigRegistry<TItem,TId>` builds its dictionary in `Initialize()`; Postfix it (or mutate the protected `itemList` before init) to inject/replace items. Resolve the concrete registry via `ServiceLocator.Get<...>()`.
- **Meta unlocks:** `MetaProgressManager` is pure PlayerPrefs — set `META_UNLOCKED_LOADOUTS` (semicolon-joined names) / `META_TOTAL_DEATH_COUNT` directly, or Postfix `GetUnlockedLoadouts`.

### Platform
- `SteamManager.Awake` hardcodes AppId `2707980`; `SteamManager.Initialized` gates all `SteamPlatformFeatures` calls. To run leaderboard code without Steam, you'd swap the installed service — Prefix `PlatformFeaturesInstaller.InstallServices` to install `DummyPlatformFeatures` instead.
- Analytics can be disabled by forcing `AnalyticsInstaller.useDummy` true (Prefix `InstallServices`), which installs `DummyAnalyticsService`.
