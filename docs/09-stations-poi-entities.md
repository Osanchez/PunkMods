# Stations, POIs, Entities & Plants
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

Besides the terrain itself (cells/tiles, biomes), the PUNK world is populated by a data-driven **entity** system layered on top of the level graph. The pieces fit together like this:

- **The level graph** (`LevelGraph` of `LevelGraphNode`/`LevelGraphEdge`) is the skeleton. Each node has a `poiId`, `interestLevel`, `biomeId`, `roomSetupIndex` and `center`. Generation systems mutate these node fields.
- **PoI** (Point of Interest) ScriptableObjects describe *what kind of content* a node holds. `PoIGenerator` assigns `poiId`s to graph nodes (Burst job), spreading "interest level" outward and optionally spreading biomes. `StationGenerator` is a specialized placer that stamps the dedicated `stationPoI` onto suitable nodes. `Scanner`/dungeon generators do similar work but live in other doc categories.
- **EntityGenerator** walks every graph node after PoIs are assigned and instantiates content: for a node *with* a PoI it draws a prefab from that PoI's `entities` distribution; for a node *without* one it rolls each `PlacedEntity` in the node's `RoomSetup`. Each placed prefab's `SavableEntity` components are turned into lightweight, serializable `EntityData` records.
- **EntityData / EntityManager / SpatialGrid** are the runtime model. `EntityData` is a pure-data object (position, rotation, instanceId, a dictionary of `ComponentData`). `EntityManager` queries entities by component or instance id; the `SpatialGrid` buckets them by level segment. GameObjects are spawned/despawned on demand by `EntityGameObjectManager` as segments stream in and out — only `isUnloadable` entities are virtualized. `SavableEntity` binds a prefab to its `EntityData` and forwards Bind/Unbind to every `IEntityBindingListener` in its hierarchy.
- **Stations** are entities carrying a `Station.Data` component: upgradeable shops connected to each other via a minimum-spanning-tree of `StationConnection` pipes, lit by `StationLightManager`/`StationLightSource`, shown on the map by `StationMapIcon`, and reachable through `FastTravelManager`. Locked stations can emit a screen-space distress signal (`StationDistressSignalUpdater`).
- **Plants** come in two flavors, both produced during level generation from per-biome `Ecosystem` lists:
  - **Tile-based plants** (`PlantGeneratorJob`, Burst) grow a connected graph of `PlantCell`s directly into the level's `plants` array. They are rendered by `PlantTilemapUpdater` + `PlantTile` (a custom `RuleTile`) and structurally destroyed cell-by-cell by `PlantDestructor`. Fruit positions become standalone `PlantFruit` entities.
  - **Entity plants** (`EntityPlantGeneratorJob` finds seeds; `EntityPlant.Data.Generate` builds a recursive branch tree) are full GameObjects with procedurally instantiated branch visuals (`StraightPlantBranchVisual`/`CurvedPlantBranchVisual`), shakeable fruit (`EntityPlantFruit`), and save support.
- **Saving** is via the memento pattern: `EntityData.CreateMemento()` snapshots each entity and its `IMementoOriginator` components; `RestoreFromMemento` rebuilds them. `IEntitySavingListener.OnSaveEntity` lets components react to a save.
- **Instruments** are special discoverable entities that reveal the nearest undiscovered POI of a configured kind on the map.

## Class Index

