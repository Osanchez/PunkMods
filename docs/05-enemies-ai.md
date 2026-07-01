# Enemies, AI & Bosses
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK enemies are ordinary game `Unit`s (see the entity/component docs) that carry a few AI-specific components. Their lifecycle:

1. **Generation / distribution.** During level generation `EnemyGenerator.PlaceEnemies` walks every `LevelGraphNode`, derives a per-room "power level" budget from the dominant `Ecosystem`'s difficulty curves (scaled by `PoI.difficultyMultiplier`), then repeatedly draws enemies from a weighted `Ecosystem.EnemyDistribution` and spends their `powerLevel` until the budget is exhausted. Each drawn enemy is instantiated as an `EntityData` and added to the level `EntityManager`. Its `Unit.Data.SpawnRoomIndex` is stamped so AI movement can be confined to the spawn room.
2. **Perception.** A `Vision` component (a `ComponentScanner<Unit>`) periodically `OverlapCircle`s for nearby `Unit`s and line-of-sight tests them against blocking layers. `AIAgent` consumes the visible list each frame, sorting units into friends/enemies via `Faction` allies/enemies lists (plus per-instance black/white lists), and picks a `currentTarget`.
3. **Behaviour.** An `AIAgent` plus a Unity-GameObject-based behaviour tree drives the enemy. The tree is a `StateMachine` whose children are `State` GameObjects; each `State` holds Action components (movement, aiming, shooting, etc.) that run while it is active, and `Transition` components whose `Condition` lists decide when to switch states.
4. **Reaction.** `AggroWhenHit` / `WaitForTargetAction` / `GotAttackedCondition` let an enemy acquire a target and switch to attack or flee behaviour when damaged. `Unit.IsAfraidOf` (target `powerLevel` > `maxPowerLevelToFight`) governs flee-vs-fight.
5. **Bosses.** A boss is just an enemy whose state hierarchy contains a `BossStateActivator`; entering that state notifies the global `BossStateManager`, which the `BossHealthbar` UI listens to. Multi-phase bosses use ordinary states + `RepeateChildrenAction` loops.

## AI Behavior Model

This is the heart of enemy AI and the most important thing for modders to understand. It is a **hierarchical, GameObject-driven finite state machine** layered on Unity's `SetActive` mechanism — there is no scripting/asset language; everything is components arranged in the prefab hierarchy.

### StateMachine
`StateMachine` (a `MonoBehaviour`) sits on a parent object. On `Awake` it **deactivates all of its child GameObjects**. On `Start` it calls `ChangeState(startState)`. `ChangeState`:
- deactivates the current state (`State.Deactivate()` → `gameObject.SetActive(false)`),
- sets the new current state and activates it (`SetActive(true)`),
- caches `currentState.GetComponents<Transition>()`.

Every `Update`, the machine evaluates its cached transitions **in component order** and takes the first whose `IsFulfilled()` returns true, calling `ChangeState(target, allowTransitionToSelf:true)`. `returnToDefaultOnDisable` (default true) resets to `startState` when the machine is disabled. `logTransitions` prints each transition to the Unity log — handy for reverse-engineering an enemy's tree.

### State
`State` is a marker `MonoBehaviour`. Activating/deactivating it simply enables/disables its GameObject, which in turn enables/disables all the **Action** components parented under it — that is how actions "run only in their state" (they hook `OnEnable`/`OnDisable`/`Update`). `State.StateMachine` is resolved lazily by walking up the parent chain. Exposes `Activated`/`Deactivated` events (used by `RepeateChildrenAction`). `ChangeState()` is a convenience that transitions the machine to this state.

### Transition
`Transition` lives **on a State** and points at a destination `State` (or, if `randomDestination`, draws one from a weighted `StateDistribution`). It owns a `List<Condition> conditions` and an `evaluationMode` (`And`/`Or`):
- `IsFulfilled()` returns true immediately if the list is empty.
- Each condition's result is XORed with its `IsInverted` flag.
- `Or`: true as soon as one condition passes. `And`: false as soon as one fails, otherwise true.

### Condition
`Condition` is an abstract `MonoBehaviour` with a serialized `invert` flag (`IsInverted`) and abstract `IsFulfilled()`. Conditions are pure predicates — they read agent/unit/sensor state and never mutate the machine. Many also implement `IValidatable` (editor wiring checks).

### Actions
Actions are **not** a single base class — they are independent `MonoBehaviour`s placed under a `State` that do their work in `OnEnable`/`OnDisable`/`Update`/`FixedUpdate`. Movement actions share the abstract `MovementAction` base; aiming actions share `AimAction`; the rest are standalone. Because activation is just `SetActive`, an action with side effects on enable (e.g. `ForgetTargetAction`, `PushSelfAction`, `SelfDestructAction`) fires once per state entry, while polling actions (e.g. `MoveTowardsTargetAction`, `ShootAction`) run every frame the state is active.

