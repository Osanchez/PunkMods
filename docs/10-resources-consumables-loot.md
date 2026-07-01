# Resources, Consumables, Pickups & Loot
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

This category covers the player/unit **resource economy**, the crafting **ingredient** store, **consumable** abilities, world **pickups**, and the **loot/drop-table** system. The pieces fit together as follows.

### The resource/energy economy
A `Resource` is a `ScriptableObject` definition (energy, hull/health, ammo-style currencies, etc.). The runtime amount of a resource held by a unit lives in a plain C# class `ResourceTank` (resource, `Capacity`, `Value`, low-threshold tracking, optional `isInfinite`). Resources are **also used as health**: `DamagableResource` is a `HealthBase` whose `CurrentHealth`/`MaxHealth` read/write a unit's `ResourceTank`, and damage drains that tank.

Tanks live inside `Unit.Data` (the per-unit component-data object, also called `ComponentData`). Key entry points on `Unit` / `Unit.Data`:
- `HasTank(Resource)`, `GetTank(Resource)`, `GetResource(Resource)` — read access.
- `InstallNewTank(Resource, capacity)`, `IncreaseCapacity(Resource, amount)` — capacity management (capacity increases auto-create a tank if missing).
- `IncreaseRechargeRate(Resource, delta)` / `GetRechargeRate(Resource)` — recharge configuration; rechargers are stored per-resource in a `Dictionary<Resource, ResourceRecharger>`.
- `RefillResources()` refills all non-shared tanks to capacity. `HasInfiniteResource` toggles `isInfinite` on every tank.

`ResourceRecharger` (one per resource per unit) runs every `Unit.Data.Update()`: it detects when a tank's value drops, waits `Resource.rechargeDelay` seconds since the last decrease, then charges the tank by `RechargeRate * deltaTime` while not full. `AutoRecharge` is a standalone `MonoBehaviour` equivalent that recharges a `ResourceTank` directly (used on non-Unit objects). `ResourceAutoChargeEffect`, `ModifyResourceCapacity`, and `DrainResourceEffect` are `ModuleEffect`s that adjust recharge rate, capacity, or drain a resource over time when a module is installed.

UI: `ResourceBar` → `ResourceBarRow` → `ResourceUnit` render a tank as rows of "unit" segments. `ResourceDispenser` periodically spits out `ResourcePickup`s to nearby units whose tank isn't full. `UnitResourceRechargeIndicator` shows particles/SFX while recharging. `SharedResourceDisplayText` binds a TMP text to a shared resource on player ship 0.

### Ingredients
`Ingredient` is a simple `ScriptableObject` (id, icons, display name). They are crafting/currency items stored in the player's `Vault` (`Vault.Add(Ingredient, count)` / `Remove`). `IngredientRegistry` is a `ScriptableObjectRegistry<Ingredient,string>`. `IngredientPickup` (an `InteractiblePickup`) adds 1 ingredient to the Vault on pickup; `IngredientsBar` / `IngredientBarItem` show the Vault contents in the HUD.

### Consumables
`Consumable` is an abstract `ScriptableObject` with `abstract void Use(Ship ship)`. Concrete types: `WeaponBasedConsumable` (fires a weapon once), `SpawnMinionConsumable` (spawns a minion owned by the ship), `SpawnPrefabConsumable` (instantiates a prefab). Consumables are stored in the `Vault` in **8 fixed slots**. `ConsumableWheel`/`ConsumableWheelItem` is the in-game radial selector (opens, slows time, on close uses the selected consumable and removes 1 from the Vault). `ConsumablesScreen`/`ConsumablesShopWidget`/`ConsumableShopItem(Widget)` are the station shop UI for buying consumables with `Price` (ingredients or resources).

### Pickups & interaction
Two pickup families:
1. **Magnet pickups** — abstract `Pickup` (e.g. `ResourcePickup`). A `LootCollector` (attached to a unit, with a magnet trigger radius) attracts these; the pickup moves toward the preferred collector and is consumed at `pickupDistance`. `CanBePickUpBy`/`OnPickedUp`/`CompareCollectors` are overridden per subclass.
2. **Interaction pickups** — abstract `InteractiblePickup<TData>` (e.g. `IngredientPickup`, `ConsumablePickup`). These require the player to press Use via the `Interactable`/`Interactor`/`InteractionPrompt` system, then fly to the ship and call `OnPickedUp(Ship)`.

