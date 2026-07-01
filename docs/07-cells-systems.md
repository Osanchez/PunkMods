# Cells & Cellular Simulation
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK's destructible terrain is a dense grid of **cells**. The authoritative grid state does **not** live in `Cell` objects — it lives in a set of parallel `NativeArray`/`NativeHashMap` buffers owned by the `Level` service (see the *Terrain & Level* docs). The `Cell` class is a lightweight per-position record; the simulation works directly on the flat native buffers, indexed as `index = y * Width + x`.

Key `Level` storage that the cell systems read and write (declared in `Level.cs`):

| Buffer | Type | Meaning |
| --- | --- | --- |
| `cellTypes` | `NativeArray<byte>` | the `CellType.id` at each grid index (0 = `CellType.Empty`) |
| `burnLevels` | `NativeHashMap<int, float>` | accumulated heat per index (sparse — only burning cells present) |
| `foreGroundCellTypes` / `backGroundCellTypes` | `NativeArray<byte>` | decorative layers |
| `luminocity`, `fogLevels`, `bioms`, `heightMap`, `plants` | `NativeArray<...>` | other per-cell layers |
| `containingMergedCellRelativePosition` | `NativeArray<HalvedByte>` | which merged-cell (if any) owns this index |
| `obstackles` | `NativeHashSet<int2>` | manual navmesh blockers |

A `CellType` is a `ScriptableObject` (Odin `SerializedScriptableObject`) identified by a `byte id`. It carries **everything** designers tune for that material: collision, contact damage, fire constants (`fireThreshold`, `burnDuration`, `heatTransmission`, `fireSpreadVariance`, `fireOverheatSpreadInfluence`, `burntVariant`), shake settings, sprites, merged-cell distribution, drop table, and a `List<CellBehaviour>` of pluggable per-material behaviours.

Mutation flows through `Level.SetCell(...)` / `Level.DestroyCell(...)`, which fire the `Level.CellChanged` event with a `CellChange` struct that includes a `changeSource` int. Sub-systems use sentinel `changeSource` constants to avoid reacting to their own writes (e.g. burning = `15324`, auto-pop = `1324`).

The sub-systems, grouped by how they update cells:

- **Burning** — `CellBurningManager` (MonoBehaviour) schedules a Burst `BurningUpdateJob` each tick that diffuses heat across `burnLevels`; cells whose burn time exceeds `burnDuration` are converted to `burntVariant` or destroyed. `BurnCellBehaviour` + `BurnCellBehaviourTarget` apply burn *to units* standing on burning material.
- **Electricity** — `ElectricityManager` owns two `ElectricitySubSystem`s (Player/Enemy). Each schedules a Burst `ConductorConnectionJob` to build a graph of beams between `ElectricityConductor`s, then deals damage to `IElectricityListener`s. This is conductor/object based, **not** grid based.
- **Drag / Damage** — `DragCellBehaviour` and `DamageCellBehaviour` are `CellBehaviour`s read by per-unit `*Target` MonoBehaviours via a `ContainingCellPoller`; they slow or hurt whatever stands in that cell type.
- **Regrow** — `CellRegrower` (MonoBehaviour) listens to `CellChanged`, and after a delay re-grows destroyed cells that carry a `CellRegrowBehaviour`, provided a matching neighbour exists.
- **Auto-pop** — `AutoPopper` chain-destroys neighbouring cells of types listed in an `AutoPopNeighbour` behaviour when a cell is destroyed.
- **Merging** — `MergedCellsGenerator` (generation-time) stamps multi-tile `MergedCellData` sprites onto contiguous same-type regions for visual variety.

## Class Index