### MovementAction (shared movement base)
`MovementAction` is the abstract base for pathfinding moves. It integrates with the A* Pathfinding Project (`Seeker`/`Path`) and `UnitMovement`:
- On `OnEnable` it snapshots the movement's current tuning values, optionally applies a `PushMovement`/`SwimmingMovement` override (named or inline `ValueOverride`), then kicks off `FindPath()` (abstract, supplied by each subclass).
- `Update` advances along `path.vectorPath` waypoints; within `arrivalDistance` of the final waypoint it either transitions to `transitionOnArrival`, fires `transitionAfterFirstPush` (for push-style movement), or stops.
- `OnDisable` restores original movement values and (if `stopOnExit`) stops the unit.
- `restrictMovementToSpawnRoom` + `roomRadiusMultiplier` clamp the destination to a circle around the unit's `SpawnRoomIndex` room center (via `RestrictTargetPositionToSpawnRoom`), keeping enemies tethered to where they spawned.

### AimAction (shared aiming base)
`AimAction` rotates a `BarrelTransform` toward a direction at `rotationSpeed` deg/s (clamped down to `Shooter.Weapon.MaxRotationSpeedWhileShooting` while actually firing). Subclasses choose the direction (current target, last-known position, random, or movement direction).

### Putting it together
A typical ranged enemy prefab looks like:
```
Unit (+ AIAgent, Vision, Seeker, UnitMovement, Shooter, AggroWhenHit)
└─ StateMachine (startState = Idle)
   ├─ Idle      [EnemyVisibleCondition → Chase]
   ├─ Chase     (MoveTowardsTargetAction, AimAtTargetAction) [TargetIsCloseCondition → Attack]
   ├─ Attack    (StopAction, AimAtTargetAction, ShootAction)  [TimeoutCondition / !TargetVisibleCondition → Chase]
   └─ Flee      (FleeFromTargetAction)   ← entered via WaitForTargetAction when IsAfraidOf(target)
```

## Class Index

| Class | Kind | Role |
|---|---|---|
| `AIAgent` | SavableComponent | Perception + target selection brain |
| `Vision` | ComponentScanner<Unit> | Periodic LoS unit detection |
| `ComponentScanner<T>` | MonoBehaviour (generic) | Overlap+raycast sensor base |
| `Faction` | ScriptableObject | Ally/enemy relationship data |
| `Enemy` | SavableComponent | Marks unit as enemy; installs embedded module/weapon; kill tracking |
| `EnemyGenerator` | IInitializable | Power-budgeted enemy placement |
| `EnemyGroup` | ScriptableObject | Named array of enemy `Unit`s |
| `EnemyList` | MonoBehaviour | Debug enemy-spawn palette UI |
| `Ecosystem` | ScriptableObject | Enemy distribution + difficulty curves |
| `StateMachine` | MonoBehaviour | FSM driver |
| `State` | MonoBehaviour | A behaviour state (GameObject) |
| `Transition` | MonoBehaviour | Condition-gated state edge |
| `Condition` | abstract MonoBehaviour | Predicate base |
| `MovementAction` | abstract MonoBehaviour | Pathfinding move base |
| `AimAction` | abstract MonoBehaviour | Barrel-rotation aim base |
| `Aimer` | MonoBehaviour | Standalone aim helper w/ effects |
| 13× `*Action` (move/aim) | MonoBehaviour | Concrete movement/aim actions |
| 9× `*Action` (other) | MonoBehaviour | Shoot/torque/animator/etc. actions |
| 12× `*Condition` | Condition | Concrete predicates |
| `DamageConditions` | [Serializable] | Damage-amount filter |
| `AggroWhenHit` | MonoBehaviour | Aggro on taking damage |
| `Scanner` / `ScannerGenerator` / `ScannerAreaGenerator` | mixed | Map-reveal scanner objects + placement |
| `EnemyTrackingSystem` / `EnemyTrackingCamera` | MonoBehaviour | Station-unlock enemy tracking cameras |
| `Crawler` / `CrawlerLeg` | MonoBehaviour | Procedural multi-legged walker |
| `ChargerHead` | MonoBehaviour | Charging ram-damage head |
| `Eye` | MonoBehaviour | Empty stub creature |
| `Hook` / `HookTargetSeeker` | MonoBehaviour | Grappling-hook tether + target select |
| `MinionOwnerSetter` / `HomingTargetFromAIAgent` | MonoBehaviour | Minion/projectile wiring helpers |
| `MinionSpawnerWeapon` | WeaponBase | Spawns minion units as "projectiles" |
| `MinionCountWidget` / `MinionsWidget` | MonoBehaviour | Minion-count HUD |
| `BossStateManager` | plain class | Tracks current boss, fires enter/exit events |
| `BossStateActivator` | MonoBehaviour | Enters boss state while active |
| `BossHealthbar` / `BossHealthbarRow` | MonoBehaviour | Segmented boss health UI |
| `DestinationSource` (+2 subclasses) | abstract MonoBehaviour | Pluggable destination providers |

