# Weapons, Barrels & Projectiles
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

A "shot" in PUNK flows through several decoupled layers. Understanding the chain is the key to patching anything:

1. **Authoring data (`WeaponData` ScriptableObject).** Every weapon is defined by a `WeaponData` asset (a `ScriptableObject` subclass). It holds all the serialized stats: `damage`, `fireRate`, `projectileCount`, `spread`, `cost`, etc. Four concrete subclasses exist: `ProjectileWeaponData`, `PhysicsWeaponData`, `HitscanWeaponData`, `MinionSpawnerWeaponData`.

2. **Runtime weapon (`WeaponBase`).** `WeaponFactory.Create(WeaponData, modules)` instantiates the matching runtime `WeaponBase` (`ProjectileWeapon`, `PhysicsWeapon`, `HitscanWeapon`, `MinionSpawnerWeapon`). The constructor **copies** the data fields into mutable properties (so runtime stats can diverge from the asset). The factory then applies any `IWeaponModifier` effects from the equipped modules (this is how upgrades change `FireRate`, `Damage`, `ProjectileCount`, etc.), and attaches a sub-emitter weapon if `subEmitter` is set.

3. **Holder (`WeaponHolder`).** A `WeaponHolder` MonoBehaviour owns the current `WeaponBase` and raises `WeaponChanged`. `StaticWeaponHolder` builds one fixed weapon from an inspector asset; `ModuleSlotWeaponHolder` rebuilds the weapon whenever the ship's module grid cluster (primary/secondary weapon slot) refreshes, feeding the cluster's connected & powered modules into the factory.

4. **Shooter + Barrel.** `Shooter` (MonoBehaviour) drives firing each frame: it tracks fire-rate cooldown, resource cost, warmup, burst sound, and calls `weapon.Fire(barrel)`. The `IBarrel` supplies the firing `Position` and `Direction`. `BarrelTransform` is the concrete in-world barrel; `AssistedBarrel` wraps one and bends the direction toward an aim-assist target; `FakeBarrel` is a plain struct-like object used for code-spawned shots (sub-emitters, consumables, active modules).

5. **Fire pattern (`WeaponBase.Fire` / `DoShoot` / `GetDirections`).** `Fire` runs the burst loop (`BurstSize` shots, `BurstDelay` apart). Each `DoShoot` calls `GetDirections` to fan out `ProjectileCount` directions across `Spread`, jittered by `AngleVariance` and biased by `AngleOffset`, then calls the abstract `FireSingle` per direction.

6. **`FireSingle` (per weapon type).** This is where the weapon type matters:
   - `ProjectileWeapon` instantiates a `Projectile` (custom raycast mover) or `PhysicsProjectile` (Rigidbody2D + homing), copies all the projectile-modifier data structs onto it, applies per-projectile augmentations (`IProjectileModifier`, which can cost resources), then calls `projectile.Shoot()`.
   - `PhysicsWeapon` spawns a bare `Rigidbody2D` grenade and sets its velocity.
   - `HitscanWeapon` does an immediate `Physics2D.CircleCast` and notifies `IHitscanWeaponListener`s on the hit collider.
   - `MinionSpawnerWeapon` spawns a `Unit` entity and launches it.

7. **Projectile flight & impact.** `Projectile` integrates its own movement in `FixedUpdate` via `CircleCast`, handling range slowdown, movement noise, piercing, bouncing, and impact. On hit it notifies `IProjectileListener`s and runs `ProjectileImpactBehaviour` (destroy effect, explosion, sub-fire, electric discharge). `PhysicsProjectile` instead uses Unity physics (`OnCollisionEnter2D`/`OnTriggerEnter2D`) plus a homing controller.

**Modifiers vs. augmentations.** Two upgrade mechanisms exist:
- `IWeaponModifier.Modify(WeaponBase)` runs **once at weapon creation** and mutates the weapon's stat properties. `ModifyWeaponProperty` is the data-driven implementation (add/multiply any of 12 `TargetProperty` values). `WeaponAugmentation` also implements this, registering itself into `weapon.augmentations`.
- `WeaponAugmentation` objects live on the weapon and are consulted **at fire time**: `IProjectileModifier` augmentations mutate each spawned projectile (and can charge a per-projectile resource cost via `IHasPerProjectileCost`); other augmentation subclasses (e.g. burn/explosion/discharge adders, referenced in `WeaponBase`) add bonus damage shown in the property list.

## Class Index