### Loot / drop tables
`LootDropper` (on an enemy/destructible) drops loot on demand via either a single `DroppabbleItem` or a `DropTable`. `LootSelector` rolls the table with a seeded `Rnd`; `LootFactory` instantiates the right pickup (module / ingredient / consumable / prefab) based on `DroppabbleType`. `DropTable` → `DropTableItem` (count by probability or min/max) → optionally `DropTableWeightedGroup` (weighted random, with repeat-drop penalty for already-dropped modules via `ModuleData.repeatedDropChanceMultiplyer`).

The `Hook` system (`Hook`, `HookTargetSeeker`, `HookVisuals`, `Grabbable`) is a physics grappling tether, grouped here because `Grabbable` objects are hookable world objects akin to grabbable loot.

## Class Index

| Class | Kind | Purpose |
|---|---|---|
| `Resource` | ScriptableObject (`IIdentifiable<string>`) | Defines a resource/energy/health currency + its bar visuals & damage modifiers |
| `Resource.DamageModifier` | struct | Per-resource damage multiplier entry |
| `ResourceTank` | plain class | Runtime store of one resource on a unit (Value/Capacity/low/infinite) |
| `ResourceUnit` | MonoBehaviour | One segment sprite in a resource bar |
| `ResourceBar` | MonoBehaviour | Renders a `ResourceTank` as rows of units |
| `ResourceBarRow` | MonoBehaviour | One row of resource-bar units (shader-driven) |
| `ResourceDispenser` | MonoBehaviour | Periodically spawns `ResourcePickup`s for nearby non-full units |
| `ResourceRecharger` | plain class | Per-resource auto-recharge logic on a unit |
| `AutoRecharge` | MonoBehaviour | Standalone auto-recharge for a `ResourceTank` component |
| `ResourceAutoChargeEffect` | ModuleEffect | Module that increases a resource's recharge rate |
| `ModifyResourceCapacity` | ModuleEffect | Module that increases a resource's capacity |
| `DrainResourceEffect` | ModuleEffect | Module that drains a resource over time |
| `ResourceRegistry` | ScriptableObjectRegistry | Registry of all `Resource`s |
| `ResourcePickup` | Pickup | Magnet pickup that charges a unit's tank |
| `UnitResourceRechargeIndicator` | MonoBehaviour | Particle/SFX feedback while a resource recharges |
| `SharedResourceDisplayText` | MonoBehaviour | TMP text bound to a shared resource value |
| `DestroyWhenResourceDrained` | MonoBehaviour | Destroys a unit when a resource hits 0 |
| `DamagableResource` | HealthBase | Treats a `ResourceTank` as health; handles damage/death/shields |
| `Ingredient` | ScriptableObject (`IIdentifiable<string>`) | Crafting/currency item definition |
| `IngredientRegistry` | ScriptableObjectRegistry | Registry of all ingredients |
| `IngredientList` | MonoBehaviour | Debug UI listing all ingredients |
| `IngredientPickup` | InteractiblePickup | World pickup that adds 1 ingredient to Vault |
| `IngredientPickupFactory` | IFactory | Creates `IngredientPickup` entities |
| `IngredientBarItem` | MonoBehaviour | HUD icon+count for one ingredient |
| `IngredientsBar` | MonoBehaviour | HUD bar of Vault ingredients |
| `Consumable` | abstract ScriptableObject | Base usable item with `Use(Ship)` |
| `WeaponBasedConsumable` | Consumable | Fires a weapon once on use |
| `SpawnMinionConsumable` | Consumable | Spawns an owned minion on use |
| `SpawnPrefabConsumable` | Consumable | Instantiates a prefab on use |
| `ConsumableRegistry` | ScriptableObjectRegistry | Registry of all consumables |
| `ConsumablePickup` | InteractiblePickup | World pickup that adds 1 consumable to Vault |
| `ConsumablePickupFactory` | IFactory | Creates `ConsumablePickup` entities |
| `ConsumableInfoPopup` | InfoPopup | Tooltip for a consumable |
| `ConsumableShopItem` | plain class | Shop entry (consumable + price + price increment) |
| `ConsumableShopItemWidget` | MonoBehaviour | Shop item UI widget |
| `ConsumablesShopWidget` | MonoBehaviour | Station consumable shop (buy logic) |
| `ConsumablesScreen` | ShipMenuTab | Ship-menu tab for consumables + shop |
| `ConsumableWheel` | MonoBehaviour | In-game radial consumable selector |
| `ConsumableWheelItem` | MonoBehaviour | One slot in the consumable wheel |
| `Pickup` | abstract MonoBehaviour | Base magnet-collected pickup |
| `InteractiblePickup<TData>` | abstract SavableComponent | Base interaction/fly-to-ship pickup |
| `InteractionPrompt` | MonoBehaviour | "Use" prompt shown while an interactable is hovered |
| `Interactable` | MonoBehaviour | Hoverable/activatable world object |
| `Interactor` | MonoBehaviour | On the ship; tracks & activates interactables |
| `Grabbable` | MonoBehaviour | Marks a rigidbody as hookable |
| `Hook` | MonoBehaviour | Grappling tether physics |
| `HookTargetSeeker` | ComponentScanner<Grabbable> | Picks the closest in-cone grabbable |
| `HookVisuals` | MonoBehaviour | Line renderer for the hook |
| `LootCollector` | MonoBehaviour | Unit's magnet that attracts `Pickup`s |
| `LootDropper` | MonoBehaviour | Drops loot from a drop table or single item |
| `LootDropper.IDroppedLoot` | interface | Lets a dropped item override drop force |
| `LootFactory` | IFactory | Instantiates the correct pickup per `DroppabbleType` |
| `LootSelector` | IInitializable | Rolls a `DropTable` with a seed |
| `DropTable` | ScriptableObject | List of `DropTableItem` rolls |
| `DropTableItem` | struct | One roll: count source + item/group |
| `DropTableItemCountSource` | enum | `MinMax` / `Probability` |
| `DropTableWeightedGroup` | ScriptableObject | Weighted random group of droppables |
| `DroppabbleItem` | struct | A single droppable (typed union) |
| `DroppabbleType` | enum | `Prefab`/`Module`/`Ingedient`/`Consumable` |