## Classes

### AIAgent : SavableComponent<AIAgent.Data>
- **Purpose:** The perception/targeting brain shared by every AI unit. Holds the visible friend/enemy sets and the current target; this is the object virtually all Actions/Conditions read from.
- **Key fields:** `thisUnit` (`Unit`), `vision` (`Vision`), `seeker` (A* `Seeker`); runtime `visibleEnemies`/`visibleFriends` (`HashSet<Unit>`), `currentTarget`, `targetLastKnownPosition`.
- **Data (saved):** `enemyBlackList` / `friendWhiteList` (`HashSet<int>` of entity instanceIds) — per-instance overrides of faction relationships; persisted via memento.
- **Key properties:** `Unit`, `Seeker`, `VisibleFriendCount`, `VisibleEnemyCount`, `SeesFriend`, `SeesEnemy`, `Target`, `HasTarget`, `IsTargetVisible`, `Position`, `TargetLastKnownPosition`.
- **Key methods:** `Update()` (refreshes lists, updates last-known position, selects target); `RefreshEnemyAndFriendLists()`; `SelectTargetFromVisibleEnemies()` (prefers a unit it `IsAfraidOf`, else first visible enemy); `HandleHitBy(Unit)` (blacklists & targets attacker, raises `GotAttackedByUnit`); `SetTarget`/`ForgetTarget`; `IsAfraidOf(Unit)`/`IsAfraidOfTarget()`; `SeesUnit(Unit)`.
- **Event:** `Action<Unit> GotAttackedByUnit`.
- **Relationships:** Reads `Vision.VisibleUnits`; delegates friend/enemy/fear checks to `Unit`. Central dependency of nearly every Action and Condition below.

### Vision : ComponentScanner<Unit>
- **Purpose:** Periodic line-of-sight detection of nearby units feeding `AIAgent`.
- **Key fields:** `refreshDelay`. Inherited (from `ComponentScanner`): `Range` (settable), `targetLayers`, `blockingLayers`, `ignoredColliders`.
- **Key methods:** `Update()` re-`Scan()`s once `refreshDelay` elapses (start time jittered to spread load); `IsVisible(Unit)` excludes invisible units (`ComponentData.IsInvisible`). `VisibleUnits` exposes the result.

### ComponentScanner<T> : MonoBehaviour where T : Component
- **Purpose:** Reusable proximity sensor base (used by `Vision`, `EnemyTrackingSystem`, `HookTargetSeeker`).
- **Key fields:** `range`/`Range`, `targetLayers`, `blockingLayers`, `ignoredColliders`; shared static buffers sized `maxVisibleUnits = 1000`.
- **Key methods:** `Scan()` does `Physics2D.OverlapCircleNonAlloc` then a `LinecastNonAlloc` LoS test per hit (respecting `ignoredColliders`), keeping unblocked components for which `IsVisible(component)` is true. `VisibleComponents` exposes the set. `IsVisible` is `virtual` (override point).

### Faction : ScriptableObject `[CreateAssetMenu "Punk/Faction"]`
- **Purpose:** Defines inter-group relationships. (Note: the `fileName` attribute is mislabelled "Weapon".)
- **Key fields:** `allies` (`List<Faction>`), `enemies` (`List<Faction>`); exposed as `Allies`/`Enemies`. Relationship is membership-based — a unit is friends/enemies with another if the other's faction is contained in this faction's allies/enemies list (see `Unit.IsFriendsWith`/`IsEnemiesWith`).

### Enemy : SavableComponent<Enemy.Data> (ModuleGridOwner.IModuleGridInitializer)
- **Purpose:** Tags a `Unit` as an enemy, installs its built-in loadout, and registers kills.
- **Key fields:** `embeddedModule` (`ModuleData`), `primaryWeapon` (`WeaponModuleData`), `coopResourceMultiplier`, `countsAsKill`.
- **Data (saved):** `coopResourceMultiplier`, `countsAsKill`; `Data.OnDestroy` calls `RunData.RegisterEnemyKilled(entity)` when `countsAsKill`.
- **Key methods:** `Initialize(IModuleGrid)` deep-copies the embedded module into the unit's `SimpleModuleGrid` (scaling `ModifyResourceCapacity` by `coopResourceMultiplier` in co-op) and installs `primaryWeapon` into the `PrimaryWeapon` cluster.