| Class | Kind | Role |
|---|---|---|
| `Station` | MonoBehaviour : `SavableComponent<Station.Data>` | Runtime station behaviour: unlock, shop, upgrades, hatch |
| `Station.Data` | data + memento originator | Saved station state (upgrades, connections, light polygon) |
| `StationConnection` | MonoBehaviour | Procedural pipe mesh + traveling "bulge" between two stations |
| `StationGenerator` | `IInitializable` | Places stations on graph nodes, builds connection MST + light polygons |
| `StationPlatform` | MonoBehaviour | Animator wrapper for the station hatch open/close |
| `StationUpgrade` | `[Serializable]` data | One purchasable station upgrade |
| `StationMapIcon` | MonoBehaviour | Map icon + fast-travel button for a station |
| `StationLightManager` | MonoBehaviour | Spawns/destroys `StationLightSource` per unlocked station |
| `StationLightSource` | MonoBehaviour | Tweened Light2D circle + polygon reveal around a station |
| `StationDistressSignalUpdater` | MonoBehaviour | Screen-space distress pulse shader feed for locked stations |
| `PoI` | ScriptableObject : `IIdentifiable<int>` | Defines content type for graph nodes |
| `PoIDistribution` / `PoIDistributionItem` | `[Serializable]` | Weighted distribution of PoIs |
| `PoIGenerator` | `IInitializable` | Burst job assigning PoIs to nodes + interest/biome spread |
| `PoIRegistry` | ScriptableObjectRegistry | id→PoI lookup |
| `POICameraTarget` | MonoBehaviour | Adds itself as a ProCamera2D target when ship is near |
| `PotentialDungeonLocation` | plain class | Candidate dead-end "main path" for a dungeon |
| `PotentialDungeonLocationCollector` | plain class | Walks graph to collect dungeon candidates |
| `EntityData` | data + memento originator | Pure-data entity record with component dictionary |
| `EntityGenerator` | `IInitializable` | Instantiates entities per graph node (PoI or RoomSetup) |
| `EntityManager` | `IInitializable` | Entity queries + movement/destroy events over `SpatialGrid` |
| `EntityGameObjectManager` | MonoBehaviour | Streams entity GameObjects in/out per active segment |
| `EntityMapItem` | MonoBehaviour : `IEntityBindingListener` | Registers an entity's map icon on bind |
| `SavableEntity` | MonoBehaviour : `ISeedProvider` | Prefab side of an entity; bind/unbind + data creation |
| `SavableComponent<T>` | abstract MonoBehaviour | Base for entity components (Bind/Unbind/CreateData) |
| `SavablesCollection` | ScriptableObject | entityId→prefab map for respawning entities |
| `SavableEntityDistribution` / `Item` | `[Serializable]` | Weighted distribution of `SavableEntity` |
| `IEntityBindingListener` | interface | Bind/Unbind callbacks on a `SavableEntity` |
| `IEntitySavingListener` | interface | `OnSaveEntity` callback |
| `Ecosystem` | ScriptableObject | Per-biome enemy/plant/entity-plant distributions |
| `EntityPlant` | MonoBehaviour : `SavableComponent<EntityPlant.Data>` | GameObject-based procedural plant |
| `EntityPlant.Data` | data + memento | Branch tree + fruit list; recursive `Generate` |
| `EntityPlantData` | ScriptableObject : `IIdentifiable<byte>` | Tunables for entity plants |
| `EntityPlantFruit` | MonoBehaviour : `IProjectileListener`, `IExplosionListener` | Shakeable, detachable fruit on an entity plant |
| `EntityPlantGeneratorJob` | Burst `IJob` | Finds entity-plant seed positions per grid cell |
| `CurvedEntityPlantSegment` | MonoBehaviour | One sprite segment of a curved branch |
| `PlantGenerator` | plain class | Drives both plant Burst jobs and spawns results |
| `PlantGeneratorJob` | Burst `IJob` | Grows tile-based plant cell graph |
| `PlantCell` | struct | One tile-plant cell: type, instance id, 4 connection dirs |
| `PlantType` | ScriptableObject : `IIdentifiable<byte>` | Tunables for tile-based plants |
| `PlantTile` | ScriptableObject : `RuleTile<...>` | Custom rule tile reading plant connection data |
| `PlantTilemapUpdater` | `TilemapUpdater` | Refreshes plant tilemap on destruction |
| `PlantFruit` | MonoBehaviour | Tile-plant fruit: sway, detach on plant destroy |
| `PlantDestructor` | MonoBehaviour | Cascading structural destruction of plant cells |
| `PlantBranchVisualBase` | abstract MonoBehaviour | Base for branch visuals; shake propagation |
| `StraightPlantBranchVisual` | `PlantBranchVisualBase` | GPU-instanced straight branch |
| `CurvedPlantBranchVisual` | `PlantBranchVisualBase` | Segment-built curved branch |
| `FastTravelManager` | MonoBehaviour | Teleport ships between stations / to any entity |
| `Instrument` | MonoBehaviour : `SavableComponent<Instrument.Data>` | Discoverable that reveals nearest POI on map |
| `InstrumentDiscoverable` | ScriptableObject | Configures which entities an instrument reveals |
| `InstrumentMapIcon` | MonoBehaviour | Swaps instrument map sprite when used |
| `Biom` | ScriptableObject : `IIdentifiable<byte>` | Biome (reference): holds ecosystems + room setups |

## Classes

### Station : MonoBehaviour : SavableComponent<Station.Data>
**Purpose:** Runtime behaviour of a space station — unlocking (first upgrade), shopping, upgrade installation, and the hatch animation.

**Key fields:** `StationUpgrade[] upgrades`, `Shop shop`, `GameObject unlockPrompt`, `GameObject shopPrompt`, `StationPlatform platform`, `Collider2D interactionCollider`, `EnemyTrackingSystem enemyTrackingSystem`, `bool emitDistressWhenLocked`, `GameObject enemyCollider`.

**Key members:**
- `bool Interactable` — proxies `interactionCollider.enabled`.
- `Update()` — while locked, station is interactable only when the tracking system sees no enemy.
- `OnUseActivated(Interactor)` — if locked, tries to install `upgrades[0]`; otherwise opens the shop for the interacting `Ship`.
- `TryInstallUpgrade(StationUpgrade, Unit)` (private) — checks resource cost, deducts it, registers the purchase, calls `Data.Install`.
- `CalculateUpgradeCost(StationUpgrade)` (private) — scales price by `runData.GetTimesStationUpgradePurchased` using the upgrade's `PriceIncreaseMode` (Add/Multiply).
- `PlayStartSequence(...)`, `PlayTeleportArrivalSequence(float)` — async open + light spawn; used at run start and on fast-travel arrival.
- `OpenHatch/CloseHatch`, `Bind/Unbind/CreateData`.

#### Station.Data : ComponentData, IMementoOriginator<Memento>
Saved state. Fields: `List<StationUpgrade> allUpgrades`, `installedUpgrades`, `float nearestStationDistance`, `bool isFastTravelDestination`, `bool skipOpenAnim`, `List<Data> connectedStations`, `Vector2[] lightPolygon`, `Triangle[] triangles`, `List<(Vector2,Vector2)> missingVonorois`, `bool emitDistressWhenLocked`. `bool IsUnlocked => installedUpgrades.Count > 0`. `event Action<Data,StationUpgrade> UpgradeInstalled`. `Install(string id)` / `Install(StationUpgrade)`. The nested `Memento` stores upgrade ids, distance, connected station instance ids and the light polygon.