## Classes

### Resource: ScriptableObject; Implements `IIdentifiable<string>`
Purpose: defines a resource/currency/health type and all its bar/explosion visuals.
- Key fields: `id`, `icon`, `color` (`ColorAsset`), `shieldColor`, `bool isShared`, three bar-unit prefabs (`resourceBarUnitPrefab`/`Compact`/`Micro`), three bar textures, `resourceBarUnitColorFull/Empty/Highlight`, `explosionBasePrefab`, `explosionAddonPrefab`, `explosionLightColor`, `int lowTreshold = -1`, `orderInHud`, `maxUnitPerRowInHud`, **`float rechargeDelay`**, `tmpSpriteId`, `List<DamageModifier> damageModifiers`.
- Key methods: `string Id`, `string SpriteTag` (`<sprite name=…>`), `float GetDamageMultiplierOn(Resource)`.
- Nested: `struct DamageModifier { Resource resource; float damageMultiplier; }`.

### ResourceTank: plain C# class
Purpose: runtime amount of one resource on one unit. **Not** a MonoBehaviour despite `[RequireComponent(typeof(ResourceTank))]` on `AutoRecharge` (note: that attribute appears in decompiled `AutoRecharge` but `ResourceTank` is a plain class — `AutoRecharge.Start` does `GetComponent<ResourceTank>()`, implying a separate MonoBehaviour wrapper context exists for that path).
- Key fields: `Resource resource`, `bool isInfinite`.
- Key properties: `float Capacity`, `float Value` (setter clamps for infinite tanks: refuses to go below current when `isInfinite`; fires `ValueChanged` and updates `IsLow` vs `resource.lowTreshold`), `bool IsLow`, `bool IsEmpty` (`Value <= 0`), `bool IsFull` (`Value >= Capacity`), `bool IsFullRounded` (`Ceil(Value) == Capacity`).
- Key methods: `void Charge(float amount)` (clamps 0..Capacity), `void TriggerResourceInsufficient()`.
- Events: `Action<ResourceTank,float,float> ValueChanged` (tank, old, new), `Action<ResourceTank,bool> LowChanged`, `Action ResourceInsufficient`.

### ResourceRecharger: plain C# class
Purpose: per-resource auto-recharge attached inside `Unit.Data`.
- Constructor: `ResourceRecharger(Unit.Data unit, Resource resource)`.
- Key fields/props: `float RechargeRate { get; set; }`, `bool IsRecharging`.
- Key methods: `void Update()` — records `lastDecreaseTime` when value drops, sets `isRecharging` when `Time.time > lastDecreaseTime + resource.rechargeDelay && !tank.IsFull`, then `tank.Charge(RechargeRate * deltaTime)`. Driven from `Unit.Data.Update()` which iterates all rechargers.

### AutoRecharge: MonoBehaviour
Purpose: standalone recharge for a `ResourceTank` (non-Unit path). `[RequireComponent(typeof(ResourceTank))]`.
- Key fields: `float rechargeSpeed`, `float delay`.
- `Update()`: on value drop resets `lastDecreaseTime`; else after `delay` charges `rechargeSpeed * deltaTime`.