### EnemyGenerator : IInitializable
- **Purpose:** Populates a generated level with enemies under a per-room power budget.
- **Key fields:** `maxPlacementTries = 5`, counters `placedEnemies`/`failedenEmies`, `levelElementsCollection`, `poiRegisty`.
- **Key methods:** `PlaceEnemies(Level, Rnd)` iterates graph nodes, multiplies by `PoI.difficultyMultiplier`; `PlaceBasedOnEcosystem(...)` computes `remainingPowerLevel = radius * difficulty * multiplier`, stores it on the node (`enemyPowerLevel`), and draws enemies from `Ecosystem.enemies` while budget remains — ignoring entries whose `powerLevel` exceeds the budget or whose `minDistanceFromCenter > node.distanceFromCenter`; spends `enemy.powerLevel` per spawn; stamps `Unit.Data.SpawnRoomIndex`. `TryGetEmptyPositionInRoom`/`GetRandomPositionInRoom` find a non-`blocksEnemyPlacement` cell within the room (respecting `enemyPlacementDeadZoneRadius`).
- **Modder note:** This is *the* hook for tuning spawn counts/composition — power level math lives entirely here and in `Ecosystem`.

### Ecosystem : ScriptableObject `[CreateAssetMenu "Punk/Ecosystem"]`
- **Purpose:** Per-biome enemy roster + difficulty scaling consumed by `EnemyGenerator`.
- **Key fields:** `enemies` (`EnemyDistribution`, a weighted `Distribution<Enemy,…>`), `plants`, `entityPlants`, `roomDifficultyRangeClose`/`roomDifficultyRangeFar` (`MinMaxFloat`), `roomDifficultyScaleCurve` (`AnimationCurve`).
- **Nested `Enemy` struct:** `minDistanceFromCenter`, `powerLevel`, `entity` (`SavableEntity`) — these are the per-enemy weights/costs.

### EnemyGroup : ScriptableObject `[CreateAssetMenu "Punk/Enemy group"]`
- Simple container: `Unit[] enemies`. Used where a fixed named set of enemies is needed (e.g. scripted encounters).

### EnemyList : MonoBehaviour
- **Purpose:** Debug/dev palette — instantiates a `SpawnEnemyButton` per savable `Unit` prefab; left-click places the selected entity at the cursor (raycast onto a Z-plane), right-click clears selection. Not part of normal gameplay AI.

### StateMachine / State / Transition / Condition
Documented in **AI Behavior Model** above. Key serialized fields recap:
- `StateMachine`: `startState`, `returnToDefaultOnDisable` (def. true), `logTransitions`.
- `State`: events `Activated`, `Deactivated`.
- `Transition`: `evaluationMode` (`And`/`Or`), `conditions`, `randomDestination`, `state`, `states` (`StateDistribution`).
- `Condition`: `invert` (`IsInverted`).

### MovementAction / AimAction
Abstract bases documented in **AI Behavior Model**. `MovementAction` notable serialized fields: `agent`, `movement`, `arrivalDistance`, `transitionOnArrival`, `stopOnExit`, `useNamedOverride`, `restrictMovementToSpawnRoom`, `roomRadiusMultiplier` (0.8), `namedOverride`, `pushMovementOverride`, `swimmingMovementOverride`, `changeStateAfterFirstPush`, `transitionAfterFirstPush`. `AimAction` fields: `barrel` (`BarrelTransform`), `rotationSpeed`, `shooter`.

### Aimer : MonoBehaviour
- **Purpose:** Standalone (non-state) barrel aimer with a pluggable effect chain. Rotates `barrel` toward `targetPosition` at `rotationSpeed`. `AimAt(position)` runs each `IAimEffect.Apply` (gathered via `GetComponents<IAimEffect>()` on `Awake`) before storing the target. Nested interface `IAimEffect`.

### Movement & Aim Actions (table)

| Action | What it does | Key fields |
|---|---|---|
| `MoveTowardsTargetAction` | Repeatedly re-paths to the current target while active | `pathFindingDelay` (0.25) |
| `MoveToTargetLastKnownPositionAction` | Paths to `agent.TargetLastKnownPosition` (± random offset) | `offset` |
| `MoveAroundTargetAction` | Paths to a point orbiting the target at a random distance/angle (fixed or relative direction) | `useFixDirection`, `distance`, `angleDeltaFromTarget`, `direction` |
| `MoveAroundOwnerAction` | Same orbit logic but around `unit.Owner` (minion behaviour) | `unit`, `distance`, `angleDeltaFromTarget` |
| `MoveAwayFromTargetAction` | Each frame moves directly away from target (no pathfinding) | `movement`, `agent`, `stopOnExit` |
| `MoveInRandomDirectionAction` | A* `RandomPath` of random length within `distance` | `distance` |
| `MoveToPositionAction` | **Stub** — fields declared, no logic in this build | `agent`, `destinationSource`, `movement`, `arrivalDistance`, `transitionOnArrival`, `stopOnExit` |
| `FleeFromTargetAcion` | A* `FleePath` away from the target, re-pathing on a delay | `distance`, `pathFindingDelay` |
| `FleeFromTargetLastKnownPositionAcion` | `FleePath` away from target's last-known position | `distance` |
| `StopAction` | Calls `movement.Stop()` on enable | `movement` |
| `AimAtTargetAction` | Aims barrel at current target; exposes `BarrelTargetAngle` (used by `ShootAction`) | `agent` |
| `AimAtLastKnownPositionAction` | Aims at `TargetLastKnownPosition` | `agent` |
| `AimInRandomDirectionAction` | Wobbles aim by random deltas on a timer | `directionChangeDelay`, `maxDirectionChangeDelta` |
| `AimInMoveDirection` | Aims barrel along `movement.MovementDirection` | `movement` |