### StationConnection : MonoBehaviour
**Purpose:** The animated "pipe" drawn between two connected stations, with a glowing bulge that travels along it during fast travel.
**Key fields (all `[SerializeField]` unless noted):** geometry/curve tunables (`segmentLength`, `segmentWidth`, `overlap`, `curveDistance`, `curveAmplitude`, `endAnchorAngle`, `zPosition`), bulge tunables (`bulgeSpeedSlow/Fast`, `bulgeScaleSmall/Big`), `string itemTravelingSfx` (public). Holds private `station1`/`station2` (`Station.Data`) and a `VertexPath`.
**Key members:**
- `SetPositions(Station.Data, Station.Data)` — builds a Perlin-perturbed Bezier/VertexPath between the two stations and generates the pipe mesh via `SegmentedPipeGenerator.CreateMesh`.
- `ShowTravelFrom(Station.Data)` / `ShowTravelTo(Station.Data)` — animate the bulge in the correct direction.
- `static StationConnection GetConnectionForStation(Station.Data)` — `FindObjectsOfType` scan to find the pipe touching a station.

### StationGenerator : IInitializable
**Purpose:** Places stations and wires up their connection network + light polygons. Pulls `Seed`, `GraphGenerator`, `LevelGeneratorConfig`, `LevelElementsCollection`, `EntityManager` from `ServiceLocator`.
**Key members:**
- `GenerateStations(Level)` — runs `PlaceBases` on a thread pool.
- `PlaceBases(Level, Rnd)` (private) — for each empty node, computes distance to nearest existing base and to the level center, then probabilistically places a base using `config.stationDistanceAtCenter`/`stationDistanceAtEdge`. Hardcoded poi ids `1` and `5` seed the initial base list. Calls `PlaceBase` → `graphGenerator.SetPoi(graph, nodeIndex, config.stationPoI)`.
- `InitializeStations(Level)` — after entities exist: installs `"FuelDispenser"` on the starting station, computes each station's `nearestStationDistance`, clears terrain in `config.stationClearRadius` around each, then Delaunay-triangulates station positions (`BowyerWatson.Triangulate`), connects them via `Kruskal.MinimumSpanningTree`, and builds each station's Voronoi `lightPolygon`.
- `IsStartingStation(Level, Station.Data)` — compares position to `graph.startingNodeIndex` center.

### StationPlatform : MonoBehaviour
Thin animator wrapper. `OpenImmediate()`, `Open(bool isFirstOpen, bool isJustActivated)`, `Close()` toggle the `IsOpen`/`OpenImmediate`/`FirstOpen`/`Activate` animator bools.

### StationUpgrade `[Serializable]`
Plain data for one upgrade. Fields: `string id`, `int cost`, `Resource resourceUsed`, `GameObject activatedObject`, `string animTriggerName`, `Sprite mapIconSprite`, `PriceIncreaseMode priceIncreaseMode`, `float priceIncreaseAmount`. **Enum:** `PriceIncreaseMode { Add, Multiply }`.

### StationMapIcon : MonoBehaviour
Map icon for a station. Listens to `MapIcon.TargetChanged` to grab the `Station.Data`, swaps `stationSpriteRenderer.sprite` to the installed upgrade's `mapIconSprite`, and raises `event Action<Station.Data> TravelButtonClicked`. `EnableTravelButton(bool)` only enables travel for unlocked stations.

### StationLightManager : MonoBehaviour
Listens to `GameController.LevelGenerated`; for each station entity registers `UpgradeInstalled` and `SpawnLight`s already-unlocked stations. `SpawnLight(Station.Data)` instantiates `lightPrefab` (a `StationLightSource`) at the station and calls `Appear`. `DestroyLight(Station.Data)` removes it.

### StationLightSource : MonoBehaviour
Tweens a `Light2D circleLight` (inner/outer radius via `innerCurve`/`outerCurve`) then hands off to a polygon `Light2D` whose shape is `station.lightPolygon`. `outerRadius` is derived from `station.nearestStationDistance`. Uses DOTween. Single public method `Appear(Station.Data)`.

### StationDistressSignalUpdater : MonoBehaviour
Drives a global shader effect that pulses a red ring outward from locked stations that have `emitDistressWhenLocked`. Builds a `ComputeBuffer` of `DistressSource{worldPosition,color}` (`RefreshBuffer`) and animates `_StationDistressRadius`/`_DistressSignalStrength` globals each frame. `TriggerSfx()` plays `signalStartSfx` from the nearest locked station within `maxSfxDistance`. Subscribes to `GameController.LevelGenerated`. **Note:** `OnDisable` uses `Delegate.Combine` (so it does not actually unsubscribe — a latent bug; relevant if you patch level regeneration).

### PoI : ScriptableObject : IIdentifiable<int>
**Purpose:** Describes the content type assigned to a graph node. CreateAssetMenu `Punk/Level/PoI`.
**Key fields:** `int id`, `int interestLevel`, `GameObjectDistribution entities`, `EntitySelectionMode entitySelectionMode`, `float difficultyMultiplier`, `float enemyPlacementDeadZoneRadius`, `bool overrideRoomSetup` + `RoomSetup roomSetup`, `bool spreadBiome` + `Biom biome` + `int biomeSpreadRange`, `string debugIcon`. **Enum:** `EntitySelectionMode { Random, RoundRobin }`. `AutoAssignId()` assigns the next free int id.