| Class | Kind | Role |
| --- | --- | --- |
| `Cell` | plain class | Per-position record (height, luminosity, type layers, position). Not the live grid. |
| `CellType` | `SerializedScriptableObject` (`IDamagable`, `IIdentifiable<byte>`) | Material definition + all tunable constants + behaviour list. |
| `CellCollision` | struct | Payload describing a collision between a body and a cell. |
| `ICellCollisionListener` | interface | `OnCellCollision(CellCollision)`. |
| `CellBehaviour` | abstract class | Base for pluggable per-`CellType` behaviours. |
| `BurnCellBehaviour` | `CellBehaviour` | `burnPerSecond` applied to units in this cell. |
| `DamageCellBehaviour` | `CellBehaviour` | `Damage` + `delay` applied to units in this cell. |
| `DragCellBehaviour` | `CellBehaviour` | velocity `drag` multiplier for bodies in this cell. |
| `CellRegrowBehaviour` | `CellBehaviour` | regrow `delay`, `spread`, animation. |
| `AutoPopNeighbour` | `CellBehaviour` | chain-pop probability + neighbour type list. |
| `BurnCellBehaviourTarget` | MonoBehaviour | Per-unit consumer of `BurnCellBehaviour`. |
| `DamageCellBehaviourTarget` | MonoBehaviour | Per-unit consumer of `DamageCellBehaviour`. |
| `DragCellBehaviourTarget` | MonoBehaviour | Per-rigidbody consumer of `DragCellBehaviour`. |
| `ContainingCellPoller` | MonoBehaviour | Tracks which cell a transform is in; raises change events. |
| `CellBurningManager` | MonoBehaviour | Owns burn native maps, ticks `BurningUpdateJob`, converts burnt cells. |
| `BurningUpdateJob` | **Burst `IJob`** | Heat diffusion over `burnLevels`. **Not Harmony-patchable.** |
| `CellRegrower` | MonoBehaviour | Re-grows destroyed regrowable cells. |
| `CellRegrowJob` | MonoBehaviour (empty stub) | Decompiled as empty; no logic. |
| `AutoPopper` | MonoBehaviour | Chain-destroys neighbour cells on destruction. |
| `MergedCellData` | `ScriptableObject` (`IIdentifiable<byte>`) | A multi-tile merged sprite definition. |
| `MergedCellDataDistribution` / `...Item` | serializable weighted distribution | Per-`CellType` pool of merged cells. |
| `MergedCellsGenerator` | `IInitializable` | Generation-time merged-cell placement. |
| `MergedCellsRegistry` | `ScriptableObjectRegistry<MergedCellData, byte>` | Lookup of merged-cell SOs. |
| `ElectricityManager` | MonoBehaviour (`IDisposable`) | Owns Player/Enemy electricity sub-systems. |
| `ElectricitySubSystem` | plain class (`IDisposable`) | Builds beam graph, deals damage, manages charge. |
| `ConductorConnectionJob` | **Burst `IJob`** | Graph/grouping of conductors. **Not Harmony-patchable.** |
| `ElectricityConductor` | MonoBehaviour | A node: source/relay with conductivity, chain length, damage. |
| `ElectricityBeam` | MonoBehaviour | One beam between two conductors; raycasts + damages. |
| `ElectricityBeamVusual` | MonoBehaviour | Procedural lightning line-renderer (note: typo `Vusual`). |
| `IElectricityListener` | interface | `OnHitByElectricity(ElectricityConductor)`. |
| `DischargeData` | struct | Discharge payload (damage, chain length, subsystem, mask). |
| `AddDischargeEffect` | `WeaponAugmentation` | Weapon mod adding/upgrading discharge on projectiles. |
| `OnScreenCellsTracker` | `IInitializable` | Computes visible cell rect for culling. |
| `CellInfoManager` | MonoBehaviour | Debug overlay (cell type / burn level text). |
| `CellDebugInfo` | MonoBehaviour | TMP text label for one debug cell. |
| `CellsGraphRule` | `GridGraphRule` (A* Pathfinding) | Marks walkable cells / applies navmesh tags. |
| `CellConvertData` | serializable class | Area cell-conversion descriptor (radius + type swap). |
| `CellAnimationClip` | struct (`ICellAnimation`) | Scale/translate tile animation via curves. |
| `CellShakeAnimation` | class (`ICellAnimation`) | Damped sinusoidal tile shake. |
| `ICellAnimation` | interface | `Duration` + `GetTransformMatrix(t)`. |

## Classes

### Cell: plain class
- **Purpose:** A lightweight per-grid-position record. The *authoritative* live grid is the native buffers on `Level`; `Cell` instances are used for localized/per-position bookkeeping.
- **Key fields:** `static int ForegroundRadius = 3`; `float height`; `int luminosity`; `CellType cellType, backgroundType, foregroundType`; `int variant`; `Vector2Int position, positionInSegment`.
- **Events:** `Action<Vector2Int, CellType> Destroyed`; `Action<Vector2Int> ForegroundDestroyed`.
- **Ctor:** `Cell(Vector2Int position)` computes `positionInSegment = position % Level.SegmentSize` (`SegmentSize = 25`).