| Class | Kind | Summary |
|---|---|---|
| `IWeapon` | interface | Single member: `Fire(IBarrel)`. |
| `WeaponBase` | abstract class | Runtime weapon base; fire loop, fan-out, all shared stats, augmentations. |
| `WeaponData` | abstract ScriptableObject | Serialized weapon definition (damage, fire rate, spread, cost...). |
| `WeaponFactory` | class | Builds the right `WeaponBase` from `WeaponData` and applies module modifiers. |
| `ProjectileWeapon` | class | Fires `Projectile`/`PhysicsProjectile` instances. |
| `ProjectileWeaponData` | ScriptableObject | Data for projectile weapons (prefabs + all projectile data structs). |
| `PhysicsWeapon` | class | Fires a bare `Rigidbody2D` grenade. |
| `PhysicsWeaponData` | ScriptableObject | Data for physics weapons. |
| `HitscanWeapon` | class | Instant `CircleCast` beam weapon. |
| `HitscanWeaponData` | ScriptableObject | Data for hitscan weapons (range, rayWidth, repeat delay). |
| `HitscanWeaponVisual` | MonoBehaviour | Beam/impact sprite + particle visual for hitscan. |
| `IHitscanWeaponListener` | interface | Receives `OnHitByHitscanWeapon`. |
| `MinionSpawnerWeapon` | class | Spawns a `Unit` minion as the "projectile". |
| `MinionSpawnerWeaponData` | ScriptableObject | Data for minion spawner weapons. |
| `WeaponHolder` | abstract MonoBehaviour | Owns current `WeaponBase`, raises `WeaponChanged`. |
| `StaticWeaponHolder` | MonoBehaviour | Holder built from a single inspector asset. |
| `ModuleSlotWeaponHolder` | MonoBehaviour | Holder driven by ship module-grid weapon cluster. |
| `WeaponRegistry` | ScriptableObject | Registry of all `WeaponData` by id. |
| `WeaponModule` / `WeaponModuleData` | class / ScriptableObject | Grid module that carries a `WeaponData`. |
| `WeaponBasedActiveModule` / `...Data` | class / ScriptableObject | Active (triggered) module that fires a weapon. |
| `WeaponBasedConsumable` | ScriptableObject | Consumable that fires a one-shot weapon. |
| `WeaponDropdown` | MonoBehaviour | Debug UI to swap the ship's primary weapon. |
| `WeaponAugmentation` | abstract class | `ModuleEffect` that registers itself onto a weapon. |
| `IWeaponModifier` | interface | `Modify(WeaponBase)` — applied at creation. |
| `ModifyWeaponProperty` | class (`ModuleEffect`) | Data-driven add/multiply of a weapon stat. |
| `IProjectileModifier` | interface | `ModifyProjectile(IProjectile)` (+ per-projectile cost). |
| `IHasPerProjectileCost` | interface | `CostPerProjectile` / `CostResource`. |
| `IHasRangeProperty` / `IHasSpeedProperty` | interface | Expose mutable `Range` / `Speed`. |
| `IHasDescriptionForWeapon` | interface | UI description hook for weapon effects. |
| `Shooter` | MonoBehaviour | Per-frame firing driver (cooldown, cost, warmup, audio). |
| `ShooterBlocker` | MonoBehaviour | Blocks listed shooters while enabled. |
| `AutoShoot` | MonoBehaviour | Holds a shooter's trigger while enabled. |
| `IBarrel` | interface | `Position`, `Direction`, `DirectionChanged`. |
| `BarrelTransform` | MonoBehaviour | Transform-based barrel. |
| `AssistedBarrel` | MonoBehaviour | Barrel that snaps direction to an aim-assist target. |
| `FakeBarrel` | class | Lightweight code-only barrel. |
| `Aimer` | MonoBehaviour | Rotates a barrel toward a target with `IAimEffect`s. |
| `AimVisual` | MonoBehaviour | Draws an aim line from a shooter's barrel. |
| `Crosshair` | MonoBehaviour | Sprite placed at the aimer's target. |
| `AimAssist` | MonoBehaviour | Scans for `AimAssistTarget`s, picks closest in cone. |
| `AimAssistData` | struct | `enabled`, `maxAngle`, `isPredictive`. |
| `AimAssistTarget` | MonoBehaviour | Marks an aim-assist target; exposes `Velocity`. |
| `Projectile` | MonoBehaviour | Custom-integrated raycast projectile. |
| `PhysicsProjectile` | MonoBehaviour | Rigidbody2D + homing projectile. |
| `IProjectile` | interface | Common projectile contract. |
| `IProjectileListener` | interface | Receives `ProjectileCollided`. |
| `ProjectileDispenser` | MonoBehaviour | Fires a weapon once on `Start`. |
| `ProjectileLifetime` | MonoBehaviour | Standalone timed self-destruct. |
| `ProjectileRotation` | MonoBehaviour | Adds spin torque on spawn. |
| `ProjectileVelocityInfluencer` | MonoBehaviour | Adds owner velocity to spawned projectiles. |
| `ProjectilePassThrough` | MonoBehaviour | Marker: projectiles pass through this collider. |
| `ProjectileMath` | static class | Ballistic launch-angle/arc helpers. |
| `ProjectileImpactBehaviour` | struct | Impact reaction config. |
| `ProjectileRangeData` | struct | Max-range / slowdown config. |
| `ProjectileLifetimeData` | struct | Timed-destruct config. |
| `ProjectileRotationData` | struct | Orientation-alignment config. |
| `PiercingData` | struct | Pierce + repeat-damage config. |
| `ProjectileBounceData` | class | Bounce off layers config. |
| `ProjectileHomingData` | struct | Homing/torque config (PhysicsProjectile). |
| `ProjectileElectricityData` | struct | Electric conductor config. |
| `ProjectileMovementNoiseData` | struct | Perlin wobble config. |
| `VelocityInfluenceData` | struct | Inherit-owner-velocity config. |