### PoIDistribution / PoIDistributionItem `[Serializable]`
Trivial weighted-distribution subclasses (`Distribution<PoI, PoIDistributionItem>` / `DistributionItem<PoI>`).

### PoIGenerator : IInitializable
**Purpose:** Assigns regular PoIs to graph nodes using a Burst job, spreading interest level along edges and optionally spreading biomes afterward.
**Key members:**
- `PlacePoIs(LevelGenerationContext)` — packs each `config.regularPois` entry into a `PoIGeneratorJob.PoIToPlace`, builds biome-filter and main-path native collections, schedules the job, then for each placed node calls `graphGenerator.SetPoi` and feeds `BiomeSpreader` for PoIs with `spreadBiome`.
- nested **`[BurstCompile] struct PoIGeneratorJob : IJob`** — `Execute` iterates `poisToPlace`, collects compatible nodes (`CanPlace` checks empty `poiId`, `maxInterestLevel`, main-path allowance, `minDistanceFromCenter`, and biome whitelist/blacklist), and places up to `maxAmount` respecting `minDistanceFromSamePoi`. `SpreadInterest` recursively decrements interest across edges. The `PoIToPlace` struct mirrors `LevelGeneratorConfig.PoIConfig`.

### PoIRegistry : ScriptableObjectRegistry<PoI, int>
id→PoI registry asset (`Punk/Registries/PoI registry`). Resolve with `ServiceLocator.Get<PoIRegistry>().Get(id)`.

### POICameraTarget : MonoBehaviour
Adds itself as a `ProCamera2D` camera target (with `targetInfluenceH/V`, `targetOffset`, `duration`) when the average alive-ship position comes within `activationDistance`, and removes itself when ships leave. Use to make the camera frame a POI.

### PotentialDungeonLocation (+ MainNode) / PotentialDungeonLocationCollector
Graph-analysis helpers (dungeon placement, documented here because they consume the same `LevelGenerationContext`). `PotentialDungeonLocation` holds an `endNodeIndex`, an `entranceNodeIndex`, and a `mainPath` of `MainNode{nodeIndex, subGraphs}`. `PotentialDungeonLocationCollector.CollectPotentialDungeonLocations(context)` walks from each dead-end node (`numberOfConnections == 1`) inward, recording the linear corridor and the side sub-graphs branching off it, and appends results to `context.potentialDungeonLocations`.

### EntityData : IMementoOriginator<Memento>
**Purpose:** The pure-data runtime representation of any world entity. Not a MonoBehaviour.
**Key fields:** `string entityId`, `int instanceId`, `Vector3 position`, `Quaternion rotation`, `bool isInitialized`, `bool isUnloadable`, `[OdinSerialize] Dictionary<Type,ComponentData> components` (private).
**Events:** `Action<EntityData,Vector3,Vector3> Moved`, `Action<EntityData> Destroyed`.
**Key members:** `MoveTo(Vector3)`, `TryGetComponent<T>` / `TryGetComponentImplementing<T>` / `TryGetComponent(Type,...)`, `AddComponent(ComponentData)`, `Clone()`, `Destroy()`, `OnCreate()`, `CreateMemento()` / `RestoreFromMemento` / `RestoreComponentsFromMemento`. This is the core save unit: components implementing `IMementoOriginator` get their mementos captured here.

### EntityGenerator : IInitializable
**Purpose:** Populates the level with entities once the graph (with PoIs and room setups) is ready.
**Key members:**
- `PlaceEntities(Level, int seed)` — runs `EnemyGenerator.PlaceEnemies` (if `config.generateEnemies`) then `PlaceObjects`.
- `PlaceObjects(Level, Rnd)` (private) — for each node: if it has **no** PoI, rolls each `RoomSetup.PlacedEntity.probability` and adds the entity at the node center; if it **has** a PoI, draws a prefab via `SelectPrefabForPoi` and adds every `SavableEntity` in the prefab tree (preserving child offsets). New entities get an `instanceId` from `level.entityManager.CreateInstanceId()` and have `Unit.Data.SpawnRoomIndex` set.
- `SelectPrefabForPoi(PoI, Rnd)` — implements `Random` vs `RoundRobin` selection from `poi.entities`.
- `PlaceGameObjectsForRooms(Level)` — instantiates each room setup's `gameObjectAtCenter`.

### EntityManager : IInitializable
**Purpose:** Central registry/query layer over the `SpatialGrid` (documented here as it is the world-content access point).
**Events:** `Action<EntityData,Vector2Int,Vector2Int> EntityMovedToNewSegment`, `Action<EntityData> EntityDestroyed`.
**Key members:** `int CreateInstanceId()` (random), `Add(EntityData, Vector3)`, `GetEntitiesInSegment(Vector2Int)`, `GetAllEntities()`, `GetShips()` (entityId `"Ship"`), `IReadOnlyList<T> GetEntitiesWithComponent<T>()`, `EntityData GetEntity(int instanceId)`. This is the go-to API for finding stations, instruments, etc. at runtime.