### CellType: SerializedScriptableObject; implements IDamagable, IIdentifiable<byte>
- **Purpose:** The material definition. Every tunable property of a terrain material lives here. Created via `CreateAssetMenu "Punk/Level/Cell type"`.
- **Identity / map:** `byte id` (`const byte Empty = 0`), `colorOnMap`, `backgroundTint`, `mapTexture`, `minimapTexture`, `showAsEmptyOnMinimap`.
- **Collision / damage:** `DamageConditions damageConditions`; `Damage contactDamage`; `float contactPushbackForce`; `ColliderType colliderType` (enum `NonTrigger/Trigger/None`); `bool blocksEnemyPlacement` (default true); `bool isWalkable`; `bool ignoredByDepthMap`; `int navmeshTagId`.
- **Fire constants:** `float fireThreshold` (heat needed to ignite; `<= 0` means non-flammable), `float burnDuration` (time on fire before consumed), `float heatTransmission` (how readily it accepts heat from neighbours), `float fireSpreadVariance` (Perlin-noise jitter on spread), `float fireOverheatSpreadInfluence` (extra spread from overheating), `CellType burntVariant` (what it becomes when burnt; null = destroyed).
- **Visual / merge:** `Material tileMaterial`; `AnimationCurve tileBrightnessCurve`; `SpriteDistribution sprites`; `TileDistribution tiles`; `bool randomizeRotation/randomizeMirror`; `float mergedCellProbability`; `MergedCellDataDistribution mergedCells`; `ShakeSettings shakeSettings`; particles `impactParticle`/`destroyParticle`; `string destroySfx`, `hudAnimParamName`.
- **Behaviours:** `List<CellBehaviour> cellBehaviours` — the pluggable per-material behaviour list (burn/damage/drag/regrow/auto-pop).
- **Drops:** `DropTable dropTable`.
- **Nested:** `struct ShakeSettings { float stiffness, offsetLimit, propagationDamping, propagationDelay, frequency, shakeDuration; bool randomizeDirection; }`; `enum ColliderType { NonTrigger, Trigger, None }`.
- **Key methods:** `byte Id => id`; `float GetDamageAmount(Damage damage)` returns `damage.amount` only if `damageConditions.Validate(damage)`.

### CellCollision: struct
- **Fields:** `Collision2D collision`; `Vector2Int segmentPosition`; `LevelSegmentComponent levelSegmentComponent`; `Vector2Int cellPositionWorld`; `CellType cellType`.
- **Relationships:** Passed to `ICellCollisionListener.OnCellCollision`.

### ICellCollisionListener: interface
- `void OnCellCollision(CellCollision cellCollision)`.

### CellBehaviour: abstract class
- **Purpose:** Empty abstract base. Concrete behaviours are data containers attached to a `CellType.cellBehaviours`. Consumers find them with `cellType.cellBehaviours[i] is XxxCellBehaviour`. Because they are plain serialized C# objects (not Burst), their fields **are** mod-friendly.

### BurnCellBehaviour: CellBehaviour
- **Field:** `float burnPerSecond` — burn rate applied to a unit standing on this material.

### DamageCellBehaviour: CellBehaviour
- **Fields:** `Damage damage`; `float delay` (seconds between ticks).

### DragCellBehaviour: CellBehaviour
- **Field:** `float drag` — fraction of velocity removed each `FixedUpdate` (`velocity *= 1 - drag`).

### CellRegrowBehaviour: CellBehaviour
- **Fields:** `MinMaxFloat delay`, `MinMaxFloat spread`, `CellAnimationClip animationClip`.
- **Method:** `OnCellDestroyed(Level, Level.CellChange)` — present but **empty** in the decompile; the real logic lives in `CellRegrower`.

### AutoPopNeighbour: CellBehaviour
- **Fields:** `float probability`, `float probabilityDecrease` (per chain generation), `List<CellType> cellTypesToPop`, `MinMaxFloat delay`.

### BurnCellBehaviourTarget: MonoBehaviour
- **Purpose:** Makes a `Unit` accumulate burn while standing on a `BurnCellBehaviour` cell.
- **Fields:** `Unit unit`; `ContainingCellPoller cellPoller`; `ParticleSystem particleSystem`; `string sfx`; runtime `currentBurnCellBehaviour`, `isBurning`, `audioHandle`.
- **Flow:** subscribes to `cellPoller.ContainingCellTypeChanged`; on cell change finds the first `BurnCellBehaviour` on the new type. Each `Update`, if burning, `unit.ComponentData.BurnLevel += burnPerSecond * Time.deltaTime` and plays particles/SFX.

### DamageCellBehaviourTarget: MonoBehaviour
- **Purpose:** Periodically damages a `HealthBase` while in a `DamageCellBehaviour` cell.
- **Fields:** `HealthBase health`; `ContainingCellPoller cellPoller`; runtime `currentDamageCellBehaviour`, `lastDamageTime`.
- **Flow:** every `Update`, if `Time.time - lastDamageTime > delay`, `health.TakeDamage(damage)`.