## Classes

### IWeapon
- Kind: interface.
- Purpose: Minimal weapon contract used by holders/shooters.
- Key methods: `UniTaskVoid Fire(IBarrel barrel)` — start a (possibly bursted, async) shot.

### WeaponBase
- Kind: abstract class. Implements `IWeapon`, `IDisposable`, `IPropertyListOwner`.
- Purpose: Runtime weapon. Owns all mutable shared stats (copied from `WeaponData` in ctor), the burst fire loop, the spread/fan-out math, augmentation list, and muzzle particles.
- Key fields/properties (all `{ get; set; }` unless noted — these are the live, patchable values):
  - `TemplateData : WeaponData` — the source asset.
  - `Damage : Damage` — contact damage (`amount` + `damageType`).
  - `Explosion : Explosion`, `DischargeData : DischargeData`, `CellConvertData : CellConvertData`, `Burn : MinMaxFloat` — secondary effect payloads.
  - `FireRate : float` — shots per second (cooldown is `1f / FireRate`).
  - `WarmupTime : float` — seconds of held-trigger before firing starts.
  - `BurstSize : int`, `BurstDelay : float` — shots per pull and delay between them.
  - `ProjectileCount : float` — number of directions per shot (note: **float**, compared against counts).
  - `Spread : float` — total fan angle (degrees, 0-360).
  - `AngleVariance : float` — random per-shot jitter (+/- degrees).
  - `AngleOffset : float` — base rotation of the fan.
  - `KnockbackForce : float` — recoil applied to the owner.
  - `PushForce : float` — impulse applied to things hit.
  - `ResourceUsed : Resource`, `Cost : float` — per-shot resource cost.
  - `AimAssistData : AimAssistData` — aim-assist config.
  - `BarrelLength : float` — muzzle offset along the fire direction.
  - `MaxRotationSpeedWhileShooting : float`.
  - SFX/visual strings & prefabs: `ShootSfx`, `ContinousShootSfx`, `StartSfx`, `ReleaseSfx`, `WarmupSfx`, `ReloadSfx`, `MuzzleParticlePrefab`, `ReloadParticlePrefab`, `ShakePreset`, `RumblePreset`.
  - `IsTriggerPulled : bool`, `IsWarmingUp`/`IsWarmedUp` (computed from `warmupProgress`), `Owner : Unit` (set via `Equip`).
  - `subEmitters : List<WeaponBase>` — fired by projectiles' `fireSub`.
  - `augmentations : List<WeaponAugmentation>` — fire-time modifiers.
- Key methods:
  - `Fire(IBarrel) : UniTaskVoid` — burst loop: `DoShoot` then `await Delay(BurstDelay)` × `BurstSize`.
  - `DoShoot(IBarrel)` — for each `GetDirections(barrel.Direction)` calls `FireSingle`, plays muzzle particle, plays `ShootSfx`.
  - `protected IEnumerable<Vector2> GetDirections(Vector2)` — the spread math (first angle `-Spread/2 + AngleOffset`, step `Spread/(count-1)`, plus `±AngleVariance`).
  - `protected abstract FireSingle(Vector2 position, Vector2 direction)` — **the main per-weapon override / patch point**.
  - `Equip(Unit)` / `Unequip()`, `Warmup(float)` / `CoolDown()`, `InitializeVisuals()`.
  - `AddAugmentation(WeaponAugmentation)`; `GetTotalCostWithAllAugmentations`, `GetExplosionDamageWithAllAugmentations`, `GetBurnWithAllAugmentations`, `GetDischargeDataWithAllAugmentations`, `GetExplosionRadiusWithAllAugmentations` — aggregate effects for UI.
- Relationships: Base of all four runtime weapon types; created by `WeaponFactory`.

### WeaponData
- Kind: abstract `ScriptableObject`. Implements `IIdentifiable<string>`, `IHasCost`.
- Purpose: The serialized authoring definition. Every field here is a moddable asset value.
- Key fields: `id` (auto-set to asset name in `OnValidate`), `damage`, `burn:MinMaxFloat`, `fireRate`, `warmupTime`, `burstSize`(`[Min(1)]`, default 1), `burstDelay`, `projectileCount`(default 1), `spread`(`[Range(0,360)]`), `angleVariance`, `angleOffset`(`[Range(0,360)]`), `knockbackForce`, `pushForce`, `resourceUsed`, `cost`(default 1), `barrelLength`, `maxRotationSpeedWhileShooting`(default 360), `aimAssistData`, `explosion`, `discharge`, `cellConvertData`, `subEmitter:WeaponData`, plus all the SFX strings & particle prefabs.
- Relationships: Subclassed by the four `*WeaponData` types; registered in `WeaponRegistry`.

