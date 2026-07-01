# Combat: Health, Damage, Shields & Explosions
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK's combat resolution is a small, surprisingly centralized layer. Almost every weapon, hazard, explosion, fire tick, electrical conductor and colliding cell ultimately funnels into **one of two `HealthBase` implementations** and then into a single private damage-application method. Understanding that funnel is the key to patching combat.

The pieces:

- **`Damage`** — a small `struct` carrying a `float amount` and a `Resource damageType`. The *damage type is just a `Resource`* (the same ScriptableObject used for health/ammo/currency tanks). This is how the game unifies "fire damage", "kinetic damage", etc. with the resource economy.
- **`IDamageSource`** — anything that can produce a `Damage` (`GetDamage()`). Implemented by `ExplosionComponent`, `Hazard`, and the projectile.
- **`IDamagable`** — anything that can *evaluate* an incoming `Damage` (`GetDamageAmount(Damage)`). Implemented by `HealthBase`.
- **The damage matrix / type system** — each `Resource` carries a list of `DamageModifier`s. `DamageMatrixConfig` parses a CSV into those modifiers so that "damage type X vs. target resource Y" yields a multiplier. `Damage.GetScaledDamageAmount(resource)` applies it.
- **`HealthBase`** — abstract `MonoBehaviour` that implements every "I got hit by …" listener interface and routes them all to `TakeDamage`. Two concrete subclasses: **`Health`** (simple, standalone props/destructibles) and **`DamagableResource`** (the real combat actor for the player ship and enemies — resource-tank-backed, with shields, i-frames, burn, corpse states, and invincibility).
- **Shields** — `Shield` components sit in front of `DamagableResource`. Incoming `Damage` is passed through each shield (last-installed first); each shield absorbs against its own resource tank by an `effectiveness` factor and returns the *reduced* `Damage` to continue.
- **Explosions** — `Explosion` is a `struct` (radius + list of `Damage` + burn + push). `ExplosionManager.SpawnExplosion` does an `OverlapCircleAll`, pushes rigidbodies (`PushByExplosion`), damages level cells, and calls `OnExplosion` on every `IExplosionListener` in range.

## Damage Pipeline

End-to-end trace of a typical hit (projectile shown; other sources differ only at step 1):

1. **Source produces damage.** A source implementing `IDamageSource.GetDamage()` (or a projectile/explosion/hazard) carries a `Damage`. `Projectile.GetDamage()` returns its configured `Damage`.

2. **Collision routed to a listener.** The source calls the appropriate listener method on the target's `HealthBase`. The dispatch interfaces and their entry methods (all on `HealthBase`):
   - `IProjectileListener.ProjectileCollided(projectile, pos, normal)` → `TakeDamage(projectile.GetDamage())`
   - `IExplosionListener.OnExplosion(explosion)` → `TakeDamage(explosion.damages)` *(the `List<Damage>` overload)*
   - `IHitscanWeaponListener.OnHitByHitscanWeapon(weapon, hit)` → `TakeDamage(weapon.GetDamage())`
   - `Hazard.IHazardSensor.OnHazardTouched(args)` → `TakeDamage(args.damage)`
   - `IElectricityListener.OnHitByElectricity(conductor)` → `TakeDamage(conductor.Damage)`
   - `ICellCollisionListener.OnCellCollision(cell)` → `TakeDamage(cellType.contactDamage)` (only if `contactDamage.amount > 0`)

   (`Projectile` gates re-hits itself: `Projectile.TryHit` → `CanDamage(listener)` enforces `PiercingData.damageRepeatDelay` before calling `ProjectileCollided`.)