### EntityGameObjectManager : MonoBehaviour
**Purpose:** Streams entity GameObjects in and out as level segments activate/deactivate, so only nearby entities are instantiated.
**Key fields:** `SavablesCollection savablesCollection` (entityId→prefab), `entityGameObjects` (instanceId→`SavableEntity`), `activeSegments`.
**Key members:** `CreateEntity(SavableEntity prefab, Vector2)` (data + spawn), `InstantiateGameObjects(Vector2Int segment)` (spawns all `isUnloadable` entities in a segment), `SpawnObjectForEntity(EntityData)` / `SpawnPrefabForEntity(EntityData, SavableEntity)` (instantiate + `Bind`), `DestroyGameObjects(LevelSegmentComponent)`, `UnloadEntity(int)`, `TryGetSavableEntity(int, out SavableEntity)`. Subscribes to `EntityManager.EntityMovedToNewSegment` to unload entities leaving active segments.

### EntityMapItem : MonoBehaviour : IEntityBindingListener
On `Bind(EntityData)` registers itself with `MapIconManager.AddItem`. Exposes `EntityData Entity` and `MapIcon IconPrefab`. Attach to a prefab to give an entity a map marker.

### SavableEntity : MonoBehaviour : ISeedProvider
**Purpose:** Prefab-side glue between a GameObject hierarchy and its `EntityData`.
**Key fields:** `string entityId`, `bool isUnloadable`, `bool destroyWhenUnloaded`. `EntityData EntityData {get;}`, `bool IsUnloadable` (proxies `EntityData.isUnloadable`).
**Key members:** `Bind(EntityData, bool isFirstTime)` / `Unbind(EntityData)` forward to all `IEntityBindingListener` children; `CreateData()` builds an `EntityData` and adds a `ComponentData` from every `IComponentDataCreator` child; `int GetSeed() => EntityData.instanceId`. `Update()` writes transform changes back into the `EntityData`.

### SavableComponent<T> : MonoBehaviour, IEntityBindingListener, IComponentDataCreator
Abstract base for entity components (Station, Instrument, EntityPlant all derive from it). Provides `T ComponentData`, events `OnBindToData`/`OnUnbindToData`, virtual `OnFirstBind()`, `Bind(T)` / `Unbind(T)`, and abstract `T CreateData()`. `Bind(EntityData)` resolves the component of type `T` from the entity and calls `OnFirstBind` when the entity is freshly created.

### SavablesCollection : ScriptableObject
`List<EntityPrefab{entityId, SavableEntity prefab}>` used by `EntityGameObjectManager` to re-instantiate entities by id. `SetEntities(List<SavableEntity>)` rebuilds the map (warns on missing ids).

### SavableEntityDistribution / Item, IEntityBindingListener, IEntitySavingListener
Weighted distribution of `SavableEntity`. `IEntityBindingListener{Bind/Unbind(EntityData)}` and `IEntitySavingListener{OnSaveEntity(SavableEntity)}` are the two component hook interfaces.

### Ecosystem : ScriptableObject
**Purpose:** Per-biome content definition consumed by enemy and plant generation. CreateAssetMenu `Punk/Ecosystem`.
**Key fields:** `EnemyDistribution enemies`, `List<Plant> plants`, `List<EntityPlant> entityPlants`, `MinMaxFloat roomDifficultyRangeClose`/`roomDifficultyRangeFar`, `AnimationCurve roomDifficultyScaleCurve`.
**Nested structs:** `Enemy{minDistanceFromCenter,int powerLevel, SavableEntity entity}`; `Plant{PlantType plantType, NoiseSetting noise, float noiseThreshold}`; `EntityPlant{EntityPlantData plantType, NoiseSetting noise, float noiseThreshold, int gridSize, int plantPerGrid}`. (`Biom` references these via its `ecosystems` list.)

### EntityPlant : MonoBehaviour : SavableComponent<EntityPlant.Data>
**Purpose:** A GameObject-based procedural plant grown from a single seed into a recursive branch tree with fruit.
**Key members:** `Generate()` instantiates branch visuals from `ComponentData.rootBranch`; `GenerateBranch(Data.Branch, parent)` recursively instantiates `plantData.branchVisualPrefab`, join prefabs, and `fruitPrefab`; `GenerateSegments()` orders curved-branch sorting. Subscribes to `LevelChangeBuffer.CellsChanged`; if its supporting terrain cell is destroyed, detaches all fruit and self-destructs (`OnTerrainDestroyed`).
**`EntityPlant.Data`** (memento originator): holds `Branch rootBranch`, `EntityPlantData plantData`, `List<Fruit> fruits`. `Generate(Level, EntityData, EntityPlantData)` seeds RNG from `entityData.instanceId` and builds the tree via `TryGenerateBranch` (handles `Straight` vs `Curved`, length scaling by `lifeForce`, branching distribution, fruit probability, and self-intersection rejection). `FinalizeBranches`/`AdjustCurveSegmentLengthsRecursive` fix curve geometry. Nested classes: `Branch{id,position,direction,end,length,lifeForce,curveAngle,int[] variations, List<Branch> children}` (with length helpers), `Fruit{id,branchId}`, and `Memento{plantId, rootBranch, fruits}`.

### EntityPlantData : ScriptableObject : IIdentifiable<byte>
Tunables for entity plants (CreateAssetMenu `Punk/Level/Entity plant data`). Fields include `PlantType plantType` (enum `{Straight, Curved}`), `MinMaxInt seedDepth`, `MinMaxFloat branchAngle`, `float branchAngleNoise`, `IntDistribution branchLength`, `float lengthIncreaseWithLifeForce`, `MinMaxInt lifeForce`, `float fruitProbability`, `IntDistribution variationDistribution`, `float[] branchLengths`, `Sprite[] branchSprites`, `MinMaxFloat branchCurve`, `MinMaxInt branchingLifeForceMask`, `IntDistribution branchingDistribution`, prefab refs (`EntityPlant prefab`, `PlantBranchVisualBase branchVisualPrefab`, `GameObject joinPrefab`, `EntityPlantFruit fruitPrefab`), `float shakePropagationFactor`, `List<CellType> compatibleCells`.