### WeaponFactory
- Kind: class. Implements `IFactory<WeaponData, IEnumerable<Module>, WeaponBase>`.
- Purpose: Maps a `WeaponData` subtype to its runtime weapon and applies module modifiers.
- Key method: `Create(WeaponData, IEnumerable<Module> modulesInCluster = null) : WeaponBase` — `switch` on data type → new weapon; then for each module's `ModuleEffect` that is an `IWeaponModifier`, calls `.Modify(weapon)`; then recursively builds `subEmitter`. Throws `NotImplementedException` for unknown data types.
- Relationships: Resolved from `ServiceLocator.Get<WeaponFactory>()`. **Extending weapon types requires patching this `Create`.**

### ProjectileWeapon
- Kind: class : `WeaponBase`, `IHasRangeProperty`, `IHasSpeedProperty`.
- Purpose: The standard gun. Spawns `Projectile` (custom mover) or `PhysicsProjectile` (`UsePhysics == true`).
- Key fields/properties: `UsePhysics:bool`, `ProjectilePrefab:Projectile`, `PhysicsProjectilePrefab:PhysicsProjectile`, `ProjectileRadius`, `ProjectileSpeed`, `ProjectileSpeedVariance`, `VelocityInfluenceData`, `ImpactBehaviour`, `RangeData`, `LifetimeData`, `RotationData`, `PiercingData`, `MovementNoiseData`(get-only), `ProjectileBounceData`(get-only), `HomingData`(get-only), `ElectricityData`(get-only), `CollidesWithSlime`(get-only). `Range` proxies `RangeData.range`; `Speed` proxies `ProjectileSpeed`.
- Events: `OnPrepareProjectile(ProjectileWeapon, IProjectile)` — fired before and after the projectile shoots; used by `ProjectileVelocityInfluencer`.
- Key method: `FireSingle` — instantiates prefab at `position + direction*BarrelLength`, copies all data onto the projectile, sets `Velocity = direction * (ProjectileSpeed ± variance)`, applies `IProjectileModifier` augmentations (charging `CostPerProjectile` from owner), then `projectile.Shoot()`.

### PhysicsWeapon
- Kind: class : `WeaponBase`, `IHasSpeedProperty`.
- Purpose: Lobs a plain `Rigidbody2D` (e.g. grenades).
- Key fields: `ProjectilePrefab:Rigidbody2D`, `ProjectileSpeed`, `ProjectileSpeedVariance`, `VelocityInfluenceData`. `Speed` proxies `ProjectileSpeed`.
- Event: `OnPrepareGrenade(PhysicsWeapon, Rigidbody2D)`.
- Key method: `FireSingle` — instantiate, set `linearVelocity`, raise event.

### HitscanWeapon
- Kind: class : `WeaponBase`, `IHasRangeProperty`, `IDamageSource`.
- Purpose: Instant beam — `Physics2D.CircleCast` each shot; continuous beam visual.
- Key fields/properties: `Range`, `RayWidth`, `Visual:HitscanWeaponVisual`, `LayerMask`, `DamageRepeatDelay` (default 0.25). `GetDamage()` returns `Damage`.
- Key methods: `FireSingle` — circle-casts, notifies `IHitscanWeaponListener`(s) on hit if `CanDamage` (repeat-delay gated), applies `PushForce`, converts cells if `CellConvertData.enabled`. `OnBarrelMoved` updates one beam visual per direction while trigger pulled. `InitializeVisuals`/`Dispose` pool `HitscanWeaponVisual` instances to match `ProjectileCount`.

### HitscanWeaponData
- Kind: `ScriptableObject` : `WeaponData`, `IHasRangeProperty`.
- Key fields: `range`, `rayWidth`, `layerMask`, `damageRepeatDelay`(0.25), `visual`.

### HitscanWeaponVisual
- Kind: MonoBehaviour.
- Purpose: Renders the beam fire/warmup sprites, impact particle, and `Light2D`. `Firing`/`WarmingUp` toggles; `UpdateVisual(start,end,hitNormal)` stretches the sprites.

### IHitscanWeaponListener
- Kind: interface. `void OnHitByHitscanWeapon(HitscanWeapon weapon, RaycastHit2D hit)`.

### MinionSpawnerWeapon / MinionSpawnerWeaponData
- Kind: class : `WeaponBase`, `IHasSpeedProperty` / `ScriptableObject` : `WeaponData`, `IHasSpeedProperty`.
- Purpose: "Fires" a `Unit` minion entity instead of a projectile.
- Key fields: `MinionPrefab:Unit`, `ProjectileSpeed`, `ProjectileSpeedVariance`, `VelocityInfluenceData`, `MinionFacesShootDirection`. Data mirrors these (`minionPrefab`, etc.).
- Key method: `FireSingle` — creates the entity via `EntityGameObjectManager.CreateEntity`, optionally rotates to face, sets its `Rigidbody2D.linearVelocity`, raises `OnPrepareMinion`.