### DragCellBehaviourTarget: MonoBehaviour
- **`[RequireComponent(typeof(ContainingCellPoller))]`**
- **Fields:** `Rigidbody2D rigidbody`; `ContainingCellPoller cellPoller`.
- **Flow:** every `FixedUpdate`, if in a drag cell, `rigidbody.linearVelocity *= 1 - drag`.

### ContainingCellPoller: MonoBehaviour
- **Purpose:** The shared bridge that all `*Target` behaviours use. Tracks which cell a transform occupies and fires events on change. Disables itself if no `Level` service exists.
- **Events:** `Action<CellType, CellType> ContainingCellTypeChanged`; `Action<Vector2Int, Vector2Int> CellPositionChanged`.
- **Properties:** `Vector2Int CellPosition` (setter validates with `level.ContainsCell` and fires events); `CellType ContainingCellType`.
- **Flow:** `Update` sets `CellPosition = RoundToInt(transform.position)`; `OnDisable` clears `ContainingCellType` to null.

### CellBurningManager: MonoBehaviour
- **Purpose:** Owner/driver of fire simulation. Builds per-`CellType` native lookup maps at `Awake`, ticks the Burst `BurningUpdateJob`, then resolves burnt cells on the main thread.
- **Constants:** `const int CHANGE_SOURCE_BURN = 15324` (used as `changeSource` so regrow/auto-pop ignore burn-driven changes).
- **Fields:** `float tickRate`; native maps keyed by `byte` cell id: `cellTypeBurnThresholds`, `materialBurnDurations`, `materialSpreadVariances`, `materialOverheatSpreadInfluences`, `materialHeatTransmissions`; `NativeHashMap<int,float> timeSpentBurning`; `NativeHashSet<int2> burntPositions`; `NativeList<int2> directions` (8 neighbours despite being allocated for 4).
- **Event:** `event Action BurnLevelsUpdated`.
- **Flow:** `Awake` populates the native maps from the `IRegistry<CellType, byte>`. `Update` calls `Tick` once `level.generationFinished`. `Tick` schedules `BurningUpdateJob` over `level.burnLevels`/`level.cellTypes`, `.Complete()`s it, then for each `burntPositions` entry converts to `burntVariant` (or `DestroyCell`) with `changeSource = 15324` and resets burn level. `IncreaseBurnLevel(position, delta)` is the public ignition entry point. `OnDestroy` disposes all native collections.

### BurningUpdateJob: struct — **`[BurstCompile] IJob`**
- **Purpose:** Heat diffusion. For every burning cell, pushes heat into its 8 neighbours scaled by the neighbour's `heatTransmission`, the source's overheat (`(burnLevel - threshold) * overheatSpreadInfluence`), a diagonal factor `0.7071`, and Perlin-noise variance; accumulates `timeSpentBurning`; flags cells past `burnDuration` into `burntPositions`.
- **Buffers:** `[ReadOnly] NativeArray<byte> blocks` (= `level.cellTypes`); `[ReadOnly]` the five per-material `NativeHashMap<byte,float>` maps + `NativeList<int2> directions`; read/write `NativeHashMap<int,float> burnLevels`, `timeSpentBurning`, `NativeHashSet<int2> burntPositions`; scalars `width, height, deltaTime`.
- **Helpers:** `IsEmpty`, `IsOnFire`, `GetBurnLevel/SetBurnLevel`, `IncreaseBurningTime`, `GetCellId`, `IsBurnt` — all indexing `y*width+x`.
- **MODDING: this is Burst-compiled native code; its `Execute` body cannot be Harmony-patched at the IL level.** Tune fire via `CellType` fields instead (see Modding Notes).

### CellRegrower: MonoBehaviour
- **Purpose:** Re-grows destroyed cells whose `CellType` has a `CellRegrowBehaviour`.
- **Fields:** `TilemapAnimator tilemapAnimator`; `LevelElementsCollection`; `Level`; `Dictionary<CellType, CellRegrowBehaviour> regrowBehaviours` (built at `Awake` from all cell types); `List<CellRegrowData> regrowingCells`.
- **Nested:** `struct CellRegrowData { byte cellType; Vector2Int position; float spread; float regrowTime; CellAnimationClip animation; }`.
- **Flow:** subscribes to `Level.CellChanged`. On a destruction *not* caused by burning (`changeSource != 15324`) where the previous type is regrowable, queues an entry with `regrowTime = Time.time + delay.RandomInRange()`. Each `Update`, if a matching neighbour exists and `regrowTime` passed, calls `Regrow` (`level.SetCell(index, cellType)`) and plays a slide-in `CellAnimationClip` via `tilemapAnimator`; otherwise pushes `regrowTime` out by `spread`.