### Other Actions (table)

| Action | What it does | Key fields |
|---|---|---|
| `ShootAction` | Holds fire until past `delay`, target within `shootingDistance`, aim within `maxAimAngle`, and resource recharged; toggles `Shooter` shooting | `shooter`, `aimAtTargetAction`, `agent`, `delay`, `resourceAmountToWait`, `maxAimAngle`, `stopWhenAngleTooBig`, `shootingDistance`, `stopWhenDistanceTooBig` |
| `ShootComplexAction` | Multi-shooter firing: `Continous`/`Grouped`/`Once`, instant or trigger-pull, with bursts and group cycling | `shooters[]`, `delay`, `behaviour`, `shootMethod`, `fireRate`, `groupSize`, `groupCount`, `burstCount`, `burstDelay`, `triggePullDuration` |
| `ActivateShooterAction` | Turns a `Shooter` on after `delay`; off on exit | `shooter`, `delay` |
| `ApplyTorqueAction` | Spins a rigidbody (Left/Right/Random) up to `maxAngularVelocity` | `rigidbody`, `direction`, `initialTorque`, `torque`, `maxAngularVelocity` |
| `PushSelfAction` | One-shot random force + torque impulse on enable | `rigidbody`, `force`, `torque` |
| `ReduceAngularVelocityAction` | Multiplies angular velocity by a factor on enable | `rigidbody`, `multiplyier` |
| `ChangeAnimatorParamAction` | Sets an animator (or `AnimatorGroup`) bool param on enable, optionally reverts on disable | `targetType`, `animator`, `animatorGroup`, `paramName`, `paramType`, `boolValue`, `revertOnDisable` |
| `ForgetTargetAction` | Calls `agent.ForgetTarget()` on enable | `aiAgent` |
| `SelfDestructAction` | Calls `DamagableResource.Die()` on enable (suicide/explode-on-contact enemies) | `damagableResource` |
| `WaitForTargetAction` | While active, routes to `attackState` or `fleeState` based on `IsAfraidOf(target)`; also listens to `GotAttackedByUnit` | `aiAgent`, `attackState`, `fleeState` |
| `RepeateChildrenAction` | Loops a sub-sequence N times: on each `loopbackState` activation, re-enters `firstState` until `loopCount` exhausted, then `nextState` | `firstState`, `loopbackState`, `nextState`, `loopCount` |

### Conditions (table)
All inherit `Condition` (so all support the `invert` flag).

| Condition | True when… | Key fields |
|---|---|---|
| `TargetVisibleCondition` | `agent.IsTargetVisible` (can temporarily widen `Vision.Range`) | `aIAgent`, `overrideVisionRadius`, `vision`, `scanRadius` (20) |
| `EnemyVisibleCondition` | `agent.SeesEnemy` (can widen vision range) | `aIAgent`, `overrideVisionRadius`, `vision`, `scanRadius` (20) |
| `TargetIsCloseCondition` | distance to target < `threshold` | `agent`, `threshold` |
| `TargetIsAheadCondition` | target within `maxAngle` of agent's right vector | `agent`, `maxAngle` |
| `OwnerIsWithinRangeCondition` | distance to `unit.Owner` < `maxDistance` | `unit`, `maxDistance` |
| `HasOwnerCondition` | `unit.Owner != null` | `unit` |
| `HasLessMinionCondition` | `unit.Minions.Count < threshold` | `unit`, `threshold` |
| `GotAttackedCondition` | unit took damage since state entry (listens `GotAttackedByUnit`) | `aiAgent` |
| `IsInLightCondition` | `lightSensor.IsInLight` | `lightSensor` |
| `TimeoutCondition` | time since state entry > random `time` (rolled per entry) | `time` (`MinMaxFloat`) |

### DamageConditions : [Serializable]
- Not a `Condition` subclass — a plain serializable filter. `Validate(Damage)` returns `damage.amount >= minDamage`. Used by damage-reactive components (e.g. aggro/flinch gating). Field: `minDamage`.