### WeaponHolder (abstract) / StaticWeaponHolder / ModuleSlotWeaponHolder
- Kind: abstract MonoBehaviour / two MonoBehaviour subclasses.
- Purpose: Own the active `WeaponBase` and notify listeners.
- `WeaponHolder`: `Action<WeaponBase,WeaponBase> WeaponChanged`; abstract `WeaponBase Weapon`.
- `StaticWeaponHolder`: serialized `weapon:WeaponData`; builds once in `Start`, disposes in `OnDestroy`. Exposes `WeaponData`.
- `ModuleSlotWeaponHolder`: serialized `gridOwner:ModuleGridOwner`, `isSecondary:bool`. Binds to the ship's `ClusterType.PrimaryWeapon`/`SecondaryWeapon` cluster and `RecreateWeapon` on every `ModulesRefreshed`, passing `ConnectedAndPoweredModules` to the factory (so upgrades re-apply). This is the live ship weapon path.

### WeaponRegistry
- Kind: `ScriptableObject` : `ScriptableObjectRegistry<WeaponData,string>`. Lookup of all weapon assets by id.

### WeaponModule / WeaponModuleData
- Kind: class : `Module` / `ScriptableObject` : `ModuleData`.
- Purpose: A grid module carrying a `WeaponData` (`weapon`). The main weapon module in a cluster determines the cluster's weapon (`ModuleSlotWeaponHolder.RefreshWeapon` checks `MainModule is WeaponModule`). `WeaponModule` builds a `baseWeapon` for property-list display.

### WeaponBasedActiveModule / WeaponBasedActiveModuleData
- Kind: class : `ActiveModule` / `ScriptableObject` : `ActiveModuleData`.
- Purpose: A manually-activated module that fires a weapon at the owner's position. `Cooldown = 1/fireRate`, `ActivationCost = cost`, `ResourceUsed` from the data. `Activate(owner)` equips if needed, checks resource, fires via a `FakeBarrel`, deducts cost. Rebuilds its weapon on cluster refresh.

### WeaponBasedConsumable
- Kind: `ScriptableObject` : `Consumable`.
- Purpose: One-shot consumable. `Use(Ship)` builds a throwaway weapon (`using` disposed) and calls `DoShoot(new FakeBarrel(ship.pos, up))`.

### WeaponDropdown
- Kind: MonoBehaviour (debug/dev UI). Lists all `WeaponModuleData` in a `TMP_Dropdown` and installs the selected weapon module into the ship's primary weapon grid slot. Useful reference for how to swap weapons at runtime.

### WeaponAugmentation / IWeaponModifier / ModifyWeaponProperty
- `IWeaponModifier`: `void Modify(WeaponBase)` — applied once by `WeaponFactory` at creation.
- `WeaponAugmentation`: abstract `ModuleEffect` implementing `IWeaponModifier`; its `Modify` just calls `weaponBase.AddAugmentation(this)` so it persists for fire-time effects.
- `ModifyWeaponProperty`: `ModuleEffect`, `IWeaponModifier`, `IHasDescriptionForWeapon`. **The data-driven stat upgrade.** Serialized: `targetProperty:TargetProperty`, `operation:Operation`, `deltaCalculationMode:DeltaCalculationMode`, `value:FloatSeries` (per upgrade level).
  - `enum TargetProperty { FireRate, BurstSize, BurstDelay, ProjectileCount, Spread, AngleVariance, AngleOffset, KnockbackForce, Cost, Range, Speed, Damage }`.
  - `enum Operation { Add, Multiply }`.
  - `enum DeltaCalculationMode { Constant, FromOriginal, FromCurrent }` — delta is the level value, the value × current stat, or value × the asset's base stat.
  - `Modify` looks up a `ValueApplyer` (get/set lambdas per property) and applies `current ± delta` or `current × delta`. `Range`/`Speed` are no-ops if the weapon doesn't implement the matching interface; `Damage` adds to `Damage.amount`.

### IProjectileModifier / IHasPerProjectileCost
- `IProjectileModifier : IHasPerProjectileCost` — `void ModifyProjectile(IProjectile)`. Applied per-projectile in `ProjectileWeapon.FireSingle` only if the owner can afford `CostPerProjectile` of `CostResource`; cost is then deducted.
- `IHasPerProjectileCost` — `float CostPerProjectile`, `Resource CostResource`.

### IHasRangeProperty / IHasSpeedProperty / IHasDescriptionForWeapon
- `IHasRangeProperty`: `float Range { get; set; }`. `IHasSpeedProperty`: `float Speed { get; set; }`. Implemented by both data and runtime weapon types so modifiers can read/write generically.
- `IHasDescriptionForWeapon`: `GetDescription(WeaponBase, bool isInstalled, List<DisplayableProperty>)` for UI.