### CellRegrowJob: MonoBehaviour
- **Purpose:** Empty stub in the decompile (`Start`/`Update` empty). Despite the name it is **not** a Burst job. No behaviour to mod.

### AutoPopper: MonoBehaviour
- **Purpose:** Chain-destruction. When a cell with `AutoPopNeighbour` is destroyed, neighbouring matching cells "pop" after a delay, cascading outward with decreasing probability.
- **Constant:** `const int CHANGE_SOURCE_AUTO_POP = 1324`.
- **Fields:** `Dictionary<CellType, AutoPopNeighbour> autoPopBehaviours`; `List<AutoPopData> cellsToPop`.
- **Nested:** `struct AutoPopData { Vector2Int position; float popTime; int generation; }`.
- **Flow:** on `CellChanged` (a true destruction, `changeSource` not 1324 or 15324) registers the 4 orthogonal neighbours. `Update` pops queued cells whose `popTime` elapsed via `level.DestroyCell(x, y, 1324)`, cascading to *their* neighbours at `generation + 1`. `RegisterToPopIfNeeded` rolls `Random < probability - probabilityDecrease * generation` and checks `cellTypesToPop`.

### MergedCellData: ScriptableObject; implements IIdentifiable<byte>
- **Purpose:** A multi-tile decorative sprite spanning several cells. `CreateAssetMenu "Punk/Level/Merged cells data"`.
- **Fields:** `static int MaxSize = 3`; `byte id`; `bool randomRotation, mirrorX, mirrorY`; `Sprite sprite1..sprite9`; `int width, height`; `List<TileBase> tiles`.
- **Method:** `TileBase GetTile(int x, int y)` → `tiles[y*width + x]`.

### MergedCellDataDistribution / MergedCellDataDistributionItem
- `MergedCellDataDistribution : Distribution<MergedCellData, MergedCellDataDistributionItem>` and `MergedCellDataDistributionItem : DistributionItem<MergedCellData>` — a weighted pool (`WeightedDistribution`). Referenced from `CellType.mergedCells`.

### MergedCellsGenerator: IInitializable
- **Purpose:** Generation-time placement of merged cells over contiguous same-type regions.
- **Fields:** `MergedCellsRegistry mergedCellsRegistry`; nested `MergedCellDistribution possibleMergedCells`; `byte[] possibleRotations = {0,1,2,3}`.
- **Flow:** `Generate(level)` iterates the whole grid → `TryPlaceMergedCell`: skips empty cells, rolls against `cellType.mergedCellProbability`, enumerates the type's `mergedCells` items (each rotation/mirror variant), validates fit with `CanPlace` (all covered cells exist, same `cellTypeId`, none already part of a merged cell), then draws weighted and calls `level.PlaceMergedCell`.

### MergedCellsRegistry: ScriptableObjectRegistry<MergedCellData, byte>
- Asset registry of `MergedCellData`. `CreateAssetMenu "Punk/Level/Merged cells registry"`.

### ElectricityManager: MonoBehaviour; implements IDisposable
- **Purpose:** Top-level electricity coordinator. Splits the world into a Player and an Enemy sub-system so the two factions' arcs don't damage each other.
- **Nested:** `[Flags] enum SubSystemType { None = 0, Player = 1, Enemy = 2 }`.
- **Fields:** `ElectricityBeam _beamPrefab, _enemyBeamPrefab`; `ParticleSystem _particleSystemPrefab, _enemyParticleSystemPrefab`; `float beamRange`; `bool logChanges`; `ElectricitySubSystem playerSubsystem, enemySubsystem`.
- **Methods:** `Register/Unregister(ElectricityConductor)` (forwards to both sub-systems); `SpawnDischarge(DischargeData, Vector2)` spawns a temporary `ElectricityConductor` GameObject that self-destroys after 0.25s; `Update` calls `Recalculate()` on both sub-systems each frame.

