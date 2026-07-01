# Modules, Grid & Loadouts
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK builds a ship out of **modules** placed on a 100x100 sparse **module grid**. The grid is anchored around a fixed set of six **main slots** (positions hard-coded near `(50,50)`):

| Cluster | Root position | Slot type | Holds |
| --- | --- | --- | --- |
| `Passive` | `(50,50)` ShipGridPosition | `Embedded` | The ship "core" main module |
| `PrimaryWeapon` | `(46,50)` | `Weapon` | A `WeaponModule` |
| `SecondaryWeapon` | `(54,50)` | `Weapon` | A `WeaponModule` |
| `Active1` | `(46,46)` | `Active` | An `ActiveModule` |
| `Active2` | `(50,46)` | `Active` | An `ActiveModule` |
| `Active3` | `(54,46)` | `Active` | An `ActiveModule` |

**How it fits together:**

- A **`ModuleData`** is a `SerializedScriptableObject` (Odin) authored asset describing one module: icon, type, level, power-level range, effect-field shapes, connection-count distribution, and a list of **`ModuleEffect`** instances. `DeepCopy()` produces a runtime **`Module`** instance.
- A **`Module`** is the runtime object. It has four directional connection flags (`North/East/South/West`), a `Level`, a `PowerLevel` (rolled from the data's `MinMaxInt`), an optional **`PowerCore`** `ModuleEffectField` (its powered "footprint"), an optional `LevelModificationField`, and a cloned list of `ModuleEffect`s whose `Module` back-reference points at it.
- The **`ModuleGrid`** (`IModuleGrid`) stores `Dictionary<Vector2Int, Module>` plus per-slot `ModuleSlotType`s. It owns six **`ModuleCluster`**s (one per `ClusterType`). Whenever modules change, `OnModulesChanged()` recomputes connectivity, powered slots, and level deltas.
- A **`ModuleCluster`** flood-fills outward from its root (`GridHelper.CollectConnectedModulesRecursive`) following matching connection flags. A module is **connected** if reachable from the cluster root via connections, and **powered** if it sits on a slot reserved/powered by a connected module's `PowerCore` footprint. Only modules that are *connected AND powered* (`ConnectedAndPoweredModules`) actually contribute effects. The number of power cores a cluster can support is the main module's `PowerLevel`.
- **Weapons plug in** as `WeaponModule` (a `Module` subclass carrying `WeaponData`) installed in a weapon cluster's root. `ModuleSlotWeaponHolder` listens to `IModuleCluster.ModulesRefreshed` and rebuilds a `WeaponBase` via `WeaponFactory.Create(weaponData, cluster.ConnectedAndPoweredModules)` — so augmentation modules in the cluster (those whose effects implement `IWeaponModifier` / `ModifyWeaponProperty`) modify the weapon. Active modules fire/spawn via `ModuleActivator`.
- **Effects** (`ModuleEffect` subclasses) implement lifecycle hooks (`OnInstalled`, `OnUninstalled`, `OnUpdate`, `OnRecalculateUnitStats`, `OnContainingClusterRefreshed`, `ModifyWeapon`). Effect magnitudes are usually `FloatSeries` indexed by `Module.Level - 1`.
- **Loadouts** are starter templates. `LoadoutTemplate` (ScriptableObject) names a module for each of the six slots and `Apply(...)`s them to a real `ModuleGrid`. `LoadoutPool` lists templates; `LoadoutSelector`/`LoadoutCard` are the picker UI; `LoadoutUnlocker` unlocks a template when one of its `unlockingModules` is installed; unlock state lives in `PlayerPrefs` via `MetaProgressManager`.
- **Vault** is the persistent off-grid inventory of owned modules (plus consumables/ingredients). `ModulePickup` stores into the Vault; `ModuleGridScreen` moves modules between Vault, Shop, and grid.
- **Persistence:** almost everything uses the **Memento** pattern (`IMementoOriginator`). Mementos store module-data IDs (resolved via the `IRegistry<ModuleData,string>` service) plus connections, power level, and effect fields.

**Two grid implementations** exist: full `ModuleGrid` (player ships, spatial 2D grid with slot types and validation) and `SimpleModuleGrid` (minions — flat lists per cluster, no spatial layout). `ModuleGridOwner` chooses between them via its `useSimplifiedGrid` flag.

## Class Index

| Class | Kind | Summary |
| --- | --- | --- |
| `ModuleData` | SerializedScriptableObject | Authored module definition; `DeepCopy()` -> `Module`. |
| `Module` | class | Runtime module instance; connections, level, power core, effects. |
| `Module.Memento` | nested class | Serialized save form of a module. |
| `ModuleType` | ScriptableObject | Category (weapon/active/etc.); `isMain`, shop order, background. |
| `ModuleEffect` | abstract class | Base for module behaviors; lifecycle hooks + `Clone()`. |
| `ModifyResourceCapacity` | ModuleEffect | Increases a `Resource` capacity by a level-scaled delta. |
| `ModifyWeaponProperty` | ModuleEffect, `IWeaponModifier` | Adds/multiplies a weapon stat (12 target properties). |
| `ModuleEffectField` | class | Boolean shape footprint (power core / level field) parsed from a sprite. |
| `ModuleEffectFieldSegment` | MonoBehaviour | One cell visual of an effect field. |
| `ModuleGrid` | class, `IModuleGrid` | Spatial player grid; modules, slots, clusters, level deltas. |
| `ModuleGrid.Memento` | nested class | Serialized grid (slot-type IDs + module mementos). |
| `SimpleModuleGrid` | class, `IModuleGrid` | Flat list-based grid for minions. |
| `IModuleGrid` | interface | Grid contract: clusters, install events, update/recalc. |
| `ModuleCluster` | class, `IModuleCluster` | Connectivity/power graph rooted at one main slot. |
| `SimpleModuleCluster` | class, `IModuleCluster` | List-based cluster (minion grid). |
| `IModuleCluster` | interface | Cluster contract: main module, powered modules, refresh event. |
| `ClusterType` | enum | Passive, PrimaryWeapon, SecondaryWeapon, Active1/2/3. |
| `ModuleCollection` | ScriptableObject | Simple `List<ModuleData>` asset. |
| `ModuleRegistry` | ScriptableObjectRegistry | `IRegistry<ModuleData,string>` service. |
| `ModuleSlotType` | ScriptableObject | Slot rules; static well-known types; `IsCompatible`. |
| `LevelChangerSlotType` | ModuleSlotType | Slot that applies a `levelDelta` to placed modules. |
| `ModuleSlotTypeRegistry` | ScriptableObjectRegistry | `IRegistry<ModuleSlotType,string>` service. |
| `ActiveModule` | abstract Module | Module with cooldown/cost; `Activate(Unit)`. |
| `ActiveModuleData` | abstract ModuleData, `IHasCost` | Data for active modules. |
| `ModuleActivator` | MonoBehaviour | Polls an Active cluster and fires its `ActiveModule`. |
| `WeaponBasedActiveModule` | ActiveModule | Active that fires a weapon. |
| `WeaponBasedActiveModuleData` | ActiveModuleData | Data for weapon-based active. |
| `SpawnMinionModule` | ActiveModule | Active that spawns minion units carrying copied modules. |
| `SpawnMinionModuleData` | ActiveModuleData | Data for minion-spawn active. |
| `WeaponModule` | Module | Holds `WeaponData`; root of a weapon cluster. |
| `WeaponModuleData` | ModuleData | Data for a weapon module. |
| `ModuleSlotWeaponHolder` | WeaponHolder | Builds the live `WeaponBase` from a weapon cluster. |
| `ModuleGridOwner` | SavableComponent, `IGridOwner` | Component owning a ship/minion's grid + lifecycle. |
| `ModuleGridOwner.Data` | ComponentData | Runtime grid state + stat recalculation. |
| `IGridOwner` | interface | Exposes `ModuleGrid`. |
| `ComponentData` | abstract class | Base entity-component data (`entity`, `Clone`, lifecycle). |
| `IComponentDataCreator` | interface | `ComponentData CreateData()`. |
| `GridHelper` | static class | Connectivity flood-fill + move helpers. |
| `ModuleGridPreview` | class | Validates a hypothetical move; returns `ValidationError`s. |
| `ModulePickup` | InteractiblePickup | World pickup that stores a module into the Vault. |
| `ModulePickupFactory` | `IFactory<…,ModulePickup>` | Spawns module pickups. |
| `ModulePickupList` | MonoBehaviour | Debug/UI list of spawnable modules. |
| `SpawnModulePickupButton` | MonoBehaviour | Button that spawns a module pickup near the ship. |
| `ModuleDataDistribution` | class | Weighted distribution of `ModuleData`. |
| `ModuleDataDistributionItem` | class | One weighted entry. |
| `IModuleModifierSlot` | interface | `Modify`/`RemoveModification` on a module. |
| `Vault` | class | Persistent inventory of modules/consumables/ingredients. |
| `Loadout­Template` | ScriptableObject | Starter ship: a module per slot + unlock modules. |
| `LoadoutPool` | ScriptableObject | List of `LoadoutTemplate`. |
| `LoadoutSelector` | MonoBehaviour | Loadout picker (input + cards). |
| `LoadoutCard` | MonoBehaviour | One loadout's UI card. |
| `LoadoutModuleItem` | MonoBehaviour | One module entry on a card. |
| `LoadoutUnlocker` | MonoBehaviour | Unlocks loadouts when an unlocking module is installed. |
| `LoadLoadoutButton` | MonoBehaviour | Debug button: applies a loadout to all ships. |
| `MetaProgressManager` | class | PlayerPrefs-backed loadout unlock + death count. |
| `AbilitySlot` | MonoBehaviour | HUD icon mirroring a grid main slot. |
| `AbilitySlotsPanel` | MonoBehaviour | Binds the 5 weapon/active HUD slots to a grid. |
| `ModuleGridInput` | MonoBehaviour | Player input for selecting/moving modules. |
| `ModuleGridScreen` | ShipMenuTab | The ship-build screen (grid + shop + vault). |
| `ModuleGridWidget` | ModuleContainerWidget | Visual grid; placement, hover, clusters. |
| `ModuleContainerWidget` | abstract MonoBehaviour | Base for grid/vault/shop module containers. |
| `ModuleIconWidget` | MonoBehaviour | One module's icon + connections + power level. |
| `ModuleEffectFieldWidget` | MonoBehaviour | Renders a `ModuleEffectField` grid. |
| `ClusterWidget` | MonoBehaviour | Visualizes a cluster's reserved/powered slots. |
| `ConnectionWidget` | MonoBehaviour | One edge connector visual. |
| `SpecialSlotWidget` | MonoBehaviour | Visual for a special slot type. |
| `HoveredModuleInfo` | InfoPopup | Tooltip showing module stats/effects. |

## Classes

### ModuleData
- **Kind:** `SerializedScriptableObject` (Odin), `IIdentifiable<string>`. CreateAssetMenu `Punk/Modules/Module`.
- **Purpose:** Authoring asset for a module type.
- **Key fields:** `string id` (serialized, exposed as `Id`); `Sprite icon`; `string displayName`; `int level = 1`; `string description`; `ModuleType moduleType`; `ColorAsset color`; `MinMaxInt powerLevel`; `SpriteDistribution powerCore`; `SpriteDistribution levelModificationField`; `IntDistribution connectionCountDistribution`; `float repeatedDropChanceMultiplyer`; `List<ModuleEffect> effects`; `List<ModuleType> supportedAugmentationTypes`; `bool repeatInShop`; `bool canBeBoosted = true`; `string gridPlacementSfx`.
- **Key methods:** `virtual Module DeepCopy()` (overridden by subclasses to return the right `Module` subtype); `bool Equippable` (icon + displayName present); `bool IsTheSame(ModuleData)`.
- **Relationships:** Registered in `ModuleRegistry`. Subclasses: `WeaponModuleData`, `ActiveModuleData` (-> `WeaponBasedActiveModuleData`, `SpawnMinionModuleData`).

### Module
- **Kind:** class implementing `IPropertyListOwner`, `IMementoOriginator<Module.Memento>`.
- **Purpose:** Runtime instance of a module placed in a grid/vault.
- **Key fields/properties:** `ModuleData Data`; `int Level`; `int BaseLevel = 1`; `int PowerLevel` (rolled in ctor from `moduleData.powerLevel`); `bool North/East/South/West`; `List<ModuleEffect> Effects`; `ModuleEffectField PowerCore`; `ModuleEffectField LevelModificationField`. Convenience getters `Icon`, `DisplayName`, `Description`, `ModuleType`, `Color` delegate to `Data`.
- **Key methods:** ctor `Module(ModuleData)` clones effects and draws power-core/level fields; `virtual Module DeepCopy()`; `void RandomizeConnections()` (uses `connectionCountDistribution`); `CopyConnectionsFrom`, `AddAllConnections`, `bool GetConnection(Vector2Int)`; lifecycle `OnInstalled/OnUninstalled/OnUpdate(Unit.Data)`, `OnContainingClusterRefreshed(IModuleCluster)`, `OnRecalculateUnitStats(Unit.Data)` — each forwards to all `Effects`; `bool CanBeAugmentedWith(Module)` (checks `Data.supportedAugmentationTypes`); `CreateMemento()` / `RestoreFromMemento(Memento)`.
- **Nested `Memento`:** fields `moduleDataId`, four connection bools, `powerCore`, `levelModificationField`, `powerLevel`; `Restore()` rebuilds via `ServiceLocator.Get<IRegistry<ModuleData,string>>().Get(id).DeepCopy()`.
- **Relationships:** Subclasses `WeaponModule`, `ActiveModule`. `Effects[i].Module` points back to owner.

### ModuleType
- **Kind:** ScriptableObject. CreateAssetMenu `Punk/Module/Module type`.
- **Key fields:** `string displayName`; `Sprite background`; `int orderInShop`; `bool isMain` (true = a cluster main module like core/weapon/active; affects power-level display & default placement).

### ModuleEffect
- **Kind:** abstract `[Serializable]` class.
- **Purpose:** Pluggable behavior attached to a module.
- **Key members:** `Module Module { get; set; }`; virtual hooks `OnInstalled`, `OnUninstalled`, `OnUpdate`, `OnContainingClusterRefreshed(IModuleCluster)`, `OnRecalculateUnitStats(Unit.Data)`, `ModifyWeapon(Unit, WeaponBase)`; `abstract ModuleEffect Clone()`; `virtual bool OnValidate()`.
- **Modding note:** Custom effects subclass this; `Clone()` MUST deep-copy serialized fields (the ctor clones the data's effect list). Effects often also implement `IWeaponModifier`, `IHasDescriptionForUnit`, or `IHasDescriptionForWeapon` for tooltips.

### ModifyResourceCapacity
- **Kind:** `ModuleEffect`, `IHasDescriptionForUnit`.
- **Key fields:** `Resource resource`; `FloatSeries delta`. `DeltaForCurrentLevel = delta.GetElement(Module.Level - 1)`.
- **Key methods:** `OnRecalculateUnitStats` calls `owner.IncreaseCapacity(resource, DeltaForCurrentLevel)`; `Clone()` copies `resource`/`delta`; `GetPropertyList(...)` builds tooltip rows. Note: in co-op, `ModuleGridOwner.Data.RestoreFromMemento` scales `delta.baseValue` by an enemy's `coopResourceMultiplier`.

### ModifyWeaponProperty
- **Kind:** `ModuleEffect`, `IWeaponModifier`, `IHasDescriptionForWeapon`.
- **Enums:** `TargetProperty { FireRate, BurstSize, BurstDelay, ProjectileCount, Spread, AngleVariance, AngleOffset, KnockbackForce, Cost, Range, Speed, Damage }`; `Operation { Add, Multiply }`; `DeltaCalculationMode { Constant, FromOriginal, FromCurrent }`.
- **Key fields:** `[SerializeField] TargetProperty targetProperty`, `Operation operation`, `DeltaCalculationMode deltaCalculationMode`, `FloatSeries value`. A static dictionary `valueAppliers` maps each property to getter/setter lambdas on `WeaponBase`/`WeaponData`.
- **Key methods:** `Modify(WeaponBase)` applies `value.GetElement(Level-1)` per mode/operation; `Clone()`; `GetDescription(...)`.

### ModuleEffectField
- **Kind:** plain class (not serialized as asset; built at runtime).
- **Purpose:** A boolean "footprint" mask (power-core area or level-modification area) parsed from a sprite, with random mirroring/rotation each time.
- **Key fields:** `bool[] fieldData`; `int width`; `int height`.
- **Key methods:** ctor `ModuleEffectField(Sprite)` parses pixels (alpha > 0.5 = filled), requires odd width/height; `IEnumerable<Vector2Int> GetPositionsRelative()` (offsets relative to center); `bool GetValueRelative(Vector2Int)`.
- **Relationships:** A `Module`'s `PowerCore` and `LevelModificationField` are instances; `ModuleCluster.RefreshPoweredSlots` and `ModuleGrid.OnModulesChanged` iterate these footprints.

### ModuleGrid
- **Kind:** class, `IModuleGrid`, `IMementoOriginator<ModuleGrid.Memento>`.
- **Purpose:** The spatial player grid.
- **Key static fields:** `ShipGridPosition (50,50)`, `PrimaryWeaponGridPosition (46,50)`, `SecondaryWeaponGridPosition (54,50)`, `Active1/2/3GridPosition`, and `Vector2Int[] MainSlotPositions` (all six).
- **Key instance state:** `Dictionary<Vector2Int,ModuleSlotType> slotTypes`; `Dictionary<Vector2Int,Module> modules`; `Dictionary<ClusterType,ModuleCluster> clusters` (six, created in ctor); `Dictionary<Vector2Int,int> levelDeltas`; `HashSet<Vector2Int> poweredSlots`; `ModuleGridPreview preview`.
- **Key properties:** `IReadOnlyDictionary<Vector2Int,Module> Modules`; `IReadOnlyDictionary<Vector2Int,ModuleSlotType> SpecialSlots`; `IEnumerable<ModuleCluster> Clusters`; `PositionsWithLevelDelta`; indexer `this[Vector2Int]`; `PoweredSlots`.
- **Events:** `ModuleInstalled`, `ModuleUninstalled`, `ModuleInstalledAtPosition`, `ModuleUninstalledAtPosition`, `LevelDeltaChanged`.
- **Key methods:** `RandomizeSlots()` (scatters special slot types into placement rects per `ModuleSlotType.gridPlacementRectSize`/`countInPlacementRect`, then clears 3x3 around each cluster root); `Install(Vector2Int, Module)`, `Uninstall(Vector2Int/Module)`, `Move`, private `Swap`; `OnModulesChanged()` (refreshes connected modules, powered slots, powered modules, and recomputes level deltas from `LevelChangerSlotType` + modules' `LevelModificationField`); `OnUpdate(Unit.Data)` / `OnRecalculateStats(Unit.Data)` iterate each cluster's `ConnectedAndPoweredModules`; queries `GetSlotType`, `GetLevelDelta`, `IsEmpty`, `IsPowered`, `IsPoweredAndConnected`, `IsConnectedToRoot`, `IsConnectedToPrimary/SecondaryWeapon`, `GetContainingCluster`, `IsRecommended`; `bool CanMove(module, position, out errors)` delegates to `preview`.
- **Note:** `OnRecalculateStats` special-cases `SpawnMinionModule` main modules (recalculated even if the cluster has no powered followers).

### SimpleModuleGrid
- **Kind:** class, `IModuleGrid`. Used for minions (no spatial layout).
- **State:** `Dictionary<ClusterType,SimpleModuleCluster> clusters` (six).
- **Key methods:** `InstallMainModule(Module, ClusterType)`, `UninstallMainModule`, `InstallAugmentation(Module, ClusterType)`; `OnUpdate`/`OnRecalculateStats`; memento stores each cluster's modules as `List<Module.Memento>`. (Note: `RestoreFromMemento` restores `active3` from `memento.active2` — a decompiled-game bug, mirror it if patching.)

### ModuleCluster
- **Kind:** class, `IModuleCluster`. The connectivity/power graph for one main slot in a `ModuleGrid`.
- **State:** `ModuleGrid grid`; `Vector2Int rootPosition`; `connectedModules`, `connectedAndPoweredModules`, `reservedSlots`, `poweredSlots`; `int connectedPowerCores`; `ClusterType ClusterType`.
- **Key properties:** `RootPosition`; `ConnectedModules`; `ReservedSlots`; `PoweredSlots`; `ConnectedAndPoweredModules`; `bool HasMainModule` (grid not empty at root); `int ConnectedCores`; `Module MainModule` (get = `grid[rootPosition]`, set = `grid.Install(rootPosition, value)`).
- **Event:** `ModulesRefreshed`.
- **Key methods:** `RefreshConnectedModules()` (`GridHelper.CollectConnectedModulesRecursive` from root); `RefreshPoweredSlots()` (walks each connected module's `PowerCore` footprint, reserving up to `MainModule.PowerLevel` cores, marking `canBePowered` slots as powered); `RefreshPoweredModules()` (intersects connected with powered, fires `OnContainingClusterRefreshed`); `IsPowered`, `IsReserved`, `IsConnected`, `Contains`.

### IModuleCluster / IModuleGrid / ClusterType
- `IModuleCluster`: `ConnectedAndPoweredModules`, `HasMainModule`, `MainModule`, `ClusterType`, event `ModulesRefreshed`, `Contains`.
- `IModuleGrid` : `IMementoOriginator`; events `ModuleInstalled`/`ModuleUninstalled`; `GetAllClusters()`, `GetCluster(ClusterType)`, `OnUpdate(Unit.Data)`, `OnRecalculateStats(Unit.Data)`.
- `ClusterType` enum: `Passive, PrimaryWeapon, SecondaryWeapon, Active1, Active2, Active3`.

### ModuleSlotType
- **Kind:** ScriptableObject, `IIdentifiable<string>`. CreateAssetMenu `Punk/Grid/Slot types/Regular`.
- **Key fields:** `string id`; `int gridPlacementRectSize`; `int countInPlacementRect`; `List<ModuleType> compatibleModuleTypes`; `SpecialSlotWidget gridVisualPrefab`; `bool canBePowered`; `bool usedForValidation`.
- **Static well-known values:** `Normal, Embedded, Weapon, Active, Invalid`, and `Values`; set via `SetValues(List<ModuleSlotType>)` which matches by name substring ("normal","embedded","weapon","invalid","active").
- **Key method:** `bool IsCompatible(Module)` -> `compatibleModuleTypes.Contains(module.ModuleType)`.
- **Subclass:** `LevelChangerSlotType` adds `int levelDelta`, applied to modules in `ModuleGrid.OnModulesChanged`.

### ActiveModule / ActiveModuleData
- `ActiveModule : Module` (abstract): `float Cooldown`, `float ActivationCost`, `Resource ResourceUsed`; `abstract void Activate(Unit owner)`.
- `ActiveModuleData : ModuleData, IHasCost` (abstract): abstract `Cooldown`, `ActivationCost`, `ResourceUsed`; `CostAmount`/`CostCurrency` from those.

### ModuleActivator
- **Kind:** MonoBehaviour.
- **Key serialized fields:** `ModuleGridOwner moduleGridOwner`, `Ship ship`, `int moduleIndex` (1/2/3 -> `Active1/2/3`), `float minDelayAfterLeavingShipMenu`.
- **Behavior:** on bind, resolves its `IModuleCluster`; each `Update`, if the cluster has a main `ActiveModule`, the activator `IsActivated`, the cooldown has elapsed, and enough time passed since the ship menu closed, calls `ActiveModule.Activate(unit)`.

### WeaponBasedActiveModule / Data
- `WeaponBasedActiveModule : ActiveModule` holds `WeaponData`; on `OnContainingClusterRefreshed` rebuilds a `WeaponBase` from `cluster.ConnectedAndPoweredModules`; `Activate` equips/fires the weapon (`FakeBarrel`), checks/deducts `ResourceUsed`.
- `WeaponBasedActiveModuleData : ActiveModuleData` has `WeaponData weaponData`; `Cooldown = 1f / weaponData.fireRate`, `ActivationCost = weaponData.cost`, `ResourceUsed = weaponData.resourceUsed`.

### SpawnMinionModule / Data
- `SpawnMinionModule : ActiveModule` spawns up to `Level` minions of `Minion` (a `Unit`). On spawn it copies the cluster's other connected+powered modules into the minion's `SimpleModuleGrid` (weapon-modifier effects -> `PrimaryWeapon` cluster, else `Passive`), then recalculates the minion's stats. `OnUpdate` despawns excess minions; `OnUninstalled` destroys all minions of its connection type.
- `SpawnMinionModuleData : ActiveModuleData`: `Unit minion`, `cooldown`, `activationCost`, `Resource resourceUsed`, `spawnPositionOffset`, `startSpeed`, `dropAngle`, `dropAngleVariance`, `ParticleSystem particlePrefab`.

### WeaponModule / WeaponModuleData / ModuleSlotWeaponHolder
- `WeaponModule : Module` carries `WeaponData WeaponData` and builds a `baseWeapon` via `ServiceLocator.Get<WeaponFactory>().Create(WeaponData)`. Used for tooltip property lists.
- `WeaponModuleData : ModuleData` has `WeaponData weapon`; `DeepCopy()` -> `WeaponModule`. CreateAssetMenu `Punk/Modules/WeaponModule`.
- `ModuleSlotWeaponHolder : WeaponHolder` (serialized `gridOwner`, `bool isSecondary`): binds to the primary/secondary weapon cluster, and on `ModulesRefreshed` recreates the live `WeaponBase` via `WeaponFactory.Create(weaponData, cluster.ConnectedAndPoweredModules)` — this is where augmentation modules modify the actual ship weapon.

### ModuleGridOwner
- **Kind:** `SavableComponent<ModuleGridOwner.Data>`, `IGridOwner`.
- **Key serialized field:** `bool useSimplifiedGrid` (true -> `SimpleModuleGrid`, false -> full `ModuleGrid` with `RandomizeSlots()`).
- **Property:** `IModuleGrid ModuleGrid`.
- **`CreateData()`** also runs every child `IModuleGridInitializer.Initialize(grid)` (nested interface `ModuleGridOwner.IModuleGridInitializer`).
- **Nested `Data : ComponentData, IGridOwner, IMementoOriginator<Data.Memento>`:** holds the `IModuleGrid`; subscribes to install/uninstall to call `module.OnInstalled/OnUninstalled` and flag `modulesChanged`; `OnUpdate()` recalculates stats when dirty then `moduleGrid.OnUpdate(unit)`. Memento wraps the grid's memento.

### ComponentData / IComponentDataCreator / IGridOwner
- `ComponentData` (abstract): public `EntityData entity`; `abstract ComponentData Clone()`; virtual `OnCreate`/`OnDestroy`.
- `IComponentDataCreator`: `ComponentData CreateData()`.
- `IGridOwner`: `IModuleGrid ModuleGrid { get; }`.

### GridHelper
- **Kind:** static helper.
- **Key methods:** `Move(...)` overloads (note `swapEnabled = false`, so moves overwrite, not swap, at the raw-dictionary level); `bool TryGetPosition(modules, module, out pos)`; `CollectConnectedModulesRecursive(grid, position, results)` (flood-fill along mutually-matching connections); `bool IsConnectedWithNeighbour(grid, position, direction)` (both modules must have the facing connection flag).

### ModuleGridPreview
- **Kind:** class. Validates a hypothetical placement.
- **Enum `ValidationErrorType` { SlotType, Connection, MainModulesConnected, ClustersOverlap, IncompatibleAugmentation }**; struct `ValidationError { errorType, gridPosition, direction }`.
- **Key method:** `bool ValidateMove(ModuleGrid grid, Module module, Vector2Int to, out IReadOnlyList<ValidationError> errors)` — copies grid state, applies the move, and checks: slot-type compatibility, opposing connection flags match, two main slots aren't connected, power-core footprints don't overlap between clusters, and non-root modules are valid augmentations of their cluster main.

### ModulePickup / ModulePickupFactory / ModulePickupList / SpawnModulePickupButton
- `ModulePickup : InteractiblePickup<Data>` — world pickup; `Data.module` is a `Module`; `OnPickedUp(ship)` logs, registers in `RunData`, and `ServiceLocator.Get<Vault>().Store(module, markAsNew:true)`. `UpdateVisuals` shows icon/background/power level.
- `ModulePickupFactory : IFactory<ModuleData, Vector2, ModulePickup>` — `Create(moduleData, position)` instantiates the prefab, deep-copies the module, randomizes connections.
- `ModulePickupList` — instantiates a `SpawnModulePickupButton` per registry module (ordered by `moduleType.orderInShop`, skipping a `hiddenType`).
- `SpawnModulePickupButton` — `Spawn()` uses `ModulePickupFactory` to drop the module at the ship.

### ModuleDataDistribution / ModuleDataDistributionItem / ModuleCollection
- `ModuleDataDistribution : Distribution<ModuleData, ModuleDataDistributionItem>` (weighted); `ModuleDataDistributionItem : DistributionItem<ModuleData>`.
- `ModuleCollection : ScriptableObject` — `List<ModuleData> items`. CreateAssetMenu `Punk/Collections/ModuleCollection`.

### IModuleModifierSlot
- Interface: `void Modify(Module module)`, `void RemoveModification(Module module)`. (Slot that mutates modules placed into it.)

### Vault
- **Kind:** class, `IMementoOriginator<Vault.Memento>`. The persistent off-grid inventory.
- **State:** `List<Module> modules`; `List<ConsumableWithAmount> consumables` (8 slots); `Dictionary<Ingredient,int> ingredients`; `HashSet<Module> newModules`.
- **Key methods:** `Store(Module, bool markAsNew)`, `Remove(Module)`, `Contains`; consumable `Add/Remove/GetAmount`; ingredient `Add/Remove/AmountOf`; `IsNew`/`MarkModuleSeen`. Properties `Modules`, `ModuleCount`, `HasNew`. Events `ConsumableAmountChanged`, `IngredientAmountChanged`, `NewModuleSeen`.
- **Relationship:** Resolved via `ServiceLocator.Get<Vault>()`; mementos resolve IDs through the module/ingredient/consumable registries.

### LoadoutTemplate
- **Kind:** ScriptableObject. CreateAssetMenu `Punk/Loadout/Template`.
- **Key fields:** `string displayName`, `string description`; `ModuleData embedded, primary, secondary, active1, active2, active3`; `ModuleData[] unlockingModules`.
- **Key method:** `Apply(ModuleGridOwner.Data owner)` — installs each non-null module (deep-copied + `RandomizeConnections()`) at the matching main slot. Only works on a full `ModuleGrid` (logs error on `SimpleModuleGrid`).

### LoadoutPool / LoadoutSelector / LoadoutCard / LoadoutModuleItem / LoadoutUnlocker / LoadLoadoutButton
- `LoadoutPool : ScriptableObject` — `List<LoadoutTemplate> loadouts`. CreateAssetMenu `Punk/Loadout/Pool`.
- `LoadoutSelector : MonoBehaviour` — serialized `LoadoutPool loadoutPool`, `LoadoutCard loadoutCardPrefab`, input refs; reads `MetaProgressManager.GetUnlockedLoadouts()`; `IsLocked(loadout)` = has `unlockingModules` and name not in unlocked list; event `LoadoutSelected`.
- `LoadoutCard` — displays a template's weapon/active modules; events `Clicked`.
- `LoadoutModuleItem` — one module name + `ModuleIconWidget`.
- `LoadoutUnlocker : MonoBehaviour` — serialized `LoadoutPool loadoutPool`; on each ship's `ModuleGrid.ModuleInstalled`, if the installed module's `Data` is in some template's `unlockingModules`, calls `MetaProgressManager.UnlockLoadout(loadout)`.
- `LoadLoadoutButton : MonoBehaviour` — debug button; `OnClick()` applies its `Loadout` to all ships.

### MetaProgressManager
- **Kind:** plain class (a `ServiceLocator` service).
- **Loadout unlock storage is PlayerPrefs key `META_UNLOCKED_LOADOUTS`** (semicolon-joined template names).
- `string[] GetUnlockedLoadouts()`; `bool UnlockLoadout(LoadoutTemplate)` (adds name if new); `static void ResetUnlockedLoadouts()` (deletes the key).

### AbilitySlot / AbilitySlotsPanel
- `AbilitySlot : MonoBehaviour` — `ModuleIconWidget icon`, `Image hint`, `Sprite emptySlotSprite`. `Assign(ModuleGrid, Vector2Int)` subscribes to the grid's `ModuleInstalledAtPosition`/`ModuleUninstalledAtPosition` to mirror that slot's module in the HUD.
- `AbilitySlotsPanel : MonoBehaviour` — five `AbilitySlot`s (primary/secondary weapon, active1/2/3); `Assign(ModuleGrid, PlatformSpriteSet)` wires each to its `ModuleGrid.*GridPosition`.

### ModuleGridInput
- **Kind:** MonoBehaviour. All keyboard/gamepad/mouse handling for the build screen.
- **Struct `ModuleMoveArgs`** { `Module module`, `ModuleContainerWidget origin`, `ModuleContainerWidget target`, `Vector2Int targetGridPosition`, `bool usingMouse` }.
- **Events:** `SelectionChanged`, `ModuleMoveStarted`, `ModuleDragStarted`, `ModuleDragged`, `ModuleMoveFinished`, `MoveCanceled`, `ModuleUnequip`, `ShopOpenClicked`, `VaultOpenClicked`.
- **Key:** `IsEditingEnabled`, `IsMovingModule`; binds the `"Shop"` input action map (actions `PickUpModule`, `SelectShop`, `SelectVault`, `Unequip`, `MoveSelection`, `MoveGrid`). Drives `ModuleGridWidget`, `VaultGridWidget`, `ShopWidget` (all resolved from `ServiceLocator`).

### ModuleGridScreen
- **Kind:** `ShipMenuTab`, pointer handlers. The full build UI orchestrator.
- **Key:** holds the live `ModuleGrid currentGrid` (= `Ship.ModuleGridOwner.ComponentData.ModuleGrid`); subscribes to `ModuleGridInput` events; `OnModuleDropped(ModuleMoveArgs)` validates via `target.CanMoveTo(...)` then routes Move-in-grid / vault<->grid / buy-from-shop; `MoveToVault`, `Unequip`. `EditEnabledOutsideStation` allows editing away from a station.

### ModuleGridWidget
- **Kind:** `ModuleContainerWidget`. Visual grid (placement, hover, clusters, slot LUT background).
- **Key serialized fields:** `Vector2Int gridSize`, `ModuleIconWidget gridItemPrefab`, `GridHover hover`, slot/effect/cluster prefabs, pulse/scroll tuning.
- **Property:** `ModuleGrid ModuleGrid` (subscribes to `LevelDeltaChanged`); `GridSize`.
- **Key methods:** `OnOpened`/`OnClosed`; `RefreshModules()` (rebuilds icon widgets, displays connection/power state, preview clusters); `RefreshSlotTypes()`; `CanMoveTo(module, gridPos)` (validates and logs `GetErrorMessage`); `GetGridPosition(screen)`, `GetDefaultPositionForModule`, `GetTargetSlotAfterMove`; hover/selection/auto-scroll helpers. `CanMove` forbids moving the embedded ship core (`ShipGridPosition`).

### ModuleContainerWidget
- **Kind:** abstract MonoBehaviour, base for grid/vault/shop containers. `Module SelectedModule` (+event `SelectedModuleChanged`); `bool IsActive`; abstract `OnDraggedModuleEnter/Exit`, `CanMove`, `CanMoveTo`, `SelectModule`, `SelectFirstModule`, `MoveSelection`, `OnMouseMoved`, `TweenModuleToGrid`, `GetModuleWidget`; virtual `OnSelectionChanged`.

### ModuleIconWidget
- **Kind:** MonoBehaviour. One module's visual (icon, background, four `ConnectionWidget`s, level pips, power-level text).
- **Key:** `Module Module` setter populates everything; `DisplayModuleData(ModuleData)` (static preview, e.g. loadout cards); `DisplayState(connectedToRoot, powered, n,e,s,w)`; `DisplayPowerLevel(powerLevel, connectedCores)` (overload coloring); `DisplayError(ValidationError)`; `SetHighlighted`, `SetNew`, `Pulse`, `EnablePowerIndicator`, `EnableConnections`.

### ModuleEffectFieldWidget / ModuleEffectFieldSegment / ClusterWidget / ConnectionWidget / SpecialSlotWidget / HoveredModuleInfo
- `ModuleEffectFieldWidget` — `Display(ModuleEffectField)` lays out `ModuleEffectFieldSegment`s in a `GridLayoutGroup` sized to the field.
- `ModuleEffectFieldSegment` — `SetActive(bool)` toggles active/inactive cell color.
- `ClusterWidget` — `Init(ModuleCluster, gridSize)`; on `ModulesRefreshed` redraws reserved-slot visuals; `ShowHint`/`HideHint`/`SetHighlighted`; `IsInCluster(pos)` = `cluster.IsReserved(pos)`.
- `ConnectionWidget` — `SetConnected`, `SetVisible`, `ShowError`.
- `SpecialSlotWidget` — visual for a slot type; `ModuleSlotType ModuleSlotType`; `SetHighlighted`.
- `HoveredModuleInfo : InfoPopup` — `Show(Module, RectTransform, Ship)` builds the tooltip: module/weapon properties (it routes to `Ship.Unit`/`PrimaryWeapon`/`SecondaryWeapon` for main/weapon slots), effect descriptions via `IHasDescriptionForUnit`/`IHasDescriptionForWeapon`/`IProjectileModifier`, power core/level field, and "attached power cores N/PowerLevel". (Note: the bullet "AddModuleOrWeaponProperties" from the type list is a private method here, not a standalone class.)

## Modding Notes

### Dependency injection / registry wiring
- Data assets are reached through `ServiceLocator.Get<IRegistry<T,string>>()`:
  - Modules: `IRegistry<ModuleData,string>` (concrete `ModuleRegistry : ScriptableObjectRegistry<ModuleData,string>`).
  - Slot types: `IRegistry<ModuleSlotType,string>` (concrete `ModuleSlotTypeRegistry`).
- `ScriptableObjectRegistry<TItem,TId>` builds its `Dictionary` from a serialized `List<TItem> itemList` in `Initialize()`. **To add a module/slot type via Harmony**, postfix `ScriptableObjectRegistry<ModuleData,string>.Initialize` (or `.Get`) to inject your `ModuleData` into `itemDictionary`/`itemList`. Also call `ModuleSlotType.SetValues(...)` consumers know the static `Normal/Embedded/Weapon/Active/Invalid` are name-matched.
- Common service handles: `ServiceLocator.Get<Vault>()`, `ServiceLocator.Get<MetaProgressManager>()`, `ServiceLocator.Get<WeaponFactory>()`, `ServiceLocator.Get<ModulePickupFactory>()`.

### Free modules (no shop cost)
- Purchasing happens in `ModuleGridScreen.BuyFromShop` -> `currentShop.Purchase(dropArgs.module)`. Patch `Shop.Purchase` (in the Shop docs category) to always return `true`, or prefix `ModuleGridScreen.BuyFromShop` to install without charging.
- Module cost surfaces via `IHasCost` on `ActiveModuleData` (`CostAmount`/`CostCurrency`); activation cost is deducted in each `ActiveModule.Activate` (`owner.GetTank(ResourceUsed).Value -= ActivationCost`). Prefix `Activate` (or zero `ActivationCost`) to make abilities free to fire.

### Bigger grid / more / different slots
- The grid is logically 100x100 (`RandomizeSlots` scatters across `0..100`), but the **six main slots are hard-coded** as `ModuleGrid.ShipGridPosition` etc. and `MainSlotPositions`. The visible/usable area is `ModuleGridWidget.gridSize` (serialized) — increasing it shows more cells.
- To add powered space, raise main modules' `PowerLevel` (`ModuleData.powerLevel` `MinMaxInt`, rolled in `Module` ctor) — `ModuleCluster.RefreshPoweredSlots` reserves up to `MainModule.PowerLevel` cores. Harmony: postfix the `Module` ctor or patch `ModuleCluster.RefreshPoweredSlots` to lift the `connectedPowerCores < MainModule.PowerLevel` cap.
- To change which slot types appear, patch `ModuleGrid.RandomizeSlots` or seed `slotTypes` after construction. Slot compatibility is `ModuleSlotType.compatibleModuleTypes` / `IsCompatible`.
- Placement validation lives in `ModuleGridPreview.ValidateMove`; prefix it to return `true` (empty error list) to allow arbitrary placement (bypasses slot, connection, overlap, and augmentation checks). `ModuleGrid.CanMove` is the public entry point.

### Stacking / boosting effects
- Effect strength scales with `Module.Level` (`FloatSeries.GetElement(Level-1)` in `ModifyResourceCapacity`/`ModifyWeaponProperty`). Level is boosted by `LevelChangerSlotType.levelDelta` and by other modules' `LevelModificationField`, applied in `ModuleGrid.OnModulesChanged` (gated by `ModuleData.canBeBoosted`).
- To stack effects harder: patch `ModuleGrid.OnModulesChanged` to add larger deltas, set `canBeBoosted = true`, or postfix `Module.Level`/the `FloatSeries` lookup. Note only `ConnectedAndPoweredModules` contribute, so also consider the power/connection gating above.
- Custom effects: subclass `ModuleEffect`, implement `Clone()` (deep copy serialized fields — the `Module` ctor clones the effect list), and the relevant hook (`OnRecalculateUnitStats`, `ModifyWeapon`, etc.). For tooltips implement `IHasDescriptionForUnit`/`IHasDescriptionForWeapon`/`IWeaponModifier`. Because `ModuleData` is an Odin `SerializedScriptableObject`, polymorphic effect lists serialize, so injected effect types can ride along on registry-injected modules.

### Unlock all loadouts
- Lock state is `MetaProgressManager`, backed by PlayerPrefs key `META_UNLOCKED_LOADOUTS` (semicolon-joined `LoadoutTemplate.name`s).
- `LoadoutSelector.IsLocked(loadout)` returns true only when the template has `unlockingModules` AND its name isn't in `GetUnlockedLoadouts()`. Easiest unlocks:
  - Harmony postfix `MetaProgressManager.GetUnlockedLoadouts` to append every template name (or return all pool names).
  - Or prefix `LoadoutSelector.IsLocked` (private) to return `false`.
  - Or call `MetaProgressManager.UnlockLoadout(template)` for each, or just clear `unlockingModules` on the `LoadoutTemplate` assets.
- Loadouts are applied via `LoadoutTemplate.Apply(ModuleGridOwner.Data)` (full `ModuleGrid` only). `LoadoutUnlocker` is what unlocks them at runtime when an `unlockingModule` is installed.

### Other Harmony-friendly hook points
- `ModuleGrid.Install` / `Uninstall` / `Move` / `OnModulesChanged` — central mutation points (events `ModuleInstalled(AtPosition)` etc. fire here).
- `Module.OnInstalled/OnUninstalled/OnUpdate/OnRecalculateUnitStats` — per-module lifecycle.
- `ModuleCluster.RefreshConnectedModules/RefreshPoweredSlots/RefreshPoweredModules` — connectivity & power logic.
- `ModuleSlotWeaponHolder.RecreateWeapon` / `WeaponBasedActiveModule.OnContainingClusterRefreshed` — where augmentations are baked into a weapon.
- Mementos (`*.CreateMemento`/`RestoreFromMemento`) — for save-data tampering; module identity is the `Data.Id` string.