### Shooter
- Kind: MonoBehaviour. **The firing driver** — patch here for trigger/cooldown logic.
- Key fields: `unit:Unit`, `weaponHolder:WeaponHolder`, `OnShoot:UnityEvent`, `noResourceSfx`. Caches `weapon:WeaponBase`, `barrel:IBarrel` (found via `GetComponentInChildren<IBarrel>()`), and a `HashSet<object> blockers`.
- Key properties: `IsShooting`/`SetShooting`, `FireRatePassed` (`Time.time > lastShootTime + 1/FireRate`), `CanShoot`, `OwnerHasResource`, `OwnerResourceFull`.
- Key methods: `Update()` — per frame: updates `weapon.OnBarrelMoved`, manages warmup, plays continuous/warmup SFX, and calls `Shoot()` when `FireRatePassed` & warmed up; otherwise triggers empty-clip feedback. `Shoot()` — `weapon.Fire(barrel)`, deducts cost from the resource tank, applies knockback to the owner, raises `OnShoot`. `Block(obj)`/`Unblock(obj)` — gate firing.

### ShooterBlocker / AutoShoot
- `ShooterBlocker`: MonoBehaviour with `Shooter[] blockedShooters`; `Block`s them while enabled, `Unblock`s on disable.
- `AutoShoot`: MonoBehaviour; holds one `shooter`'s trigger (`SetShooting(true)`) while the GameObject is enabled (used by turrets/enemies).