### ElectricitySubSystem: plain class; implements IDisposable
- **Purpose:** Per-faction beam graph. Maintains the conductor list, schedules `ConductorConnectionJob`, instantiates/destroys `ElectricityBeam`s for added/removed connections, and deals grouped damage with optional per-source charge depletion.
- **Native fields:** `NativeList<ConductorConnectionJob.Node> nodes`; `NativeList<Connection> connections, previousConnections`; `NativeHashSet<int2> removed`; `NativeHashSet<Connection> added`.
- **Managed fields:** `List<ElectricityConductor> _conductors`; `Dictionary<int2, ElectricityBeam> _beams`; index recycling `Queue<int> recycledIndices`; damage grouping `sourceConductorCountPerGroup`, `dealtDamagePerGroup`; `beamsToDestroyNextFrame`.
- **Flow:** `Register` adds conductors whose `ConductedSystem` includes this subsystem; `Recalculate` rebuilds `nodes` from conductor transforms, schedules+completes the job, destroys removed beams, instantiates added beams (`beam.Setup(source, c1, c2)`), emits particles, then `DealDamages`. `DealDamages` overlaps each conductor's `DamageRadius`, calls `conductor.TryDamage(listener)`, sums damage per group, lets each beam deal its own damage, and subtracts charge from charge-limited sources (destroying them at `Charge <= 0`).

### ConductorConnectionJob: struct — **`[BurstCompile] IJob`**
- **Purpose:** Builds the conductor connection graph and assigns `groupId`s. From each source node it recursively connects to in-range conductors (`ChainLenght` hops), respecting `minConductivity`, max `range`/`rangeSqr`, no duplicate/crossing connections (`Intersection.Intersect`), and diffs against `previousConnections` to fill `added`/`removed`.
- **Nested `Node`:** `float2 position; int chainLength, conductivity, minConductivity; float damage; int groupId; bool IsSource => chainLength > 0;` (+ three ctors).
- **Nested `Connection : IEquatable<Connection>`:** `int nodeIndex1, nodeIndex2; float damage; int sourceNodeIndex; int2 ToInt2();`.
- **Constants:** `NO_GROUP = -1`; `static float2 MissingConductor = (float.MinValue, float.MinValue)`.
- **MODDING: Burst-compiled; `Execute` cannot be Harmony-patched. Tune via `ElectricityConductor` / `ElectricityManager` serialized fields.**

### ElectricityConductor: MonoBehaviour
- **Purpose:** A graph node — either a source that emits beams or a relay that conducts them. Registers with `ElectricityManager` on enable.
- **Serialized fields:** `bool isSource`; `SubSystemType emittedSystem, conductedSystem`; `int chainLength, minConductivity, conductivity`; `Damage damage`; `LayerMask layerMask`; `bool showPreviewBeam, showBeamParticles, limitedCharge`; `float maxCharge, damageRadius (0.5), damageRepeatDelay`.
- **Runtime:** `float Charge` (set from `maxCharge`); `Dictionary<IElectricityListener,float> lastDamageTimes`.
- **Methods:** `Setup(ProjectileElectricityData)` and `Setup(DischargeData)` configure a conductor at spawn (projectile-driven arcs / discharges); `SetConductedSystem(...)`; `bool TryDamage(IElectricityListener)` gated by `damageRepeatDelay`.

### ElectricityBeam: MonoBehaviour
- **Purpose:** A single arc between two conductors. Manages preview→live transition, periodic visual regeneration, SFX position, and damage along its length.
- **Fields (serialized):** `float previewDuration`; `MinMaxFloat regenerateDelay`; `ElectricityBeamVusual[] _beamVisuals`; `ElectricityBeamVusual previewVisual`; `AnimationCurve mainVisualWidthByDamage`; `string sfx`.
- **Props:** `Damage`, `Conductor1`, `Conductor2`, `IsPreview` (+ private source/listeners/positions).
- **Methods:** `Setup(source, c1, c2)`; `DealDamages()` damages the two endpoint listeners plus anything hit by a `Physics2D.CircleCast` (radius 0.5) along the beam, each gated by `SourceConductor.DamageRepeatDelay`; `RegisterLastDamageTime(listener)`.

### ElectricityBeamVusual: MonoBehaviour  *(note: name is misspelled "Vusual" in the binary)*
- **Purpose:** Procedural lightning rendering with a `LineRenderer` — jittered nodes that drift and regenerate.
- **Fields:** `float maxNodeDistance`; `MinMaxFloat nodeSpacingNoise, regenerateDelay, nodeSpeed, nodeDistanceFromCenter`; `LineRenderer _lineRenderer`; `Gradient colorDistribution`; node position/velocity arrays.
- **Methods:** `SetLineWidth(float)`; `UpdatePositions(start, end, distance, signedAngle)`; `Regenerate(distance)`.

### IElectricityListener: interface
- `void OnHitByElectricity(ElectricityConductor conductor)` — implement on anything electricity should damage/affect.