### ResourceAutoChargeEffect: ModuleEffect; Implements `IHasDescriptionForUnit`
Purpose: module effect that boosts a resource's recharge rate by level.
- Key fields: `Resource resource`, `FloatSeries rechargeRate` (per-level).
- Key methods: `OnRecalculateUnitStats(Unit.Data)` → `unit.IncreaseRechargeRate(resource, RechargeRateForCurrentLevel)`; `Clone()`; `GetPropertyList(...)`.

### ModifyResourceCapacity: ModuleEffect; Implements `IHasDescriptionForUnit`
Purpose: module effect that increases a resource's capacity by level.
- Key fields: `Resource resource`, `FloatSeries delta`.
- Key methods: `OnRecalculateUnitStats(Unit.Data)` → `owner.IncreaseCapacity(resource, DeltaForCurrentLevel)`.

### DrainResourceEffect: ModuleEffect; Implements `IHasDescriptionForUnit`
Purpose: module effect that continuously drains a resource.
- Key fields: `Resource resource`, `FloatSeries drainRate`.
- Key methods: `OnUpdate(Unit.Data)` → `unit.GetTank(resource).Value -= DrainRateForCurrentLevel * Time.deltaTime`.

### ResourceUnit: MonoBehaviour
Purpose: one bar segment sprite (empty/full/highlight) with optional animator.
- Key methods: `Setup(Resource)`, `SetFull(bool)`, `Blink()`; prop `TimeSinceLastChange`.

### ResourceBar: MonoBehaviour
Purpose: binds to a `ResourceTank` and renders it as rows.
- Key fields: `DisplayMode displayMode` (`Regular`/`Compact`/`Micro`), `warningBlinkDelay`, `firstRow`, `const float ppu = 20f`.
- Key methods: `Assign(ResourceTank)` / `Unassign()`, `Display(float)`, `BlinkIfLow()`, `bool CheckCapacityChanged()` (rebuilds rows when capacity changes), prop `int MaxResourcePerRow`. Subscribes to `tank.ResourceInsufficient` → triggers `"NoResource"` animator state.

### ResourceBarRow: MonoBehaviour
Purpose: shader-driven row of segments. `Init(Resource, DisplayMode)`, `HandleCapacityChange(offset, unitCount, value)`, `UpdateValue(float)`, `Blink()`. Uses material floats `_SegmentCount`, `_Value`, `_MaskRange`.

### ResourceDispenser: MonoBehaviour
Purpose: trigger volume that spawns `ResourcePickup`s for non-full units in range.
- Key fields: `ResourcePickup pickupPrefab`, `float dispenseFrequency`, `Transform spawnPoint`. Tracks units via `LootCollector` on `OnTriggerEnter/Exit2D`; in `Update` instantiates a pickup when a unit `HasTank(Resource)` and tank is not `IsFullRounded`.

### ResourcePickup: Pickup
Purpose: magnet pickup that charges a tank.
- Key fields: `Resource resource`, `float amount`, `string pickupSfx`.
- Overrides: `CanBePickUpBy(unit)` = `unit.HasTank(resource)`; `OnPickedUp(unit)` charges the tank and plays SFX; `CompareCollectors(...)` prioritises units missing/most-needing the resource, then by distance.

### UnitResourceRechargeIndicator: MonoBehaviour
Purpose: plays particles + looping SFX while `unit.ComponentData.IsResourceRecharging(resource)`. Fields: `Unit unit`, `Resource resource`, `ParticleSystem particle`, `string rechargeSfx`.

### SharedResourceDisplayText: MonoBehaviour
Purpose: binds a TMP text to a shared resource on `gameController.Ships[0]`. Updates on `ResourceTank.ValueChanged`. Fields: `GameController gameController`, `Resource resource`, `TMP_Text text`, `string format`.

### DestroyWhenResourceDrained: MonoBehaviour
Purpose: destroys the unit (optionally spawning `spawnOnDeath`) when `unit.GetResource(resource) <= 0`. Fields: `Unit unit`, `Resource resource`, `GameObject spawnOnDeath`.