### AggroWhenHit : MonoBehaviour
- **Purpose:** Makes a passive enemy turn hostile after taking enough damage. Listens to `DamagableResource.onGotAttacked`; when current health ≤ `MaxHealth * (1 - minMissingHealthRatioForAggro)` it calls `aiAgent.HandleHitBy(attacker)`.
- **Key fields:** `damagableResource`, `aiAgent`, `minMissingHealthRatioForAggro`.

### DestinationSource (abstract) + subclasses
- `DestinationSource`: abstract `Vector2 GetDestination()` provider.
- `TargetLastKnownPosition`: returns `agent.TargetLastKnownPosition` (± `randomDisplacement`), optionally snapped to navmesh (`adjustToNavmesh`). Fields: `agent`, `randomDisplacement`, `adjustToNavmesh`. (`MoveToTargetLastKnownPositionAction.Copy` / `FleeFromTargetLastKnownPositionAcion` read this.)
- `RandomPoisitionAroundUnitDestinationSource`: random point at `distance` around `agent.Position`, navmesh-snapped. Fields: `agent`, `distance`, `adjustToNavmesh`. (`MoveInRandomDirectionAction.Copy` reads this.)

### Scanner objects
- **Scanner : SavableComponent<Scanner.Data>** — interactable map-reveal device. `OnUseActivated(Interactor)` reveals its scanned area once (`ServiceLocator.Get<ShipMenuToggler>().RevealScannedArea(...)`). Saved `Data`: `areaId` (byte), `isUsed`. Visual fields: `mapZoomCurve`, `mapZoomDuration`, `scanRevealDelay`, `scanAnimationDuration`, `scanShowcaseColor`.
- **ScannerGenerator : IInitializable** — places scanner PoIs at level-gen: one central (`PlaceCenterScanner`, nearest node to ideal distance) plus radial ones (`PlaceRadialScanners`, within `scannersDistanceFromCenter`, ≥ `scannersMinDegree` apart), then spreads scanner biomes. Driven by `LevelGeneratorConfig.scannerCount`/`scannerPoI`.
- **ScannerAreaGenerator : IInitializable** — Burst `IJobParallelFor` that assigns each level cell to its nearest scanner's `areaId` (Voronoi partition into `level.scannerAreas`).

### Enemy tracking (station-unlock gating)
- **EnemyTrackingSystem : ComponentScanner<Unit>** — spawns `cameraCount` `EnemyTrackingCamera`s in a ring; periodically `Scan()`s and feeds visible units to each camera. `IsVisible` only counts units with `Unit.BlocksStationUnlock`. `SeesEnemy` true while any such unit is in range. Fields: `refreshDelay`, `cameraPrefab`, `cameraCount`, `camerasParent`.
- **EnemyTrackingCamera : MonoBehaviour** — one wedge of the ring (`VisionAngle`). Picks a `Target` among in-angle visible units and rotates `rotatingPart` to track it; drives an `"Active"` animator bool. Fields: `rotatingPart`, `animator`.

### Creatures

### Crawler : MonoBehaviour
- **Purpose:** Procedural spider/centipede locomotion driven by a `PushMovement` body. Spawns `legCount` `CrawlerLeg`s (alternating left/right) on `Awake`. Each `Update`, ungrounded idle legs raycast (toward movement direction when moving, random directions when stopped) for "Ground" and step there; forces a step after `forceStepTravelDistance` of travel. Step duration scales with body speed (`stepDurationMultiplierAtFullSpeed`, `maxRigidbodySpeed`).
- **Key fields:** `rigidbody2D`, `legPrefab`, `legCount`, `legLength`, `legMoveDuration`, `forceStepTravelDistance`, `movement` (`PushMovement`), `stepDurationMultiplierAtFullSpeed`, `maxRigidbodySpeed`.

### CrawlerLeg : MonoBehaviour
- **Purpose:** A single procedurally-animated, mesh-generated leg. Tweens its tip (`DOVirtual.Float`) along a Bézier path to a raycast hit, generating a segmented-pipe mesh each frame; un-grounds itself if its terrain support cell is destroyed (`LevelChangeBuffer.CellsChanged`). Exposes `IsGrounded`, `IsMoving`, `IsActive`, `MaxLength`, `IsLeft`, `IsEndTooFar`, `CurrentLength`. Large set of visual tuning fields (control-point lengths, curves, segment sizing). Mostly cosmetic — relevant to creature visuals, not combat.

### ChargerHead : MonoBehaviour
- **Purpose:** The damaging ram of a charging enemy. Each `FixedUpdate` `CircleCast`s ahead along its velocity; on hit (throttled by `minDamageDelay`) it delivers `hazard.GetDamage()` to every `Hazard.IHazardSensor`, pushes the victim (`pushForce`) and recoils itself (`pushBackForce`).
- **Key fields:** `hazard`, `rigidbody`, `radius`, `collisionLayerMask`, `minDamageDelay`, `pushForce`, `pushBackForce`.