### EntityPlantFruit : MonoBehaviour : IProjectileListener, IExplosionListener
Fruit on an entity plant. Reacts to projectile hits (`ProjectileCollided`) and explosions (`OnExplosion`) by shaking, propagating the shake up the branch chain (`Shake` → `ParentBranch.Shake`). `Detach()` either kills (`DetachBehaviour.Destroy`) or spawns a falling `Rigidbody2D` + `DragCellBehaviourTarget` (`Fall`). Raises `event Action<EntityPlantFruit> Killed` on death. Exposes `PlantBranchVisualBase ParentBranch`, `EntityPlant.Data.Fruit Fruit`, `SortingGroup SortingGroup`.

### EntityPlantGeneratorJob `[BurstCompile] struct : IJob` — BURST
Finds entity-plant seed positions. For each `PlantData` (per biome/plant), scans grid cells of size `girdSize`, picking the highest-noise cell that passes biome, depth (`minDepth/maxDepth`), compatible-cell and `noiseThreshold` checks, then records a `PlantSeed{position, plantId, direction=closest outline edge normal}` into `plantSeeds`. Inputs include `cells`, `biomes`, `depthMap`, outline `edges`, `NativeNoiseCollection noises`, `compatibleCellsPerPlant`.

### CurvedEntityPlantSegment : MonoBehaviour
Trivial holder exposing `SpriteRenderer SpriteRenderer` and `ObjectShaker ObjectShaker` for one segment of a curved branch.

### PlantGenerator (plain class)
**Purpose:** Orchestrates plant generation for a level. `GeneratePlants(LevelGenerationContext, seed)` → `GenerateEntityPlants` then `GenerateTileBasedPlants`.
- `GenerateEntityPlants` — gathers entity-plant noises from every biome ecosystem, schedules `EntityPlantGeneratorJob`, then per returned seed instantiates the plant prefab's `SavableEntity` as an `EntityData` and calls `EntityPlant.Data.Generate`.
- `GenerateTileBasedPlants` — packs `PlantType` data + spawn/grow/fruit cell maps + per-biome ecosystems into `PlantGeneratorJob`, schedules it, then spawns a fruit entity (drawn from `PlantType.fruits`) at every `fruitPositions` entry. Manages all `NativeArray`/`NativeParallelMultiHashMap` allocations.

### PlantGeneratorJob `[BurstCompile] struct : IJob` — BURST
**Purpose:** Grows the tile-based plant graph directly into `NativeArray<PlantCell> plantCells` (which is `level.plants`). `Execute` clears all cells then `TryPlaceSeed` over every cell. `GetDominantEcosystem`/`GetDominantPlant` pick a plant by noise + depth + spawn-cell compatibility; `lifeForce` is derived from how far noise exceeds the threshold. `Grow(...)` recursively extends the plant cell-by-cell, setting `ConnectionType.Entrance`/`Exit` on the 4 directions, branching at junctions (`junctionLifeForceMask`, `branchProbability`, `sideBranchLifeForce`, `sideBranchLengthRange`), and recording apex `fruitPositions`. Honors `growsUpwards/Downwards/Sideways` and `CanSeedReachSurface`.

### PlantCell (struct)
One tile-plant cell. Fields: `byte plantTypeIndex` (255 = none), `byte plantInstanceId` (0–254, used as a color key on the tile), `ConnectionType north/east/south/west`. **Enum:** `ConnectionType { Disconnected, Entrance, Exit }`. `GetConnectionType(int2 direction)` maps a direction to the matching side.

### PlantType : ScriptableObject : IIdentifiable<byte>
Tunables for tile-based plants (CreateAssetMenu `Punk/Level/Plant type`). Fields: `byte id`, `PlantTile tile`, `Color colorOnMap`, `MinMaxInt seedDepth`, `SavableEntityDistribution fruits`, `float fruitProbabilityOnApex`, `int maxLifeForce`, `MinMaxInt junctionLifeForceMask`, `MinMaxFloat sideBranchLifeForce`, `MinMaxInt sideBranchLengthRange`, `float branchProbability`, `bool growsUpwards/growsDownwards/growsSideways`, `List<CellType> cellsThatCanSpawnInto`/`cellsThatCanGrowInto`/`cellsThatCanContainFruit`, `ParticleSystem destroyParticle`.

### PlantTile : RuleTile<PlantTile.Neighbor>
Custom rule tile (CreateAssetMenu `Punk/Level/Plant tile`) that reads plant connection data from the `Level`. Adds neighbor rule constants `Null=3`/`NotNull=4`, overrides `GetTileData` to encode `plantInstanceId/254` into the tile color, and overrides `RuleMatch`/`RuleMatches` to test `PlantCell.ConnectionType` per direction (neighbor `1` = connected, `2` = disconnected). `SetLevel(Level)` must be called so it can query cells.

### PlantTilemapUpdater : TilemapUpdater
Subscribes to `Level.PlantDestroyed` and refreshes the affected tile. `Refresh(x,y)` sets the tilemap tile to `Level.GetPlantType(x,y).tile` (or null).