3. **`TakeDamage` → scale by damage matrix → shields.** Two implementations:

   **`Health` (simple):**
   - `TakeDamage(Damage)` → `Damage(GetDamageAmount(damage))`.
   - `GetDamageAmount(Damage)` returns `damage.amount` if `damageConditions.Validate` passes (i.e. `amount >= minDamage`), else `0`. *No matrix scaling here.*
   - `TakeDamage(IReadOnlyList<Damage>)` sums raw `amount`s and applies if `>= minDamage`.

   **`DamagableResource` (real combat actor):**
   - `TakeDamage(Damage)`: loops shields **from last installed to first**: `damage = shields[i].TakeDamage(damage)`; if `damage.amount <= 0` it returns (fully absorbed). Surviving damage → `Damage(GetDamageAmount(damage))`.
   - `GetDamageAmount(Damage)`: **applies the matrix** — `damage.amount = damage.GetScaledDamageAmount(resource)` (scales the incoming damage type against *this unit's* resource), then returns `0` if it fails `damageConditions.Validate`, else the scaled amount.
   - `TakeDamage(IReadOnlyList<Damage>)`: for each damage, if a shield is active it routes through `TakeDamage(Damage)`; otherwise accumulates `GetScaledDamageAmount(resource)`. Applies the sum if `>= minDamage`.

4. **Matrix scaling detail.** `Damage.GetScaledDamageAmount(resource)` = `GetDamageMultiplier(resource) * amount`, where `GetDamageMultiplier` calls `damageType.GetDamageMultiplierOn(resource)`. `Resource.GetDamageMultiplierOn` scans its `damageModifiers` list for an entry matching the target resource and returns that multiplier (default `1`). Those modifiers are populated at load by `DamageMatrixConfig.Parse` from a CSV (rows = damage types, columns = target resources, cells = multipliers; values equal to 1 are skipped).

5. **Apply to health.** Final numeric application:
   - `Health.Damage(float)`: `CurrentHealth -= amount` (if `!isDead && amount != 0`), invoke `onDamage`, then `Die()` if `CurrentHealth <= 0`.
   - **`DamagableResource.Damage(float)` — THE chokepoint.** Guards: `!IsDamageBlocked()` (all `damageBlockers` active), `amount > 0`, `Unit.HasTank(resource)`, and `Time.time - lastDamageTime >= iFrameDuration` (invincibility frames). Then `tank.Value -= amount`; set `lastDamageTime`; invoke `onDamage`. **`if (IsInvincible && tank.Value <= 0f) tank.Value = 1f;`** Then `Die()` if not dead and `tank.Value <= 0`, or `DestroyCorpse()` if in corpse state and below `-corpseSettings.health`.

6. **Death.** `Die()` sets `IsDead`/`isDead`, invokes `onDeath`, optionally instantiates `spawnOnDeath` / corpse particles, and either enters a corpse state (`DamagableResource` with `hasCorpseState`) or destroys the GameObject (`destroyOnDeath`). On `DamagableResource`, kill credit is registered separately: after a hit, if `IsDead`, the source's `Owner.RegisterKill(Unit)` is called (see `ProjectileCollided`/`OnExplosion`/`OnHitByHitscanWeapon` overrides).

**Fire/burn side-channel:** `DamagableResource.Update()` ticks burn damage directly via `Damage(Unit.ComponentData.burnProperties.fireDmgPerTick)` while `IsOnFire`, independent of `TakeDamage`. Burn is *applied* to a unit by `ApplyBurn` (raises `Unit.Data.BurnLevel`) from projectile/hitscan/explosion hits.

## Class Index

| Type | Kind | Role in combat |
|------|------|----------------|
| `IDamagable` | interface | `GetDamageAmount(Damage)` — evaluate incoming damage |
| `IDamageSource` | interface | `GetDamage()` — produce a `Damage` |
| `Damage` | struct | `amount` + `Resource damageType`; matrix scaling helpers |
| `Resource` | ScriptableObject | Doubles as damage *type*; holds `damageModifiers` |
| `DamageMatrixConfig` | ScriptableObject (`ConfigRegistry`) | Parses CSV into per-resource damage multipliers |
| `DamageMatrixItem` | class | One matrix row: a damage type → `Dictionary<Resource,float>` |
| `DamageConditions` | class | `minDamage` threshold gate |
| `HealthBase` | abstract MonoBehaviour | Routes all hit-listener interfaces → `TakeDamage` |
| `Health` | MonoBehaviour : HealthBase | Simple float-HP target (no matrix, no shields) |
| `DamagableResource` | MonoBehaviour : HealthBase | Real combat actor; tanks, shields, i-frames, burn, corpse |
| `ResourceTank` | class | Backing store for a resource/HP value (`Value`, `Capacity`, `isInfinite`) |
| `ImpactDamage` | MonoBehaviour | Collision-velocity → `DamagableResource.Damage` |
| `Hazard` | MonoBehaviour : IDamageSource | Touch/collision damage volume |
| `Shield` | MonoBehaviour | Absorbs `Damage` against a resource by `effectiveness` |
| `AddShieldEffect` | ModuleEffect | Adds a shield to a unit on stat recalc |
| `Explosion` | struct | radius + `List<Damage>` + burn + push |
| `ExplosionComponent` | MonoBehaviour : IDamageSource | Spawns an `Explosion` on Start |
| `ExplosionManager` | MonoBehaviour (service) | `SpawnExplosion` / `DoExplosionLogic` — area damage |
| `ExplosionVisual` | MonoBehaviour | Particle visuals for an explosion |
| `ExplosionVisualSettings` | ScriptableObject | Lerped particle presets by radius |
| `IExplosionListener` | interface | `OnExplosion(Explosion)` |
| `PushByExplosion` | MonoBehaviour | Applies explosion knockback to a rigidbody |
| `AddExplosionEffect` | WeaponAugmentation | Adds explosion damage/radius/burn to projectiles |
| `AddImpactExplosionEffect` | ModuleEffect : IWeaponModifier | Enables impact explosion on a projectile weapon |
| `IncreaseExplosionRadiusEffect` | ModuleEffect : IWeaponModifier | +radius on a weapon's explosion |
| `AddBurnEffect` | WeaponAugmentation | Adds burn to projectiles |
| `AddDischargeEffect` | WeaponAugmentation | Adds chain-lightning discharge to projectiles |
| `DrainResourceEffect` | ModuleEffect | Drains a resource tank over time |
| `DamageHighlight` | MonoBehaviour | Flashes sprites on `onDamage` |
| `HealthVisualizer` | MonoBehaviour | Toggles GameObjects by HP threshold |
| `HealthbarOwner` | MonoBehaviour | Declares which resources display as a healthbar |
| `HealthbarManager` | MonoBehaviour (service) | Spawns/removes healthbar widgets; boss bar |
| `HealthbarWidget` | MonoBehaviour | Builds `ResourceBar`s for an owner |
| `ImpactVisualizer` | MonoBehaviour | Spawns impact particle at a point |
| `ImpactParticle` | MonoBehaviour | Velocity-gated collision particles |
| `DestroyOnImpact` | MonoBehaviour | Destroy self on hard collision |
| `DestroyIfNotTouch` | MonoBehaviour | Destroy self if no touchscreen |
| `StatusEffectForUnit` | MonoBehaviour | Emits burn particles while unit on fire |
| `StatusEffectParticleManager` | MonoBehaviour (service) | Manages burn particle/SFX emission |

## Classes

### IDamagable: interface
- **Members:** `float GetDamageAmount(Damage damage)`.
- **Purpose:** Lets a source ask "how much would this hit actually do?" without applying it. Implemented only by `HealthBase`.

### IDamageSource: interface
- **Members:** `Damage GetDamage()`.
- **Purpose:** Marks objects that emit damage. Implemented by `ExplosionComponent` and `Hazard`. (`Projectile` exposes the same `GetDamage()` signature but is typed as `IProjectile`.)

### Damage: struct (`[Serializable]`)
- **Fields:** `Resource damageType`; `float amount`. Primary constructor `Damage(float amount, Resource damageType)`.
- **Key methods:**
  - `float GetScaledDamageAmount(Resource resource)` → `GetDamageMultiplier(resource) * amount`.
  - `float GetDamageMultiplier(Resource resource)` → `1` if `damageType == null`, else `damageType.GetDamageMultiplierOn(resource)`.
- **Relationships:** The `damageType` is a `Resource`. A `null` `damageType` means "untyped, ×1" (used e.g. for cell/level damage).

### Resource: ScriptableObject (also the damage-type asset)
- **Purpose:** Used both as a resource tank id (health/ammo/currency) *and* as a damage type.
- **Key combat members:**
  - `List<DamageModifier> damageModifiers` (nested struct `DamageModifier { Resource resource; float damageMultiplier; }`).
  - `float GetDamageMultiplierOn(Resource resource)` — scans `damageModifiers`, returns matching multiplier or `1`.
  - `ExplosionVisual explosionBasePrefab`, `explosionAddonPrefab`, `Color explosionLightColor` (explosion visuals keyed by primary damage type).
  - `Color shieldColor` (used by `Shield`).
  - `int lowTreshold`, `isShared` (tank economy).
- **Note:** `damageModifiers` are cleared and repopulated by `DamageMatrixConfig.Parse` at load.

### DamageMatrixConfig: ConfigRegistry<DamageMatrixItem, string>
- **Purpose:** Loads the damage type vs. resource multiplier table from CSV (`CreateAssetMenu` "Punk/Config/DamageMatrixConfig").
- **Key fields:** `ResourceRegistry resourceRegistry`.
- **Key method:** `Parse(string csv)` — first line is the column order (target resources); each subsequent row is keyed by a damage-type resource name. For each non-`1` cell it writes both `DamageMatrixItem.damageMultiplyers[targetResource]` and a `Resource.DamageModifier` onto the damage-type resource. Logs errors for unknown resources / unparseable floats.

### DamageMatrixItem: class (`IIdentifiable<string>`)
- **Fields:** `Resource damageType`; `Dictionary<Resource, float> damageMultiplyers`. `Id => damageType.Id`.
- **Purpose:** One row of the matrix (in-memory). The runtime lookup actually goes through `Resource.damageModifiers`, not this dictionary.

### DamageConditions: class (`[Serializable]`)
- **Fields:** `float minDamage`.
- **Method:** `bool Validate(Damage damage)` → `damage.amount >= minDamage`.
- **Purpose:** A per-target floor; hits below `minDamage` are ignored. Present on both `Health` and `DamagableResource`.

### HealthBase: abstract MonoBehaviour
- **Implements:** `IDamagable, IProjectileListener, IExplosionListener, IHitscanWeaponListener, Hazard.IHazardSensor, ICellCollisionListener, IElectricityListener`.
- **Fields:** `UnityEvent onDamage`, `UnityEvent onDeath`.
- **Abstract:** `float CurrentHealth { get; set; }`, `float MaxHealth { get; }`, `void TakeDamage(Damage)`, `void TakeDamage(IReadOnlyList<Damage>)`, `float GetDamageAmount(Damage)`, `void Die()`.
- **Virtual routers (the dispatch table):** `ProjectileCollided` → `TakeDamage(projectile.GetDamage())`; `OnExplosion` → `TakeDamage(explosion.damages)`; `OnHitByHitscanWeapon` → `TakeDamage(weapon.GetDamage())`; `OnHazardTouched` → `TakeDamage(args.damage)`; `OnHitByElectricity` → `TakeDamage(conductor.Damage)`; `OnCellCollision` → `TakeDamage(cellType.contactDamage)` (guarded by `contactDamage.amount > 0`).
- **Purpose:** Single chokepoint where *all* damage sources converge before subclass-specific resolution.

### Health: MonoBehaviour : HealthBase
- **Purpose:** Simple, self-contained HP for destructible props / non-unit objects. No matrix scaling, no shields, no i-frames.
- **Fields:** `DamageConditions damageConditions`; `float maxHealth` (`[FormerlySerializedAs("currentHealth")]`); `bool destroyOnDeath`; `GameObject spawnOnDeath`; private `bool isDead`.
- **Props:** `CurrentHealth` (auto-prop, init to `maxHealth` in `Awake`); `MaxHealth => maxHealth`.
- **Methods:** `TakeDamage(Damage)` → `Damage(GetDamageAmount(damage))`; `TakeDamage(IReadOnlyList<Damage>)` sums raw amounts then `Damage(sum)` if `>= minDamage`; private `Damage(float)` reduces `CurrentHealth`, fires `onDamage`, `Die()` at `<= 0`; `GetDamageAmount(Damage)` returns `amount` or `0` (via `damageConditions.Validate`); `Die()` sets `isDead`, fires `onDeath`, spawns `spawnOnDeath`, optionally destroys.

### DamagableResource: MonoBehaviour : HealthBase (`IEntityBindingListener`, `[RequireComponent(typeof(Unit))]`)
- **Purpose:** The main combat health component for units (player ship & enemies). HP is stored in a `Unit` `ResourceTank` (`resource`), not a local float. Adds shields, i-frames, burn ticks, corpse states, kill credit, invincibility.
- **Key fields:** `Resource resource` (which tank is HP); `DamageConditions damageConditions`; `GameObject spawnOnDeath`; `float iFrameDuration`; `bool hasCorpseState`; `CorpseSettings corpseSettings` (nested struct: ai/health/animator/particles); `bool destroyOnDeath = true`; `Shield shieldPrefab`; `float firstShieldSize`; `bool shieldHasCollider`; `List<GameObject> damageBlockers`; events `UnityEvent<Resource> onShieldDamaged`, `UnityEvent<Unit> onGotAttacked`, `Action<DamagableResource> EquippedShieldsUpdated`.
- **Key properties:** `bool IsInvincible { get; set; }`; `bool IsDead { get; set; }`; `ResourceTank Tank => Unit.GetTank(resource)`; `IReadOnlyList<Shield> Shields`; `CurrentHealth` (reads/writes the resource tank); `MaxHealth => tank.Capacity`.
- **Key methods:**
  - `TakeDamage(Damage)` — passes through shields (last→first), then `Damage(GetDamageAmount(damage))`.
  - `TakeDamage(IReadOnlyList<Damage>)` — per-damage shield routing or accumulate scaled amounts; applies sum if `>= minDamage`.
  - **`Damage(float amount)`** (public) — the final application chokepoint (i-frames, damage blockers, invincibility floor at 1, death/corpse). *Best single patch target for HP manipulation.*
  - `GetDamageAmount(Damage)` — scales via `GetScaledDamageAmount(resource)` then `damageConditions.Validate`.
  - `Die()` / `EnterCorpseState()` / `DestroyCorpse()`.
  - Override hit routers add `onGotAttacked`, `ApplyBurn`, and `Owner.RegisterKill` on death.
  - `OnStatsRecalculated()` rebuilds `Shield` instances from `Unit.ComponentData.Shields`.
  - `Update()` ticks fire damage and shield visuals.
- **Nested:** `struct CorpseSettings`.

### ResourceTank: class
- **Purpose:** Backing store for a `Resource` value (HP, ammo, currency). HP changes ultimately mutate this.
- **Fields/props:** `Resource resource`; `float Capacity`; `float Value` (setter clamps via `isInfinite` — refuses to decrease when infinite, fires `ValueChanged`); `bool isInfinite`; `bool IsLow/IsEmpty/IsFull`. Events `ValueChanged`, `LowChanged`, `ResourceInsufficient`. `Charge(float)` clamps add.
- **Note:** Setting `isInfinite = true` (via `Unit.HasInfiniteResource`) makes the tank reject any decrease — an alternative infinite-health/ammo lever.

### ImpactDamage: MonoBehaviour
- **Purpose:** Deals damage on physical collision above a velocity threshold (e.g. ramming).
- **Fields:** `float minVelocity`; `Damage damage`; `DamagableResource damagableResource`.
- **Logic:** `OnCollisionEnter2D` — if `enabled`, contact-normal·relativeVelocity `>= minVelocity`, and not excluded (excludes colliders carrying a `Projectile`) → `damagableResource.Damage(damagableResource.GetDamageAmount(damage))`.

### Hazard: MonoBehaviour : IDamageSource
- **Purpose:** A damaging volume/surface (touch damage + optional knockback).
- **Fields:** `Damage damage`; `float contactPushbackForce`.
- **Nested:** `interface IHazardSensor { struct HazardTouchArgs { position, normal, damage } ; OnHazardTouched(args) }`.
- **Logic:** `OnCollisionEnter2D`/`OnTriggerEnter2D` apply optional impulse and call `IHazardSensor.OnHazardTouched` on the toucher with `GetDamage()`.

### Shield: MonoBehaviour
- **Purpose:** Front-line damage absorber backed by its own resource tank.
- **Fields (gameplay):** `Unit unit`; `Resource resource`; `float radius`; `float effectiveness`; `bool useCollider/hasCollider`; `bool BackgroundEnabled`. Visual/animation fields for ring/highlight. Event `Action<Shield> Damaged`.
- **Props:** `float Charge => unit.GetResource(resource)`; `Resource Resource`; `bool IsActive`.
- **Key method:** `Damage TakeDamage(Damage damage)` — if no tank, returns damage unchanged. Computes `num = min(damage.GetScaledDamageAmount(resource) / effectiveness, Charge)`, reduces incoming `damage.amount -= num * effectiveness / damage.GetDamageMultiplier(resource)`, drains `tank.Value -= num`, plays hit anim, fires `Damaged` if any absorbed, and returns the residual `Damage`.
- **Setup:** `Setup(unit, ShieldData, radius, backgroundEnabled, colliderEnabled)`. `effectiveness == 0` logs an error (would div-by-zero).

### AddShieldEffect: ModuleEffect (`IHasDescriptionForUnit`)
- **Fields:** `Resource resource`; `FloatSeries effectiveness`.
- **Logic:** `OnRecalculateUnitStats` → `unit.AddShield(resource, effectiveness.GetElement(Module.Level - 1))`. The `DamagableResource` then instantiates a `Shield` per `Unit.ShieldData` on stat recalc.

### Explosion: struct (`[Serializable]`)
- **Fields:** `float radius`; `List<Damage> damages`; `string sfx`; `bool skipCamShake`. Props: `BurnRadius => radius + 1`, `PushForce`, `Unit Owner`, `Vector2 Position`, `MinMaxFloat Burn`, `RectInt Bounds`.
- **Methods:** `SortDamages()` (descending by amount), `AddDamage(Damage)` (merges by damage type), `Duplicate()`.

### ExplosionComponent: MonoBehaviour : IDamageSource
- **Fields:** `float range`, `float force`; `Damage damage`; `MinMaxFloat burn`; `bool skipCamShake`; `Unit Owner`.
- **Logic:** On `Start`, builds an `Explosion` and calls `ServiceLocator.Get<ExplosionManager>().SpawnExplosion(pos, explosion)`. `GetDamage()` returns `damage`.

### ExplosionManager: MonoBehaviour (service)
- **Purpose:** Central explosion resolver (area damage, push, cell destruction, light, shake, SFX).
- **Key methods:**
  - `SpawnExplosion(Vector2, Explosion)` — duplicates, sorts damages, computes push force, spawns visuals (base prefab from primary damage type + addon prefabs), then `DoExplosionLogic`, light, shake, SFX.
  - `DoExplosionLogic(Explosion)` — `DamageLevel` (destroy/burn/shake cells in `Bounds`), then `Physics2D.OverlapCircleAll(Position, radius)`; for each collider: `PushByExplosion.Push`, and for each `IExplosionListener` in parents (deduped via `triggeredListeners`) call `OnExplosion(explosion)`. Skips trigger colliders not on the Ground layer.
  - `DamageLevel`, `SpawnLight`, `Shake`, `PlaySfx`.
- **Note:** Listener damage is the *full* `explosion.damages` list, resolved per-target by `DamagableResource.TakeDamage(IReadOnlyList<Damage>)`.

### ExplosionVisual / ExplosionVisualSettings
- `ExplosionVisual.Setup(Explosion)` configures particle speed/lifetime/rate from `ExplosionVisualSettings.GetSetting(radius)` (lerp between `presetRadius1` and `presetRadius10`) and recurses into child visuals. `ExplosionVisualSettings` is a ScriptableObject of `ExplosionPreset`s.

### IExplosionListener: interface
- `void OnExplosion(Explosion explosion)`. Implemented by `HealthBase`.

### PushByExplosion: MonoBehaviour
- `Push(Explosion)` → `rigidbody.AddForce((rb.position - explosion.Position).normalized * explosion.PushForce, Impulse)`.

### AddExplosionEffect: WeaponAugmentation (`IProjectileModifier, IHasPerProjectileCost, IHasDescriptionForWeapon`)
- **Fields:** `Resource damageType`; `FloatSeries damageAmount`; per-projectile cost; `bool addImpactExplosion/addTimeoutExplosion`; `float explosionRadiusIncrement`; `FloatSeries burn`. Prop `Damage DamageForCurrentLevel`.
- **Logic:** `ModifyProjectile` adds damage to the projectile's `Explosion`, increments radius, adds burn, and enables impact/timeout explosion behaviours.

### AddImpactExplosionEffect / IncreaseExplosionRadiusEffect: ModuleEffect : IWeaponModifier
- `AddImpactExplosionEffect.Modify` enables `ImpactBehaviour.spawnExplosion` on a `ProjectileWeapon`.
- `IncreaseExplosionRadiusEffect.Modify` adds `increaseAmount` to `weaponBase.Explosion.radius`.

### AddBurnEffect / AddDischargeEffect: WeaponAugmentation
- `AddBurnEffect.ModifyProjectile` adds `BurnAmountAtCurrentLevel` to `projectile.Burn`.
- `AddDischargeEffect.ModifyProjectile` mutates `projectile.DischargeData` (chain length, damage) and optionally enables impact/timeout discharge.

### DrainResourceEffect: ModuleEffect (`IHasDescriptionForUnit`)
- `OnUpdate` subtracts `DrainRateForCurrentLevel * Time.deltaTime` from a unit's tank (used for upkeep mechanics).

### DamageHighlight: MonoBehaviour
- Listens to `HealthBase.onDamage`; swaps sprite materials to a highlight material for `duration` seconds after each hit.

### HealthVisualizer: MonoBehaviour
- Toggles `HealthVisual.gameObject`s active when `CurrentHealth / MaxHealth < threshold` (damage states).

### HealthbarOwner / HealthbarManager / HealthbarWidget
- `HealthbarOwner` declares which `Resource`s (the unit's HP resource plus each shield resource) to show; raises `Damaged`/`StatsRecalculated`. `HealthbarManager` (service) creates/removes `HealthbarWidget`s (spawns on damage if not already shown; skips bosses → dedicated `BossHealthbar`). `HealthbarWidget` builds `ResourceBar`s for each resource.

### ImpactVisualizer / ImpactParticle / DestroyOnImpact / DestroyIfNotTouch
- Cosmetic/utility collision components: spawn impact particles (velocity-gated for `ImpactParticle`), destroy self on hard impact, or destroy if no touchscreen.

### StatusEffectForUnit / StatusEffectParticleManager
- `StatusEffectForUnit.Update` calls `StatusEffectParticleManager.EmitForUnit` while `unit.ComponentData.IsOnFire`. The manager emits burn particles for on-screen burning cells and units and drives burn SFX volume.

## Modding Notes

All damage to *units* (player and enemies) funnels through **`DamagableResource.Damage(float amount)`** — this is the highest-leverage patch point. Simple `Health` props use `Health.Damage(float)` instead (a separate private method).

### God mode / invincible player — best single patch
**Target: `DamagableResource.Damage(System.Single)` — Harmony `Prefix` returning `false` for the player.**

This is the single best method because *every* damage path (projectile, explosion, hitscan, hazard, electricity, cell contact, ramming, and even fire ticks) eventually calls this one instance method with the final scaled amount. Patching it once blocks them all, before the tank is touched. Identify the player via the owning `Ship`/`Unit` (e.g. compare against `GameController.Ships`/the player faction) so enemies still take damage.

```csharp
[HarmonyPatch(typeof(DamagableResource), "Damage", new[] { typeof(float) })]
static class NoPlayerDamage {
    static bool Prefix(DamagableResource __instance) {
        // return false to skip the original (no HP loss) for the player only
        return !IsPlayer(__instance); // e.g. __instance.GetComponent<Ship>() != null
    }
}
```

Alternatives (weaker):
- **`DamagableResource.IsInvincible = true`** — the *built-in* invincibility flag (toggled by `DebugMenu.ToggleInvincibility`). Note it does **not** block damage; it only floors the tank at `1` when a hit would bring it to `≤ 0` (`if (IsInvincible && tank.Value <= 0f) tank.Value = 1f;`). HP still chips down to 1 and `onDamage`/i-frames still fire. Set it via reflection/property on the player's `DamagableResource` for a no-code-patch option.
- **`Unit.HasInfiniteResource = true`** (debug "infinite resource") — sets `ResourceTank.isInfinite`, which makes `ResourceTank.Value` reject *any* decrease. This yields infinite HP **and** infinite ammo/currency for that unit. Toggled by `DebugMenu.ToggleInfiniteResource`.
- **`DamagableResource.GetDamageAmount` Prefix → return 0** — also stops HP loss, but is called from more places and still runs shield logic; less clean than patching `Damage`.

### One-shot enemies
- **`DamagableResource.Damage(float)` Prefix**, for non-players, rewrite the amount to a huge value (e.g. call `__instance.Tank.Value = 0` / set `amount` via `ref`), or
- **`DamagableResource.GetDamageAmount(Damage)` Postfix** → multiply `__result` by a large factor (applies after matrix scaling and shield pass-through). Shields will still absorb first; to bypass shields, also neutralize `Shield.TakeDamage` (below).

### Damage multipliers (global)
- **`DamagableResource.Damage(float amount)` Prefix** with `ref float amount` → `amount *= multiplier` (cleanest single global multiplier; affects all units — gate by faction if you only want enemy damage scaled).
- Or **`Damage.GetScaledDamageAmount` / `Resource.GetDamageMultiplierOn` Postfix** to scale at the type-matrix level (affects how damage types interact, before shields).
- Per-source scaling: patch the relevant `GetDamage()` producers (`Projectile.GetDamage`, `ExplosionComponent.GetDamage`, `Hazard.GetDamage`).

### Infinite shields
- **`Shield.TakeDamage(Damage)` Prefix** → `return false` won't work directly (it has a return value); instead use a **Prefix that sets `__result = damage` and returns `false`** to pass the hit through untouched without draining `tank.Value`, *or* a **Postfix that refills** `unit.GetTank(resource).Value = capacity`. Keeping `Charge > 0` also keeps the shield `IsActive` (which is what `DamagableResource.HasActiveShield` checks to route subsequent hits into shields).
- To make a shield *block everything*: in a `Shield.TakeDamage` Prefix, set the outgoing `damage.amount = 0` and return it, so `DamagableResource.TakeDamage(Damage)` early-returns.

### Useful fields/hooks for combat mods
- `DamagableResource.iFrameDuration` — raise for longer invulnerability windows after each hit.
- `DamagableResource.damageBlockers` (`List<GameObject>`) — if all are active, `IsDamageBlocked()` returns true and the unit takes no damage (a built-in conditional-immunity mechanism).
- `DamageConditions.minDamage` — raise to make a unit ignore small hits.
- `HealthBase.onDamage` / `onDeath` (`UnityEvent`) — subscribe for reactive mod logic without patching.
- `Unit.KilledAnotherUnit` event + `Unit.RegisterKill` — kill-tracking hooks.
- `ExplosionManager.SpawnExplosion` / `DoExplosionLogic` — patch to globally rescale explosion radius/damage or add custom AOE behavior.

> **Reflection note (Mono/BepInEx):** `DamagableResource.Damage(float)` and `Health.Damage(float)` are the load-bearing methods but `Damage` is also the *struct* type name — when targeting via `HarmonyPatch` use the explicit argument-type array (`new[] { typeof(float) }`) to disambiguate the method from the type and from the `Damage(Damage)`/`Damage(IReadOnlyList<Damage>)` overloads (those are named `TakeDamage`, but `Health.Damage` and `DamagableResource.Damage` differ in accessibility — `DamagableResource.Damage` is `public`, `Health.Damage` is `private`).