### Eye : MonoBehaviour
- Empty stub — `Start`/`Update` are no-ops in this build (placeholder creature/decoration).

### Hook : MonoBehaviour
- **Purpose:** Grappling-hook tether (player/ship utility that interacts with grabbable enemies/objects). `Activate()` toggles attach/detach; while attached, `FixedUpdate` applies a spring (`stiffness`) when beyond `length`, reeling the attached body toward the ship (capped by `maxVelocityTowardsShip`) and pulling the ship back by `shipForceMultiplier`.
- **Key fields:** `targetSeeker` (`HookTargetSeeker`), `shipRigidbody`, `length`, `stiffness`, `maxVelocityTowardsShip`, `shipForceMultiplier`. Props: `HasAttachment`, `AttachedRigidbody`.

### HookTargetSeeker : ComponentScanner<Grabbable>
- Each `Update` scans for `Grabbable`s and selects the closest within `maxAngle` of the hook's `-up` axis (`SelectedTarget`). Field: `maxAngle`.

### Minion support
- **MinionSpawnerWeapon : WeaponBase (IHasSpeedProperty)** — fires by *instantiating minion `Unit`s* instead of projectiles. `FireSingle` creates the `MinionPrefab` entity at the barrel, optionally aligns it to the shoot direction, sets its velocity, and raises `OnPrepareMinion`. Props: `MinionPrefab`, `ProjectileSpeed`(+`Variance`), `Speed`, `VelocityInfluenceData`, `MinionFacesShootDirection`. Built from `MinionSpawnerWeaponData`.
- **MinionOwnerSetter : MonoBehaviour** — subscribes to a `WeaponHolder`'s `MinionSpawnerWeapon.OnPrepareMinion` and calls `minion.SetOwner(owner, connectionType)` so spawned minions know their master. Fields: `weaponHolder`, `owner`, `connectionType`.
- **HomingTargetFromAIAgent : MonoBehaviour** — when a `ProjectileWeapon` fires a `PhysicsProjectile`, sets `projectile.Target = agent.Target.gameObject` so AI projectiles home. Fields: `weaponHolder`, `agent`.
- **MinionCountWidget / MinionsWidget : MonoBehaviour** — HUD counting a ship's minions per `Unit.OwnerConnectionType` slot (`PrimaryWeapon`, `SecondaryWeapon`, `Active1/2/3`), reading `SpawnMinionModule.Level` as the cap and flashing when the max-minion event fires.

### Bosses

### BossStateManager (plain class)
- **Purpose:** Global service tracking the active boss. `EnterBossState(SavableEntity)` sets `CurrentBoss` and fires `EnteredBossState`; `ExitBossState(entity)` fires `ExitedBossState` only if `entity` is the current boss. Events: `EnteredBossState`, `ExitedBossState`. Property: `CurrentBoss`. Retrieved via `ServiceLocator`.

### BossStateActivator : MonoBehaviour
- **Purpose:** Bridges the FSM to the boss system. Because it's parented under a `State`, its `OnEnable`/`OnDisable` call `bossStateManager.EnterBossState`/`ExitBossState(savableEntity)` — i.e. the boss "is in boss mode" exactly while that state's GameObject is active. Resolves its `SavableEntity` from parents on `Awake`.

### BossHealthbar : MonoBehaviour
- **Purpose:** Segmented boss HP UI bound to a `ResourceTank`. `Assign(ResourceTank)` rebuilds rows; `CreateRows` splits capacity across `maxUnitPerRow` rows of `BossHealthbarRow`. `Update` animates a trailing "delta" bar (`deltaMoveSpeed`/`deltaMoveDelay`) and distributes current/delta values across rows. `Show()`/`Hide()` drive a `"Visible"` animator bool.
- **Key fields:** `rowPrefab`, `animator`, `unitSize`, `rowSpacing`, `maxUnitPerRow`, `deltaMoveSpeed`, `deltaMoveDelay`.

### BossHealthbarRow : MonoBehaviour
- One row of pips. `Init(Resource, capacity, unitSize)` sizes/colours it; `SetValue(value, delta)` resizes the fill and delta images. Property: `Capacity`, `RectTransform`.

## Modding Notes

PUNK has **no central "AI tick"** to patch — behaviour emerges from per-component `Update`/`OnEnable` methods that are all valid Harmony targets. Because enemies are prefabs assembled from these components, two modding strategies exist: (a) Harmony-patch the C# methods below, or (b) at runtime find/spawn enemy GameObjects and toggle/replace their state-machine components.