### DamagableResource: HealthBase; Implements `IEntityBindingListener`
Purpose: makes a unit's `ResourceTank` act as health, routing damage/shields/death.
- Key fields: `Resource resource`, `DamageConditions damageConditions`, `GameObject spawnOnDeath`, `float iFrameDuration`, `bool hasCorpseState`, `CorpseSettings corpseSettings`, `bool destroyOnDeath = true`, `Shield shieldPrefab`, `float firstShieldSize`, `bool shieldHasCollider`, `List<GameObject> damageBlockers`, events `onShieldDamaged`/`onGotAttacked` (UnityEvents).
- Key props: `ResourceTank Tank => Unit.GetTank(resource)`, **`CurrentHealth`** get=`Unit.GetResource(resource)` set=`tank.Value`, **`MaxHealth`** =`tank.Capacity`, `bool IsInvincible`, `bool IsDead`, `IReadOnlyList<Shield> Shields`.
- Key methods: `TakeDamage(IReadOnlyList<Damage>)`, `TakeDamage(Damage)`, `void Damage(float amount)` (respects `IsInvincible`, iFrames, corpse state), `Die()`, `GetDamageAmount(Damage)`, `Bind/Unbind(EntityData)`. Burn damage ticks each `Update` while `IsOnFire`.
- Nested: `struct CorpseSettings { GameObject ai; int health; Animator animator; string animParamName; GameObject deathParticle; GameObject overkillDeathParticle; }`.

### Ingredient: ScriptableObject; Implements `IIdentifiable<string>`
- Fields: `string id`, `Sprite iconBig`, `Sprite iconSmall`, `string displayName`. Prop `string Id`.

### IngredientPickup: InteractiblePickup<IngredientPickup.Data>
Purpose: interactive world pickup that stores an ingredient in the Vault.
- Prop `Ingredient Ingredient` (setter updates visuals). `OnPickedUp(Ship)` → logs to ship + `Vault.Add(ingredient, 1)`.
- Nested `Data : ComponentData, IMementoOriginator<Memento>` holds `Ingredient ingredient`, serialised via `Memento.ingredientId` (restored from `IngredientRegistry`).

### IngredientPickupFactory: `IFactory<Ingredient, Vector2, IngredientPickup>`, `IInitializable`
- `IngredientPickup Create(Ingredient, Vector2)` via `EntityGameObjectManager.CreateEntity(ingredientPickupPrefab, position)`.

### IngredientsBar / IngredientBarItem: MonoBehaviour
HUD list of Vault ingredients. `IngredientsBar` subscribes to `Vault.IngredientAmountChanged`; `IngredientBarItem` has `SetIngredient(Ingredient)`, `SetAmount(int)`, `PlayHideAnimation()`.

### Consumable: abstract ScriptableObject; Implements `IIdentifiable<string>`
- Fields: `string id`, `Sprite icon`, `string displayName`, `string description` (`[TextArea]`), `int maxCount`. Prop `string Id`.
- **`public abstract void Use(Ship ship);`** ← the core extension point for new consumables.

### WeaponBasedConsumable: Consumable
`Use(Ship)` creates a weapon from `WeaponData` via `WeaponFactory` and calls `weaponBase.DoShoot(new FakeBarrel(ship.position, Vector2.up))`. Field: `WeaponData weaponData`.

### SpawnMinionConsumable: Consumable
`Use(Ship)` spawns `Unit minionPrefab` via `EntityGameObjectManager`, sets the ship as owner (`Unit.OwnerConnectionType.Undefined`). Field: `Unit minionPrefab`.

### SpawnPrefabConsumable: Consumable
`Use(Ship)` = `Object.Instantiate(prefab, ship.position, identity)`. Field: `GameObject prefab`.

### ConsumablePickup: InteractiblePickup<ConsumablePickup.Data>
Mirror of `IngredientPickup` for consumables. `OnPickedUp(Ship)` → `Vault.Add(consumable, 1)`. Prop `Consumable Consumable`. Nested `Data` serialises `consumableId` (restored from `ConsumableRegistry`).

### ConsumablePickupFactory: `IFactory<Consumable, Vector2, ConsumablePickup>`, `IInitializable`
`ConsumablePickup Create(Consumable, Vector2)`.

### ConsumableShopItem: `IMementoOriginator<Memento>`
Purpose: a shop entry. Fields: `Consumable consumable`, `List<Price> price`, `List<Price> priceIncrement`. `static CreateNew(Consumable, ShopItemConfig)`; `void IncreasePrice(float incrementMultiplier)` (adds increments per currency).

### ConsumablesShopWidget: MonoBehaviour (`IPointerClickHandler`)
Purpose: station shop buy logic for consumables. Reads `RunData.ConsumableShopItems`. `TryBuy(ConsumableShopItem)`:
- `CanPurchase` returns true immediately if **`runData.AllShopItemsAreFree`**, else checks every `Price.CanAfford(unit, vault)`.
- On success (unless `AllShopItemsAreFree`): removes ingredients from Vault or subtracts `unit.GetTank(resource).Value`; then `Vault.Add(consumable, 1)` and `shopItem.IncreasePrice(...)` (coop uses `coopPriceIncrementMultiplayer`).
Event `Action<ConsumableShopItemWidget> ItemSelected`.