### PlantFruit : MonoBehaviour
Tile-plant fruit. Sways using a Perlin texture in `Update`; subscribes to `Level.PlantDestroyed` and `Detach()`es (destroy or fall, same pattern as `EntityPlantFruit`) when its containing cell is destroyed. Destroyed on hard impact (`OnCollisionEnter2D` vs `destroyImpactVelocity`).

### PlantDestructor : MonoBehaviour
Handles structural collapse of tile plants when terrain changes. On `LevelChangeBuffer.CellsChanged`, finds the affected plant's root (`GetRoot`, following `Entrance` connections), and if the root is now unsupported (`Level.IsEmpty`), recursively schedules every connected cell for destruction (`DestroyPlant` following `Exit` connections) with a staggered `destroySpreadDelay`, spawning `destroyParticle` and calling `Level.DestroyPlantCell`.

### PlantBranchVisualBase (abstract) / StraightPlantBranchVisual / CurvedPlantBranchVisual
`PlantBranchVisualBase` is the base for entity-plant branch visuals: holds `parent`, `children`, `branch`, `plantData`, `Fruit`, `ParentCount`; `Setup(...)`, `AddChild(...)`, and virtual `Shake(...)` which propagates a damped shake to parent, children and fruit.
- `StraightPlantBranchVisual` renders one straight branch with a GPU `ComputeBuffer` of segment variations (`_VariationsArray`, `_Length` shader props) and scales a `MeshRenderer` by branch length.
- `CurvedPlantBranchVisual` instantiates `CurvedEntityPlantSegment` prefabs along the branch curve (`GenerateSegments(sortingIndex)`), distributing per-segment shake.

### FastTravelManager : MonoBehaviour
**Purpose:** Teleporting ships between stations and to arbitrary entities/positions.
**Key members:**
- `TravelBetweenStations(Station originStation, Station.Data destinationData)` — closes the origin hatch, animates the connection pipe (`StationConnection.ShowTravelFrom/ShowTravelTo`), teleports via `TravelToEntity`, then plays the destination station's arrival sequence. Sets `destinationData.skipOpenAnim`/`isFastTravelDestination`.
- `TravelToEntity(...)` overloads (by `EntityData`, `int instanceId`, with optional `Vector2 offset` and `Action OnFade`).
- `TravelTo(Vector2, Action)` — fades, teleports all ships (split in co-op), snaps the ProCamera2D, fades back; toggles minion `isUnloadable` around the jump.
**Nested:** `FastTravelSequenceProperties` (delays, camera lock/unlock durations, zoom).

### Instrument : MonoBehaviour : SavableComponent<Instrument.Data>
**Purpose:** A one-use discoverable that reveals the nearest undiscovered POI of a configured type on the map.
**Key members:** `OnInteracted(Interactor)` opens an `InstrumentMenu`; `Discover(InstrumentDiscoverable, Ship)` finds, among all entities matching `discoverable.items` whose map icon is not yet visible, the best one (lowest `priority`, then nearest) and reveals it via `ShipMenuToggler.SetEntityDiscoveredByInstrument`, then plays wind/animation feedback and marks itself used. `Instrument.Data` (memento) tracks `bool isUsed` with `event Action Used` and `SetUsed()`.

### InstrumentDiscoverable : ScriptableObject
Config asset (`Punk/InstrumentDiscoverable`) listing `List<Item{SavableEntity entity, int priority}>`, plus `icon`/`displayName`. `HasUndiscoveredItem()` queries `EntityManager` + `MapIconManager` to check if any matching entity is still hidden.

### InstrumentMapIcon : MonoBehaviour
Swaps the map sprite to `usedIconSprite` once the bound `Instrument.Data` is used (via `MapIcon.TargetChanged` then `Instrument.Data.Used`).

### Biom : ScriptableObject : IIdentifiable<byte> (reference)
Biome asset (`Punk/Level/Biom`). Relevant to this category because it owns the content lists: `List<BiomEcosystem{Ecosystem ecosystem, NoiseSetting noise}> ecosystems` and `RoomSetupDistribution roomSetups`. Also `byte id`, map/debug colors, `backgroundCellType`, `randomObjectInRooms`, `subBioms`, `rasterizationStack`, `edgeSetting`. `GetDominantEcosystem(float2)` currently just returns the first ecosystem.

## Modding Notes

### General architecture for patchers
- Entity content is split into **data** (`EntityData` + `ComponentData` subclasses, e.g. `Station.Data`) and **view** (`SavableEntity` prefab + `SavableComponent<T>`). Patch the data layer to change persistent state; patch the MonoBehaviours to change behaviour/visuals.
- Most generation classes are resolved through a `ServiceLocator`. Useful runtime entry points: `ServiceLocator.Get<EntityManager>()` (then `GetEntitiesWithComponent<Station.Data>()`, `GetEntity(id)`, `GetAllEntities()`), `ServiceLocator.Get<PoIRegistry>()`, `ServiceLocator.Get<LevelGeneratorConfig>()`.