### IBarrel / BarrelTransform / AssistedBarrel / FakeBarrel
- `IBarrel`: `Vector2 Position`, `Vector2 Direction`, `event Action<Vector2> DirectionChanged`.
- `BarrelTransform`: MonoBehaviour. `Position = transform.position`; `Direction` is either a stored vector or `transform.right` (`useTransformRotation`); can drive `transform.rotation` from direction (`updateTransformRotation`).
- `AssistedBarrel`: MonoBehaviour wrapping a `BarrelTransform` + `AimAssist` + `WeaponHolder`. `GetTargetDirection` bends the raw direction toward the aim-assist target (using the weapon's `Speed` for prediction) unless `SettingsManager.GameplayOptions.disableAimAssist`. Exposes `CurrentTarget`.
- `FakeBarrel`: plain class implementing `IBarrel` with settable `Position`/`Direction`; used for code-spawned shots (sub-emitters, consumables, active modules).

### Aimer / AimVisual / Crosshair
- `Aimer`: MonoBehaviour. Rotates `barrel:BarrelTransform` toward `targetPosition` at `rotationSpeed` deg/s. Nested `interface IAimEffect { Vector2 Apply(Aimer, Vector2) }` lets components post-process the aim point. `AimAt(Vector2)` sets the target.
- `AimVisual`: MonoBehaviour. Raycasts from the shooter's barrel and stretches a sprite to the hit point (laser-sight line).
- `Crosshair`: MonoBehaviour. Positions a sprite at `aimer.TargetPosition`; `Visible` toggles the renderer.

### AimAssist / AimAssistData / AimAssistTarget
- `AimAssist`: MonoBehaviour : `ComponentScanner<AimAssistTarget>`. Each `Update` `Scan()`s. `TryGetTarget(position, direction, projectileSpeed, AimAssistData, out target, out targetPos)` picks the on-screen target with the **smallest angle** under `maxAngle`; if `isPredictive`, leads the target by `distance/projectileSpeed × velocity`.
- `AimAssistData`: `[Serializable] struct { bool enabled; float maxAngle; bool isPredictive; }`.
- `AimAssistTarget`: MonoBehaviour marker exposing `Velocity` (from a serialized `Rigidbody2D`). Also used by `PhysicsProjectile` homing scans.

### Projectile
- Kind: MonoBehaviour : `IProjectile`, `IDamageSource`. **The custom (non-physics) projectile** — patch its `FixedUpdate`/`OnObjectHit` for movement/impact behavior.
- Key properties (set by the firing weapon): `Owner`, `Radius`, `Velocity`, `Damage`, `Explosion`, `DischargeData`, `Burn`, `ImpactBehaviour`, `RangeData`, `LifetimeData`, `RotationData`, `PushForce`, `PiercingData`, `MovementNoiseData`, `ProjectileBounceData`, `SubEmitters`. Read-only `ShotTime`, `StartSpeed`. Event `Shot`.
- Key methods:
  - `Shoot()` — records shot time/position, computes `timeToReachRange` from `RangeData`, aligns rotation if `WhenShot`, raises `Shot`.
  - `FixedUpdate()` — applies range slowdown (`Lerp(StartSpeed,0)`), movement-noise wobble (`PerlinNoise`), then `CircleCast(Radius, Velocity)`. Passes through friendly units; else `OnObjectHit`.
  - `OnObjectHit(hit)` — notifies `IProjectileListener`s (`TryHit`, repeat-delay gated by `PiercingData.damageRepeatDelay`); if `PiercingData.enabled` (and not ground) or `ProjectilePassThrough`, keeps moving; applies knockback; bounces if `ProjectileBounceData.enabled` & layer matches; else runs `ImpactBehaviour` (destroy effect, explosion, `FireSub`, discharge) past `safetyDistance`, otherwise reflects.
  - `Reflect`, `FireSub` (fires each `SubEmitter` from a `FakeBarrel`), `SpawnExplosion`.

### PhysicsProjectile
- Kind: MonoBehaviour : `ComponentScanner<AimAssistTarget>`, `IProjectile`, `IDamageSource`. **The Rigidbody2D + homing projectile.**
- Key properties: shares the `IProjectile` set (`Velocity` proxies the rigidbody's `linearVelocity`), plus `HomingData:ProjectileHomingData`, `Target:GameObject`, `CollidesWithSlime:bool`.
- Key methods: `Shoot()` — auto-seeks a target if `targetMode==AutoSeekWhenShot`. `Update()` — lifetime + homing target acquisition (`AutoSeekWhenNeeded`, `FromLockOnly`). `FixedUpdate()` → `Homing()` — adds torque toward `targetPosition` (capped by `maxAngularVelocity`), turbulence torque, and forward `acceleration` up to `maxSpeed`. `OnCollisionEnter2D`/`OnTriggerEnter2D` → `Impact` runs `ImpactBehaviour`. `TriggerCollisionOnObject` notifies `IProjectileListener`s.

### IProjectile / IProjectileListener
- `IProjectile : IDamageSource` — the shared contract: `Owner`, `Velocity`, `Damage`, `Burn`, `Explosion`, `DischargeData`, `ImpactBehaviour`, `RangeData`, `LifetimeData`, `PushForce`, `SubEmitters`, computed `ShouldApplyBurnOnContact`, event `Shot`, `Shoot()`.
- `IProjectileListener` — `void ProjectileCollided(IProjectile projectile, Vector2 position, Vector2 normal)`; implement on colliders that should take projectile hits.

### Helper components & math
- `ProjectileDispenser` (MonoBehaviour) — on `Start`, builds a weapon from a `WeaponData` and fires it through the GameObject's `IBarrel` (environmental emitters).
- `ProjectileLifetime` (MonoBehaviour) — standalone timed destroy with `lifetime`, `lifetimeVariance`, `spawnOnTimeDown`, `OnTimeout` event (independent of `ProjectileLifetimeData`).
- `ProjectileRotation` (MonoBehaviour) — on `Start`, adds spin torque; `enum ProjectileRotationDirection { Clockwise, CounterCloclwise, Random, FromDirection }`, `rotationForce:MinMaxFloat`.
- `ProjectileVelocityInfluencer` (MonoBehaviour) — subscribes to the weapon's `OnPrepareProjectile`/`OnPrepareGrenade` and adds the owner's velocity (scaled by `VelocityInfluenceData`) to each shot.
- `ProjectilePassThrough` (empty MonoBehaviour) — marker; `Projectile`/`PhysicsProjectile` pass through colliders carrying it.
- `ProjectileMath` (static) — `LaunchAngle`, `LaunchSpeed`, `TimeOfFlight`, `ProjectileArcPoints` ballistic helpers (used by AI/arc aiming).

### Projectile data structs/classes (set onto projectiles by the weapon)
- `ProjectileImpactBehaviour` (struct): `enabled`, `destroyVelocityThreshold`, `safetyDistance`, `destroyEffect:GameObject`, `spawnExplosion`, `fireSub`, `discharge`.
- `ProjectileRangeData` (struct): `enabled`, `range`, `variance`, `slowDown`, `destroyWhenReached`, `destroyEffect`, `spawnExplosion`, `fireSub`.
- `ProjectileLifetimeData` (struct): `enabled`, `time`, `timeVariance`, `spawnOnTimeDown:GameObject`, `spawnExplosion`, `fireSub`, `discharge`.
- `ProjectileRotationData` (struct): `enabled`, `orientationAlignment` of `enum AlignOrientationMode { None, WhenShot, Continuous }`.
- `PiercingData` (struct): `enabled`, `damageRepeatDelay`, `knockBackRepeatDelay`.
- `ProjectileBounceData` (**class**, serializable): `enabled`, `layerMask:LayerMask`.
- `ProjectileHomingData` (struct): `enabled`, `targetMode` of `enum TargetMode { FromLockOnly, AutoSeekWhenShot, AutoSeekWhenNeeded }`, `acceleration`, `torque`, `maxSpeed`, `maxAngularVelocity`, `turbulenceTorque`, `turbulenceFrequency`, `turbulenceDistanceCurve:AnimationCurve`.
- `ProjectileElectricityData` (struct): `enabled`, `isSource`, `emittedSystem`, `chainLength`, `damage`, `layerMask`, `minConductivity`, `conductivity`, `conductedSystem`, `showPreviewBeam`, `showBeamParticles`, `limitedCharge`, `maxCharge`, `damageRadius`, `damageRepeatDelay`.
- `ProjectileMovementNoiseData` (struct): `enabled`, `angle`, `frequency`.
- `VelocityInfluenceData` (struct): `multiplyer`, `limit`, `influenceMode` of `enum VelocityInfluenceMode { AnyDirection, Projected }`, `allowSlowDown`.

## Modding Notes

### Where to change stats
Two places hold the numbers, and they behave differently:

- **The `WeaponData` asset** (`damage`, `fireRate`, `projectileCount`, `spread`, `cost`, plus the `*WeaponData` extras like `projectileSpeed`, `range`, `rayWidth`). Editing these changes the **base** value. But because runtime weapons copy values in their constructor and `ModifyWeaponProperty` modifiers re-derive from `TemplateData`, asset edits are the cleanest way to globally rebalance a weapon.
- **The live `WeaponBase` properties** (`FireRate`, `Damage`, `ProjectileCount`, `Spread`, `AngleVariance`, `Cost`, etc.). These are mutable at runtime; Harmony postfixes that set them give per-instance control.

### Harmony patch targets
- **More damage:** postfix `WeaponBase` ctor or `Shooter.Shoot`/`WeaponBase.DoShoot` to bump `Damage` (`new Damage(amount*k, type)` — `Damage` is a struct, reassign the whole property). For projectiles specifically, postfix `ProjectileWeapon.FireSingle` (the projectile already has its `Damage` set there) or `Projectile.GetDamage`/`PhysicsProjectile.GetDamage`.
- **Faster fire:** `Shooter.FireRatePassed` getter (returns `Time.time > lastShootTime + 1/FireRate`) — postfix to force `true`, or postfix the `WeaponBase` ctor / `WeaponFactory.Create` to multiply `FireRate`. Also raises burst rate via `BurstDelay`.
- **Extra projectiles / spread:** set `WeaponBase.ProjectileCount` (it is a **float**, and `GetDirections` compares it as float — fractional values truncate in the `while ((float)i < ProjectileCount)` loop). Combine with `Spread` so they fan out. Patch points: `WeaponBase` ctor postfix, or transpile/prefix `WeaponBase.GetDirections` to yield more directions. Note muzzle-particle and hitscan-visual pools size to `ProjectileCount` in `InitializeVisuals`, so changing it after creation may need `InitializeVisuals()` re-run.
- **Infinite range:** for projectiles, set `ProjectileRangeData.enabled = false` (or huge `range`) — but `RangeData` is a **struct**; assign through `ProjectileWeapon.Range`/`RangeData` property (the property setter copies-mutates-reassigns, which is the correct struct pattern) rather than mutating a local copy. Also clear `ProjectileLifetimeData.enabled` (in `Projectile.HandleLifetime`) since lifetime independently destroys the projectile. For hitscan, set `HitscanWeapon.Range`.
- **Infinite pierce:** set `PiercingData.enabled = true` on the projectile (struct — reassign whole value) so `Projectile.OnObjectHit` keeps moving instead of destroying; tune `damageRepeatDelay` to control re-hit cadence. Disabling `ImpactBehaviour.enabled` also prevents self-destruct on impact. (PhysicsProjectile has no pierce — it relies on physics collision.)
- **No resource cost / infinite ammo:** `Shooter.OwnerHasResource` getter, or set `WeaponBase.Cost = 0`. `Unit.HasInfiniteResource` already short-circuits cost.

### Struct / value-type gotchas
- `AimAssistData`, `ProjectileImpactBehaviour`, `ProjectileRangeData`, `ProjectileLifetimeData`, `ProjectileRotationData`, `PiercingData`, `ProjectileHomingData`, `ProjectileElectricityData`, `ProjectileMovementNoiseData`, `VelocityInfluenceData` are **structs**. You cannot mutate one field through a property getter chain (e.g. `weapon.RangeData.range = x` won't compile / won't stick). Read the whole struct into a local, mutate, then assign back — exactly what `ProjectileWeapon.Range`'s setter does.
- `ProjectileBounceData` is a **class** (reference type), so its fields can be mutated in place and shared between projectiles.
- `Damage` is a struct value on `WeaponBase.Damage` — reassign the whole property to change it.
- `ProjectileCount`, `BurstSize` are stored as `float`/`int` but several comparisons cast to float; watch truncation.

### DI / lifecycle gotchas
- Weapons are built by `WeaponFactory`, resolved via `ServiceLocator.Get<WeaponFactory>()`. New weapon types must be added to `WeaponFactory.Create` (it throws `NotImplementedException` otherwise). Patch `Create` (postfix) to inject augmentations or swap prefabs.
- The live ship weapon is rebuilt on every module-grid refresh (`ModuleSlotWeaponHolder.RecreateWeapon`), so any runtime property changes you make are **lost on rebuild**. To persist, patch at creation (factory/ctor) or hook `WeaponHolder.WeaponChanged`.
- `WeaponBase.Fire` is `async UniTaskVoid` and uses a `CancellationTokenSource` cancelled in `Dispose`; long bursts stop when the weapon is disposed (weapon swap). `Projectile.FireSub`/sub-emitter shots use `.Forget()`.
- `Projectile` integrates movement in `FixedUpdate` with manual `CircleCast` (not a Rigidbody), while `PhysicsProjectile` uses real `Rigidbody2D` physics + `OnCollision/Trigger`. Patches must target the right one (`ProjectileWeapon.UsePhysics` decides which prefab is spawned).
- Effect aggregation for tooltips lives in `WeaponBase.Get*WithAllAugmentations`; if you add custom augmentations and want correct UI numbers, mirror those methods.