### ConsumablesScreen: ShipMenuTab
Ship-menu tab; hosts `ConsumablesShopWidget` (when at a `Station`) and a static consumable wheel preview. Subscribes to `Vault.ConsumableAmountChanged`.

### ConsumableWheel / ConsumableWheelItem: MonoBehaviour
In-game radial selector. On `GameController.GameStarted` builds one `ConsumableWheelItem` per `vault.ConsumablesCount` slot; hooks `ShipControlActionMap.OpenItemWheel`. `Open()` slows time via `TimeManager` modifier and enables `ItemWheelActionMap`. `Close()`: if a non-empty selected slot has amount > 0, `vault.Remove(consumable, 1)` then `consumable.Use(ship)`. `ConsumableWheelItem.Show(Vault.ConsumableWithAmount)` updates icon and `amount/maxCount` text.

### ConsumableInfoPopup: InfoPopup
`Show(Consumable, RectTransform)` populates a tooltip (icon, name, description; hides level fields).

### Pickup: abstract MonoBehaviour
Purpose: base magnet-collected pickup.
- Fields: `Rigidbody2D rigidbody`, `float pickupDelay`. Prop `float Age`.
- `FixedUpdate` after `pickupDelay`: picks the preferred `LootCollector` (sorted by `CompareCollectors`), moves toward it at `LootCollector.GetMagnetSpeed(this)`, and when within `pickupDistance` calls `GetPickedUp(owner)`.
- Abstracts to override: **`bool CanBePickUpBy(Unit)`**, **`void OnPickedUp(Unit)`**, **`int CompareCollectors(LootCollector, LootCollector)`**.

### InteractiblePickup<TData>: abstract SavableComponent<TData>; Implements `LootDropper.IDroppedLoot`
Purpose: pickup that requires Use interaction, then flies to the ship.
- Fields: `Interactable interactable`, `Rigidbody2D rigidbody2D`, `float pickupMoveSpeed`, `string pickupSfx`, `GameObject pickupEffectPrefab`, `bool overrideDropForce`, `MinMaxFloat dropForce`, `float dropAngle`.
- `OnInteractedPickup(Interactor)` sets the target ship and disables the interactable; `Update` moves to the ship and calls `OnPickedUp(Ship)` then destroys. Abstracts: `OnPickedUp(Ship)`, `UpdateVisuals()`. `OnDropped()` applies drop force when `overrideDropForce`.

### Interactable / Interactor / InteractionPrompt: MonoBehaviour
- `Interactable`: `OnHoverStateChanged(bool)`, `OnInteracted(Interactor)` UnityEvents; `hoveringInteractors` set; `OnHoverEntered/Exited`, `Activate(Interactor)`.
- `Interactor` (on ship): tracks `InteractablesInRange`, picks nearest as `hoveredInteractable`, and on `ShipControlActionMap.UseActivated` calls `hoveredInteractable.Activate(this)`. Actions `UseActivated`, `HoveredInteractablesChanged`.
- `InteractionPrompt`: shows/hides the Use prompt and switches button hints by control scheme.

### Grabbable / Hook / HookTargetSeeker / HookVisuals
- `Grabbable`: `[RequireComponent(Rigidbody2D)]`, exposes `Rigidbody`.
- `Hook`: tether physics. Fields `shipRigidbody`, `length`, `stiffness`, `maxVelocityTowardsShip`, `shipForceMultiplier`. `Activate()` toggles attach/detach to `targetSeeker.SelectedTarget`. `FixedUpdate` applies spring force.
- `HookTargetSeeker : ComponentScanner<Grabbable>`: `Scan()` + closest-in-cone (`maxAngle`) selection.
- `HookVisuals`: line renderer between hook and attached rigidbody.

### LootCollector: MonoBehaviour
Purpose: a unit's magnet for `Pickup`s. Fields: `Unit owner`, `float collectionForce`, `float pickupDistance`, `AnimationCurve pickupSpeedByDistance`. `float GetMagnetSpeed(Pickup)` = `curve.Evaluate(distance/magnetRange) * collectionForce` (`magnetRange` from the `CircleCollider2D` radius).