### More stations / POIs
- **Station placement** lives in `StationGenerator.PlaceBases` (probability driven by `LevelGeneratorConfig.stationDistanceAtCenter`/`stationDistanceAtEdge`) and `InitializeStations` (connection MST + `stationClearRadius`). Patch `PlaceBases` to change density/seeding, or just edit the `LevelGeneratorConfig` ScriptableObject fields (`stationDistanceAtCenter/Edge`, `stationClearRadius`, `stationPoI`). Note the hardcoded `poiId == 1 || poiId == 5` seed set in `PlaceBases`.
- **Regular POIs** are data-driven via `LevelGeneratorConfig.regularPois` (`PoIConfig`: `poi`, `maxAmount`, `canBePlacedOnMainPath`, `minDistanceFromCenter`, `minDistanceFromSamePoi`, `maxInterestLevel`, `biomeFilter`, `biomeFilterIsBlacklist`). Adding a new `PoI` asset + a `PoIConfig` entry is the no-code path. To change *placement logic*, patch `PoIGenerator.PoIGeneratorJob.CanPlace` / `PlacePoi` — **but this is a `[BurstCompile]` job** (see flag below).
- **What spawns inside a POI** is `PoI.entities` (a `GameObjectDistribution`) drawn in `EntityGenerator.SelectPrefabForPoi`/`PlaceObjects`. Patch `EntityGenerator.PlaceObjects` to inject/override entities, or edit the PoI's distribution and `EntitySelectionMode`.
- New station upgrades: add `StationUpgrade` entries to a `Station` prefab's `upgrades`; cost scaling is in `Station.CalculateUpgradeCost` (`PriceIncreaseMode`).

### Controlling entity spawns
- `EntityGenerator.PlaceEntities` / `PlaceObjects` are the central hooks; both are normal (non-Burst) instance methods on a managed class — good Harmony targets. `config.generateEntities`/`generateEnemies`/`generatePlants` gate the major phases.
- Per-room random props come from `RoomSetup.placedEntities` (`PlacedEntity{probability, entity}`). 
- For runtime spawning, call/patch `EntityGameObjectManager.CreateEntity(SavableEntity, Vector2)`. Streaming load/unload is `EntityGameObjectManager.InstantiateGameObjects` / `UnloadEntity` / `DestroyGameObjects`; entities with `isUnloadable == false` are never virtualized (useful for persistent custom content).
- To make a new prefab savable/spawnable it must have a `SavableEntity` with a unique `entityId` registered in the `SavablesCollection`, and components implementing `IComponentDataCreator` (typically via `SavableComponent<T>`).
- Saving hooks: implement `IEntitySavingListener.OnSaveEntity`; persistent state must be in a `ComponentData : IMementoOriginator` (mementos are gathered in `EntityData.CreateMemento`).

### Plant growth
- **Tile-based plants:** structure is grown in `PlantGeneratorJob.Grow` / `TryPlaceSeed` (**Burst**). The non-Burst orchestration `PlantGenerator.GenerateTileBasedPlants` is patchable and is where `PlantType`→job data packing happens. Tune growth via `PlantType` assets: `maxLifeForce`, `junctionLifeForceMask`, `branchProbability`, `sideBranchLifeForce`, `sideBranchLengthRange`, `growsUpwards/Downwards/Sideways`, `seedDepth`, `cellsThatCanSpawnInto/GrowInto/ContainFruit`, `fruitProbabilityOnApex`, `fruits`. Destruction cascade tuning: `PlantDestructor.destroySpreadDelay` and `PlantType.destroyParticle`.
- **Entity plants:** branch tree is built in `EntityPlant.Data.Generate` / `TryGenerateBranch` (managed code — directly Harmony-patchable). Tune via `EntityPlantData`: `lifeForce`, `branchLength`, `lengthIncreaseWithLifeForce`, `branchAngle`/`branchAngleNoise`, `branchCurve`, `branchingLifeForceMask`, `branchingDistribution`, `fruitProbability`, `branchLengths`/`branchSprites`, `compatibleCells`, `seedDepth`. Seed *placement* is `EntityPlantGeneratorJob` (**Burst**); the spawn loop in `PlantGenerator.GenerateEntityPlants` is managed.
- Per-biome plant selection comes from `Ecosystem.plants` / `Ecosystem.entityPlants` with `NoiseSetting` + `noiseThreshold` (and `gridSize`/`plantPerGrid` for entity plants). Add entries there (referenced from `Biom.ecosystems`) to introduce flora without code.
- Plant shaking/physics (`EntityPlantFruit.Shake`, `PlantBranchVisualBase.Shake`, `PlantFruit`) are all managed MonoBehaviours, easy to patch for custom interaction effects.

### Burst job flag (IMPORTANT)
The following are `[BurstCompile] IJob` structs — they are compiled to native code at build time, so **Harmony cannot patch their `Execute`/helper methods reliably** (Harmony patches managed IL; Burst-compiled jobs run native code). To alter their behaviour, instead patch the **managed scheduling method** that fills their inputs, or replace the inputs:
- `PoIGenerator.PoIGeneratorJob` — patch `PoIGenerator.PlacePoIs`.
- `PlantGeneratorJob` — patch `PlantGenerator.GenerateTileBasedPlants`.
- `EntityPlantGeneratorJob` — patch `PlantGenerator.GenerateEntityPlants`.
(`StationDistressSignalUpdater` uses a `ComputeBuffer`/`NativeArray` but its logic is normal managed code and is patchable.)

### Latent bug worth knowing
`StationDistressSignalUpdater.OnDisable` calls `Delegate.Combine` (not `Remove`) for `GameController.LevelGenerated`, so it double-subscribes across level regenerations. If you mod level reloads, account for this.