### DischargeData: struct (`[Serializable]`)
- **Fields:** `Damage damage`; `int chainLength`; `SubSystemType subSystem`; `LayerMask layerMask`. Consumed by `ElectricityManager.SpawnDischarge` / `ElectricityConductor.Setup`.

### AddDischargeEffect: WeaponAugmentation; implements IProjectileModifier, IHasPerProjectileCost, IHasDescriptionForWeapon
- **Purpose:** Weapon mod that grants/upgrades a projectile's discharge (chain lightning on impact/timeout).
- **Fields:** `int chainLengthIncrement`; `FloatSeries damageIncrement`; `bool impact, timeout`; `int costPerProjectile`; `Resource costResource`.
- **Key:** `ModifyProjectile(IProjectile)` adds to `projectile.DischargeData` (chain length, damage) and optionally enables `ImpactBehaviour.discharge` / `LifetimeData.discharge`.

### OnScreenCellsTracker: IInitializable
- **Purpose:** Computes the rect of grid cells currently on screen (used for culling/streaming cell visuals). Caches per-frame.
- **Fields:** `Camera mainCamera`; `Level level`; `float fieldOfViewTangent`; `RectInt currentRect`.
- **Methods:** `RectInt.PositionEnumerator AllPositionsOnScreen(int margin = 0)`; `RectInt GetRectWithMargin(int margin)`; private `UpdateRectIfNeeded()` recomputes once per frame from camera FOV/aspect/z.

### CellInfoManager: MonoBehaviour
- **Purpose:** Debug overlay spawning a `CellDebugInfo` label per visible non-empty cell, showing either cell type id or burn level.
- **Nested:** `enum ValueToDebug { CellType, BurnLevel }`.
- **Flow:** subscribes to `Level.CellChanged`, tilemap visibility events, and `CellBurningManager.BurnLevelsUpdated`. `GetBurnText` colours the number orange once `burnLevel >= fireThreshold`.

### CellDebugInfo: MonoBehaviour
- One TMP label: `void Display(string label)`.

### CellsGraphRule: GridGraphRule  (A* Pathfinding For Unity)
- **Purpose:** Bridges the cell grid to the A* navmesh. Registers a `BeforeConnections` main-thread pass that walks all graph nodes and ANDs `walkable` with: cell exists AND (empty OR `cellType.isWalkable`) AND not in `level.obstackles`; also copies `cellType.navmeshTagId` into node tags.
- **`[Preserve]`** — kept from stripping; pure managed (Harmony-patchable).

### CellConvertData: serializable class
- **Fields:** `bool enabled`; `float radius`; `List<CellType> convertableCells`; `CellType resultCellType`. Used (e.g. by `Level.ConvertCells`-style logic) to swap a radius of one set of cell types to another.

### CellAnimationClip: struct; implements ICellAnimation
- **Purpose:** Curve-driven tile animation (scale and/or translate) used by regrow/tilemap animator.
- **Fields:** `float duration`; `bool scale`; `AnimationCurve scaleXCurve, scaleYCurve`; `bool translate`; `AnimationCurve translationCurve`; private `fromPosition/toPosition`.
- **Methods:** `float Duration`; `void Move(Vector3 from, Vector3 to)`; `Matrix4x4 GetTransformMatrix(float t)`.

### CellShakeAnimation: class; implements ICellAnimation
- **Purpose:** Damped sinusoidal tile shake (used when cells are shot, per `CellType.ShakeSettings`).
- **Fields:** `float frequency, amplitude, duration; float2 direction; float delay`.
- **Ctor:** `CellShakeAnimation(float2 direction, float frequency, float amplitude, float duration, float delay = 0)`.
- **Method:** `Matrix4x4 GetTransformMatrix(float t)` — cosine oscillation along `direction`, amplitude decaying to 0 over `duration`, after `delay`.

### ICellAnimation: interface
- `float Duration { get; }`; `Matrix4x4 GetTransformMatrix(float t)`.

## Modding Notes

### What is freely tweakable (managed data — best targets)
Almost all cell tuning is **data on ScriptableObjects**, which is the cleanest thing to mod (edit the asset, or set the field via reflection/Harmony at load):