### Enemy health / damage scaling
- **Health is a `Unit` resource tank**, not an `Enemy` field. Scale it via `Enemy.Initialize` (the embedded `ModifyResourceCapacity` effect already gets a co-op multiplier there — a postfix can apply your own factor) or by patching `Unit`'s tank install/capacity methods (`InstallNewTank`, `IncreaseCapacity`).
- **Outgoing damage:** patch `ShootAction`/`ShootComplexAction` (fire gating) or the underlying `Shooter`/weapon, and `ChargerHead.OnObjectHit` / `Hazard.GetDamage` for contact damage. `DamageConditions.Validate` gates damage-reactive behaviour.
- **Kill accounting:** `Enemy.Data.OnDestroy` → `RunData.RegisterEnemyKilled`; `countsAsKill` controls whether a unit counts.

### Disable / neuter AI
- **Cleanest kill-switch:** Harmony-prefix `StateMachine.Update` (return false) to freeze all transitions, or `StateMachine.ChangeState` to pin a state. Disabling the `StateMachine` component triggers `returnToDefaultOnDisable`.
- **Blind enemies:** prefix `Vision.Update`/`ComponentScanner<Unit>.Scan` to skip scanning, or `AIAgent.Update`/`SelectTargetFromVisibleEnemies` so `currentTarget` stays null (most actions early-out on `!agent.HasTarget`).
- **Stop shooting:** prefix `ShootAction.Update` / `ShootComplexAction.Update`, or `Shooter.SetShooting`.
- **Pacify (no aggro):** prefix `AggroWhenHit.Aggro` or `AIAgent.HandleHitBy`. Force-flee everything by patching `Unit.IsAfraidOf` to return true.

### Spawn control
- **Counts & composition:** `EnemyGenerator.PlaceBasedOnEcosystem` is the single chokepoint — patch it to alter `remainingPowerLevel`, the ignore filter, or the draw. `EnemyGenerator.PlaceEnemies` (per-node loop) and `PoI.difficultyMultiplier` gate whole rooms.
- **Roster/weights:** edit `Ecosystem.enemies` (`EnemyDistribution`) and the nested `Enemy` struct (`powerLevel`, `minDistanceFromCenter`, `entity`) on the ScriptableObject assets, or postfix the generator to inject entities into `level.entityManager`.
- **Direct spawning:** `EntityGameObjectManager.CreateEntity(prefab, position)` (used by `EnemyList`/`MinionSpawnerWeapon`) is the runtime spawn entry point.
- **Tethering:** `MovementAction.restrictMovementToSpawnRoom` + `Unit.Data.SpawnRoomIndex` keep enemies near spawn; patch `RestrictTargetPositionToSpawnRoom` to free-roam them.

### Faster / slower enemies
- **Movement speed** lives in `UnitMovement`/`PushMovement`/`SwimmingMovement` (applied via `MovementAction`'s override snapshot). Patch those movement components, or `MovementAction.OnEnable` to inject a speed `ValueOverride`. `MoveTowardsTargetAction.pathFindingDelay` / `FleeFromTargetAcion.pathFindingDelay` change re-path cadence (responsiveness).
- **Turn/aim speed:** `AimAction.RotationSpeed` (and `Aimer.rotationSpeed`) — note it's already clamped to the weapon's `MaxRotationSpeedWhileShooting` while firing.
- **Fire rate / cadence:** `ShootComplexAction.fireRate`/`burstCount`/`burstDelay`, `ShootAction.delay`/`resourceAmountToWait`.
- **Reaction time:** `Vision.refreshDelay` and `AIAgent.Update` determine how quickly an enemy notices/retargets.
- **Global time-scaling:** since nearly everything keys off `Time.time`/`Time.deltaTime`, `Time.timeScale` (or a targeted per-component multiplier) is the broad lever.

### Useful Harmony anchors (summary)
| Goal | Method(s) |
|---|---|
| Freeze AI | `StateMachine.Update`, `StateMachine.ChangeState` |
| Blind enemies | `Vision.Update`, `ComponentScanner<Unit>.Scan`, `AIAgent.SelectTargetFromVisibleEnemies` |
| Pacify | `AggroWhenHit.Aggro`, `AIAgent.HandleHitBy`, `Unit.IsAfraidOf` |
| Spawn tuning | `EnemyGenerator.PlaceBasedOnEcosystem`, `EnemyGenerator.PlaceEnemies` |
| Loadout/health | `Enemy.Initialize`, `Unit.InstallNewTank` / `Unit.IncreaseCapacity` |
| Fire control | `ShootAction.Update`, `ShootComplexAction.Update`, `Shooter.SetShooting` |
| Contact damage | `ChargerHead.OnObjectHit` |
| Boss UI/state | `BossStateManager.EnterBossState`, `BossStateActivator.OnEnable` |
</content>
</invoke>