### LootDropper: MonoBehaviour
Purpose: drops loot when `DropLoot()` is called.
- Fields: `LootSelectionMethod lootSelectionMethod` (`DropTable`/`Single`), `DropTable dropTable`, `DroppabbleItem loot`, `float dropForce`, `float dropAngle`, `Vector2 spawnOffset`.
- `DropLoot()`: `Single` → drops `loot`; `DropTable` → uses `ISeedProvider.GetSeed()` (or random) and `lootSelector.SelectLoot(dropTable, seed)`. `Drop(item)` creates via `LootFactory` and applies drop force (unless the dropped item's `IDroppedLoot.OverridesDropForce`).
- Nested: `interface IDroppedLoot { bool OverridesDropForce; void OnDropped(); }`, `enum LootSelectionMethod { DropTable, Single }`.

### LootFactory: `IFactory<DroppabbleItem, Vector2, GameObject>`, `IInitializable`
`GameObject Create(DroppabbleItem, Vector2)` switches on `item.droppableType`: Module → `ModulePickupFactory` + `runData.RegisterModuleDropped(module)`; Ingredient → `IngredientPickupFactory`; Consumable → `ConsumablePickupFactory`; Prefab → entity or plain instantiate.

### LootSelector: `IInitializable`
`IEnumerable<DroppabbleItem> SelectLoot(DropTable, int seed)` → `dropTable.GetRandomItems(new Rnd(seed), runData)`.

### DropTable: ScriptableObject
`List<DropTableItem> items`. `List<DroppabbleItem> GetRandomItems(Rnd, RunData)` concatenates each item's rolls.

### DropTableItem: struct
One roll. Fields: `DropTableItemCountSource countSource`, `float probability` (when Probability), `MinMaxInt count` (when MinMax), `bool useGroup`, `DropTableWeightedGroup group`, `DroppabbleItem item`. `GetDroppedItems(Rnd, RunData)`: rolls a count (`probability` → 0/1, or `count.RandomInRange`) then adds the single item or a `group.GetRandomItem`.

### DropTableWeightedGroup: ScriptableObject
Weighted random group. `DroppabbleItem GetRandomItem(Rnd, RunData)` via nested `DroppabbleItemDistribution` whose `GetWeight` multiplies a module's weight by `ModuleData.repeatedDropChanceMultiplyer` for each already-`DroppedModules` match (anti-duplicate weighting).

### DroppabbleItem: struct
Typed union. Fields: `DroppabbleType droppableType`, `GameObject prefab`, `ModuleData module`, `Ingredient ingredient`, `Consumable consumable` (each shown conditionally on the type).

### Enums
- `Resource`/bars: `ResourceBar.DisplayMode { Regular, Compact, Micro }`.
- `Price.CurrencyType { Ingredient, Resource }`.
- `LootDropper.LootSelectionMethod { DropTable, Single }`.
- `DropTableItemCountSource { MinMax, Probability }`.
- `DroppabbleType { Prefab, Module, Ingedient, Consumable }` (note the misspelling `Ingedient` in source).

### Supporting (referenced, documented elsewhere)
- `Vault`: player inventory. Resource-economy-relevant API: `Add/Remove(Ingredient,int)`, `AmountOf(Ingredient)`, `Add/Remove(Consumable,int)`, `GetAmount(Consumable)`, `GetConsumableAt(int)`, `ConsumablesCount` (**8 fixed slots**), events `ConsumableAmountChanged(int)`, `IngredientAmountChanged(Ingredient,int,int)`. Nested `ConsumableWithAmount { Consumable consumable; int amount; }`.
- `Price`: struct with `CurrencyType currencyType`, `Ingredient ingredient`, `Resource resource`, `float amount`, `int AmountFloored`, `bool CanAfford(Unit, Vault)`.
- `Unit.Data` (a.k.a. `ComponentData`): `HasTank`, `GetTank`, `GetResource`, `InstallNewTank`, `IncreaseCapacity`, `GetRechargeRate`, `IncreaseRechargeRate`, `IsResourceRecharging`, `RefillResources`, `GetAllTanks`/`GetNotSharedTanks`/`GetSharedTanks`, `bool HasInfiniteResource`.

## Modding Notes

PUNK is moddable via **BepInEx + HarmonyX**. Below are concrete Harmony targets and fields for common cheats/tweaks. All method signatures are from the decompiled source above.

### Infinite resources / energy
The cleanest switch already exists in the game:
- **`Unit.Data.HasInfiniteResource` (setter)** — set to `true` to mark every tank `isInfinite`. While infinite, `ResourceTank.Value`'s setter refuses to lower the value (`if (!isInfinite || !(_value > value))`), so the tank never drains. Set it on the player ship's `Unit.ComponentData` after spawn (e.g. patch `Unit.CreateData` postfix, or `GameController.GameStarted`).
- Alternatively, Harmony-**prefix `ResourceTank` `Value` setter** to clamp the incoming value to `Capacity` (or skip decreases) for chosen resources. Because `Value` is a property, target `set_Value`.
- For a softer version, prefix **`DamagableResource.Damage(float)`** and return `false` (skip) to make the player unkillable while still letting non-health resources drain. `DamagableResource.IsInvincible = true` already forces health back to `1` when it would hit 0.
- To top up on demand, call **`Unit.Data.RefillResources()`** or `tank.Charge(amount)`.

### Free / auto-recharge
- **Auto-recharge rate**: patch **`ResourceRecharger.Update()`** (postfix) or set `RechargeRate` high via **`Unit.Data.IncreaseRechargeRate(Resource, delta)`**. The recharge gate uses `Resource.rechargeDelay`; set that field to `0` on the `Resource` ScriptableObject for instant recharge.
- **Force always-recharging**: prefix `ResourceRecharger.Update` to call `tank.Charge(bigNumber * deltaTime)` unconditionally, or postfix `Unit.Data.IsResourceRecharging` to return `true`.
- `ResourceAutoChargeEffect` / `ModifyResourceCapacity` / `DrainResourceEffect` read per-level values from `FloatSeries`; patch their `OnRecalculateUnitStats` / `OnUpdate` to scale `RechargeRateForCurrentLevel` / `DeltaForCurrentLevel` / disable drain.
- `AutoRecharge` (standalone) — fields `rechargeSpeed`, `delay`; raise speed / zero delay.

### Free shopping
- **`RunData.AllShopItemsAreFree`** (`{ get; set; }`) — set `true` to make every consumable purchase free; `ConsumablesShopWidget.CanPurchase`/`TryBuy` both short-circuit on it. (Likely shared with the module shop.)
- Or prefix **`ConsumablesShopWidget.CanPurchase`** → return `true`, and prefix the price-deduction loop in `TryBuy` to skip.
- To change price scaling, patch **`ConsumableShopItem.IncreasePrice(float)`**.

### Guaranteed loot
- **`DropTableItem.GetDroppedItems` / `ProbabilityItemCount`**: prefix to force a non-zero count (e.g. always return `1`+). For probability rolls, `ProbabilityItemCount` returns 1 when `rnd.Range(0,1) < probability`; raise `probability` or patch to always return 1.
- **`LootDropper.DropLoot`**: postfix to additionally `Drop(...)` a chosen `DroppabbleItem`, or prefix to swap `lootSelectionMethod`/`loot`.
- **`DropTableWeightedGroup.DroppabbleItemDistribution.GetWeight`**: patch to ignore `repeatedDropChanceMultiplyer` (so already-dropped modules keep full weight), or to bias toward a target item.
- To guarantee a specific module, intercept **`LootFactory.Create`** and substitute the `DroppabbleItem`.

### Drop-rate multipliers
- Global multiplier: prefix **`DropTableItem.GetDroppedItems`** and multiply the rolled count, or postfix `DropTable.GetRandomItems` to duplicate/scale the returned `List<DroppabbleItem>`.
- The roll is **seeded** (`LootSelector.SelectLoot(dropTable, seed)` builds `new Rnd(seed)` from `ISeedProvider.GetSeed()`), so for deterministic-but-boosted drops patch the count/weight math rather than the RNG.
- Module duplicate suppression strength lives in **`ModuleData.repeatedDropChanceMultiplyer`** and `RunData.DroppedModules` / `RunData.RegisterModuleDropped` — patch `RegisterModuleDropped` to a no-op to disable de-duplication.

### Pickup / magnet tweaks
- **`LootCollector`** fields `collectionForce`, `pickupDistance`, `pickupSpeedByDistance`, plus the `CircleCollider2D` radius (`magnetRange`) control magnet strength/range. Patch `GetMagnetSpeed` to return a large constant for instant vacuuming.
- **`Pickup.FixedUpdate`** / `pickupDelay` gate when collection starts; `CanBePickUpBy` decides eligibility (e.g. `ResourcePickup` requires `unit.HasTank(resource)`).
- For interaction pickups, the flow is `Interactor.OnUseActivated` → `Interactable.Activate` → `InteractiblePickup.OnInteractedPickup` → fly-to-ship → `OnPickedUp(Ship)`; patch any stage to auto-collect.

### Adding new consumables
Subclass **`Consumable`** (override `Use(Ship)`) — follow `SpawnPrefabConsumable`/`SpawnMinionConsumable`/`WeaponBasedConsumable`. Register the ScriptableObject in **`ConsumableRegistry`** (so `Memento`/save-load by `id` works) and ensure it appears in `RunData.ConsumableShopItems` (built from `ShopUpgradeData.fixStarterConsumables`) if you want it purchasable.