- **Destructibility / collision:** `CellType.colliderType`, `isWalkable`, `blocksEnemyPlacement`, `contactDamage`, `contactPushbackForce`, `damageConditions`, `dropTable`, `destroyParticle`/`destroySfx`. `CellType.GetDamageAmount(Damage)` is a normal managed method — Harmony-patchable to change how incoming damage is computed.
- **Fire spread / burning:** all driven by `CellType` floats — `fireThreshold` (ignition point; set `<= 0` to make a material fireproof), `burnDuration` (how long before consumed), `heatTransmission` (how fast it catches), `fireSpreadVariance`, `fireOverheatSpreadInfluence`, and `burntVariant` (null = destroyed, else transmutes). The `CellBurningManager` copies these into its native maps at `Awake`, so **change them before the manager's `Awake` runs** (e.g. patch the registry / SO load), not after.
- **Per-unit hazards:** `BurnCellBehaviour.burnPerSecond`, `DamageCellBehaviour.damage`/`delay`, `DragCellBehaviour.drag` — plain serialized fields on `CellBehaviour` subclasses inside `CellType.cellBehaviours`.
- **Regrow:** `CellRegrowBehaviour.delay` / `spread` / `animationClip`. **Note** the per-`CellType` `OnCellDestroyed` hook is empty; the live logic is `CellRegrower.OnCellChanged`/`Update` — Harmony-patch `CellRegrower` to change regrow rules.
- **Auto-pop chains:** `AutoPopNeighbour.probability`, `probabilityDecrease`, `cellTypesToPop`, `delay`. Cascade logic is in `AutoPopper` (managed, patchable).
- **Merged cells:** `CellType.mergedCellProbability` + `CellType.mergedCells` distribution; placement in `MergedCellsGenerator` (managed, patchable).
- **Electricity:** every meaningful value is a serialized field on `ElectricityConductor` (`chainLength`, `conductivity`, `minConductivity`, `damage`, `damageRadius`, `damageRepeatDelay`, `limitedCharge`/`maxCharge`, `layerMask`, `emittedSystem`/`conductedSystem`) or on `ElectricityManager` (`beamRange`, beam prefabs). `ElectricitySubSystem.DealDamages`, `ElectricityBeam.DealDamages`/`CanDamage`, and `ElectricityConductor.TryDamage` are all managed and Harmony-patchable.

### Useful Harmony targets (managed methods)
- `Level.SetCell(int, byte, int)` / `Level.DestroyCell(int, int)` — the choke point for **all** grid mutation; patch here to intercept/veto/log any cell change. Watch the `changeSource` sentinels: `CellBurningManager.CHANGE_SOURCE_BURN = 15324`, `AutoPopper.CHANGE_SOURCE_AUTO_POP = 1324` (sub-systems use these to ignore their own writes — reuse or add your own to stay compatible).
- `CellBurningManager.Tick` / `IncreaseBurnLevel` — main-thread fire resolution and ignition entry point (note `Tick` *schedules* the Burst job, then resolves burnt cells in managed code — patch the resolution, not the diffusion).
- `MergedCellsGenerator.TryPlaceMergedCell` / `CanPlace` — control merged-cell placement.
- `ConductorConnectionJob` is invoked from `ElectricitySubSystem.Recalculate`; patch `Recalculate`/`DealDamages` (managed) rather than the job.

### Burst-compiled — CANNOT be Harmony/IL-patched
The following are `[BurstCompile] IJob` structs. Once Burst compiles them to native code, their `Execute` bodies are **not** managed IL and **cannot be patched with Harmony**. To change their behaviour you must either change the managed inputs/fields they read, or (heavy-handed) disable Burst so the safe-managed fallback runs.

- **`BurningUpdateJob`** — the actual fire-diffusion math (heat transfer, the `0.7071` diagonal factor, Perlin variance, burnt detection). To tune fire, change the `CellType` fire constants that feed `CellBurningManager`'s native maps; you cannot rewrite the diffusion formula via Harmony.
- **`ConductorConnectionJob`** — the electricity graph build (range checks, chain recursion, group merging, crossing rejection). Tune via `ElectricityConductor`/`ElectricityManager` fields (`beamRange`, `chainLength`, `conductivity`, `minConductivity`); the connection algorithm itself is native.

Related Burst jobs exist elsewhere in the codebase (`BackgroundGeneratorJob`, `LightmapGeneratorJob`, `PlantGeneratorJob`, `SubBiomGeneratorJob`, `HeightMapGeneratorJob`, `RasterizationJob`, etc.) — same caveat applies to any `*Job.cs` marked `[BurstCompile]`.

> Tip: to force the managed (non-Burst) path for debugging or patching, Burst can be globally disabled (BepInEx launch option / `--burst-disable-compilation` or the in-editor toggle). With Burst off, the job `Execute` runs as ordinary IL and *can* be patched — but expect a large performance hit on the fire/electricity ticks.
