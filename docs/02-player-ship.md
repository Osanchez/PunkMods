# Player / Ship
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

The player-controlled ship is a composite `GameObject`: a single root carries a `Ship` facade plus a cluster of specialized `MonoBehaviour`s wired together via `[SerializeField]` references. Core gameplay components are `Unit` (resources/tanks, faction, stats), `ModuleGridOwner` (the modular grid that defines weapons/abilities), `DamagableResource` (health), `Rigidbody2D` + `ShipMovement` (physics-driven thrust/dash/boost), and `ShipInput` (Unity Input System -> action maps). The ship is data-backed: its persistent state lives in `Unit.Data` / `ModuleGridOwner.Data` (savable `ComponentData` with `Memento` snapshots), so the visible prefab is spawned around an existing `EntityData`.

`ShipManager` (a Zenject-style `IInitializable` resolved through `ServiceLocator`) owns the lifecycle. At run start it reads `ShipsConfig` for the proper prefab (`AutoSwichShipPrefab` on desktop, `VirtualJoyShipPrefab` on Android, separate prefabs for explicit KBM/Gamepad), places ship `EntityData` at the level start node (one per player; two in co-op), applies the chosen `LoadoutTemplate` to the module grid, recalculates stats, fills every resource tank except fuel, then spawns the GameObjects and assigns a `ShipTheme` (colors) via `ApplyShipTheme`. `ShipManager` also brokers respawn-on-station-unlock, alive checks, and enable/disable of ship control.

Movement is force-based. `ShipMovement` reads a normalized `flyDirection` (set by `ShipInput`/`ShipControlActionMap` or by a `Joystick` on mobile), drains the fuel `Resource` proportional to thrust, and in `FixedUpdate` applies per-`Engine` directional forces with velocity-relative falloff and dynamic linear damping to enforce `maxSpeed`. Holding the dash button triggers a one-shot `Dash()` (impulse + constant force for `dashDuration`) and, after `boostStartDelay`, transitions into a sustained boost (`IsBoosted`) that raises speed/acceleration caps, enables `boostParticle` emission and the `impactDamage` ram collider, and burns the boosted fuel rate. Hovering trades fuel for anti-gravity lift and extra drag. Legs (`ShipLegs`, `Leg`, `LegCoordinator`, and the boss-style `Crawler`/`CrawlerLeg`) are cosmetic IK locomotion that raycast the ground and step procedurally; they do not drive physics (except `LegCoordinator`, which rotates a child root rigidbody to match terrain).

Damage/health flows through `DamagableResource` (a separate subsystem) whose `onDamage`/`onDeath` UnityEvents fan out to feedback components: `ShipCameraShaker` (ProCamera2D shakes), `ShipGamepadRumble` (rumble presets, gated by per-player settings), `ShipEngineSound`, `ShipGroundParticle`, and the `Ship`'s own time-scale hit-stop. Fuel exhaustion gates a hold-to-self-destruct mechanic on `Ship`. HUD (`ShipHud`, resource bars, `AbilitySlotsPanel`, `ShipLog*`) and the pause/station `ShipMenuToggler` round out the player-facing layer. The abstract `UnitMovement` base (`PushMovement`, `SwimmingMovement`) is the AI/enemy movement contract and is mostly unrelated to the player ship, but shares the `Unit`/`Rigidbody2D` foundation and is included here as the movement family.

## Class Index

| Class | Kind | Summary |
|-------|------|---------|
| Ship | MonoBehaviour | Player ship facade: fuel warnings, self-destruct, station proximity, death/resurrect, camera lock. |
| ShipManager | class (IInitializable) | Spawns, themes, respawns and bulk-controls all player ships. |
| ShipMovement | MonoBehaviour | Force-based thrust, dash, boost, hover; fuel consumption; engine model. |
| ShipMovement.Engine | class (Serializable) | One thruster: direction, max-thrust multiplier, vertical offset. |
| ShipInput | MonoBehaviour | Maps Unity Input System actions to movement/aim/fire/modules/hook/self-destruct. |
| ShipVirtualJoyInput | MonoBehaviour | Mobile on-screen twin-stick input driver. |
| ShipActionMap | abstract class | Base wrapper around an `InputActionMap` for a `ShipInput`. |
| ShipControlActionMap | class | "ShipControl" map: Move/Use/Pause/OpenConsumableWheel events. |
| ShipHud | MonoBehaviour | Builds resource bars, ability slots, minions, log display for a ship. |
| ShipLegs | MonoBehaviour | Toggles a leg open/close animator based on movement + ground raycast. |
| Leg | MonoBehaviour | Procedural IK leg: raycast foot placement, stepping via DOTween. |
| Leg.Side | enum | Left, Right. |
| LegCoordinator | MonoBehaviour | Coordinates multiple `Leg`s, rotates body root to terrain, schedules steps. |
| Crawler | MonoBehaviour | Spawns/manages many `CrawlerLeg`s for a push-driven crawling unit. |
| CrawlerLeg | MonoBehaviour | Bezier-mesh procedural leg with terrain attachment and stepping. |
| ShipTheme | ScriptableObject | Color theme (sprite + boost particle colors). |
| ApplyShipTheme | MonoBehaviour | Applies a `ShipTheme` to renderers/particles/crosshair. |
| ShipsConfig | ScriptableObject | Ship prefab variants, shared resources, themes. |
| ShipInstaller | MonoBehaviour | Empty placeholder (no members). |
| ShipCameraShaker | MonoBehaviour | Triggers ProCamera2D shakes on shoot/damage/death. |
| ShipEngineSound | MonoBehaviour | Engine loop/start/stop and hover audio driven by movement state. |
| ShipGamepadRumble | MonoBehaviour | Gamepad rumble on damage/death/dash/boost/shoot. |
| ShipGroundParticle | MonoBehaviour | Plays dust particle where engine exhaust meets ground. |
| ShipSelfDestructVisual | MonoBehaviour | Radial fill + animator for self-destruct charge. |
| ShipMenuToggler | MonoBehaviour | Pause/station tabbed menu (map/grid/consumables), showcase sequences. |
| ShipMenuTab | MonoBehaviour | Base tab in the ship menu (open/close/input hooks). |
| ShipMenuTabButton | MonoBehaviour | Tab header button label + active animator. |
| ShipLogOutput | class | Per-ship message log model with add/remove/expire. |
| ShipLogEntry | class | One log line (id, icon, message, duration, colors, flashing). |
| ShipLogProperties | struct (Serializable) | Serializable bundle of log-line parameters. |
| ShipLogDisplay | MonoBehaviour | Instantiates `LogEntry` widgets in response to `ShipLogOutput` events. |
| Unit | SavableComponent<Unit.Data> | Core combat actor: faction, power, resource tanks, shields, burn. |
| Unit.Data | class (ComponentData) | Persistent unit state (tanks, rechargers, shields, minions, burn). |
| Unit.ShieldData | struct | Resource + effectiveness pair for a shield. |
| Unit.OwnerConnectionType | enum | Undefined, PrimaryWeapon, SecondaryWeapon, Active1/2/3. |
| UnitMovement | abstract MonoBehaviour | Contract for AI movement strategies (MoveTo/Stop/overrides). |
| IUnitInitializer | interface | `Initialize(Unit.Data)` hook called on ship build. |
| Inertia | MonoBehaviour | Forces a Rigidbody2D's `inertia` to a fixed value. |
| CenterOfMass | MonoBehaviour | Forces a Rigidbody2D's `centerOfMass`. |
| LimitVelocity | MonoBehaviour | Clamps linear velocity magnitude each FixedUpdate. |
| LimitAngularVelocity | MonoBehaviour | Clamps angular velocity each FixedUpdate. |
| SwayMovement | MonoBehaviour | Adds Perlin-noise wandering force to a rigidbody. |
| PushMovement | UnitMovement | Impulse "push toward target" AI movement. |
| SwimmingMovement | UnitMovement | Steering/torque "swim toward target" AI movement. |
| MovementAction | abstract MonoBehaviour | AI state action that pathfinds and drives a `UnitMovement`. |

## Classes

### Ship
- Kind: MonoBehaviour
- Purpose: High-level player-ship facade tying together unit, module grid, health, fuel, weapons, interaction and feedback. Handles fuel-level warnings, hold-to-self-destruct, station proximity, death and resurrect.
- Key fields (all `[SerializeField]` private unless noted):
  - `unit : Unit` — core actor (exposed via `Unit`).
  - `moduleGridOwner : ModuleGridOwner` — modular loadout (exposed via `ModuleGridOwner`).
  - `savableEntity : SavableEntity` — persistence handle (exposed via `SavableEntity`).
  - `damagableResource : DamagableResource` — health/death.
  - `fuel : Resource` — fuel resource asset, queried via `unit.GetResource`/`GetTank`.
  - `primaryWeaponHolder` / `secondaryWeaponHolder : WeaponHolder` — exposed via `PrimaryWeapon`/`SecondaryWeapon`.
  - `interactor : Interactor` — drives `NearStation` detection.
  - `rigidbody2D : Rigidbody2D` (public `Rigidbody`), `crosshair : Crosshair` (public `Crosshair`), `animator : Animator`.
  - `selfDestructFillRate : float`, `selfDestructDecreaseRate : float` — self-destruct charge rates.
  - `damageTimeScaleModifier : TimeScaleModifierSetup` — hit-stop modifier.
  - public `damageSfx`, `selfDestructStartSfx`, `selfDestructLoopSfx : string` — sound ids.
  - public `shipInput : ShipInput` — input controller.
  - public `color : Color`; public `selfDestructVisual : ShipSelfDestructVisual`.
  - public `fuelCriticalThreshold : float`; public `fuelWarningAnimator : Animator`.
- Key properties: `NearStation : Station` (get; private set), `IsNearStation`, `Fuel`, `IsDead {get;set;}`, `IsPlayerTwo {get;set;}`, `LastTimeExitShipMenu {get;set;}`, `LogOutput : ShipLogOutput`.
- Key methods:
  - `Update()` — toggles crosshair visibility, updates fuel level, shows self-destruct hint when out of fuel, charges `selfDestructProgress` while `shipInput.SelfDestructPressed`, calls `damagableResource.Die()` at 1.0.
  - `OnDamage()` / `OnShieldDamage()` — play `damageSfx`, add `damageTimeScaleModifier` (hit-stop).
  - `CheckIfDead()` / `OnDeath()` — deactivate ship, co-op respawn message.
  - `Die()` — full self-damage.
  - `Resurrect()` — refill the health tank, zero others, reactivate, headlights on.
  - `LockCamera(float)` / `UnlockCamera(float)` — forward to child `CameraTargetBase[]`.
  - `SetHeadlightsEnabled(bool)` — animator bool "Headlights".
- Enums: private `FuelLevel { Normal, Low, Critical }`.
- Relationships: `Unit`, `ModuleGridOwner`, `DamagableResource`, `Resource`, `WeaponHolder`, `Interactor`, `Crosshair`, `ShipInput`, `ShipSelfDestructVisual`, `ShipLogOutput`, `CameraTargetBase`, `TimeManager`, `RunData`, `Station`, `AudioManager`.

### ShipManager
- Kind: class implementing `IInitializable` (resolved via `ServiceLocator`).
- Purpose: Owns all player ships — placement, spawning, theming, respawn, bulk control.
- Key fields: `shipsConfig : ShipsConfig`, `entityGameObjectManager`, `entityManager`, `mapIconManager`, `ships : List<Ship>`, `uiInputModule : InputSystemUIInputModule`.
- Key properties: `Ships : IReadOnlyList<Ship>`, `FirstAliveShip : Ship`.
- Key methods:
  - `Initialize()` — resolves dependencies via `ServiceLocator`.
  - `async PlaceShipEntities ToStartPosition(Level, bool isCoop, LoadoutTemplate)` — places 1 or 2 ship entities around `graph.StartNode.center`.
  - `PlaceShipEntity(Vector3, LoadoutTemplate)` — clones `AutoSwichShipPrefab.SavableEntity` data, applies loadout to `ModuleGridOwner.Data`, `RecalculateStats`, fills resources except fuel, adds to `EntityManager`.
  - `FillEveryResourceExceptFuel(Unit.Data)` — fills tanks except the one named "Fuel".
  - `SpawnShipGameObjects(Level, RunArguments)` — picks prefab (Android->VirtualJoy, co-op->two AutoSwitch with devices, else one), spawns, assigns themes.
  - `Spawn(EntityData, Ship prefab, InputDevice=null, bool isPlayerTwo=false)` — instantiates, adds shared tanks, `shipInput.AssignDevice(device)`.
  - `OnGameStarted()`, `CheckShipsAlive()`, `EnableShipControl()`, `DisableShipControl()`, `AnyShipsAlive()`.
  - `WasPerformedThisFrame(InputAction)` — polls all ships' player inputs.
  - `AssignTheme(Ship, ShipTheme)` — calls `ApplyShipTheme.Apply` and tints map icon.
  - `OnUpgradeInstalled(...)` — respawns first dead ship at the unlocked station.
- Relationships: `ShipsConfig`, `Ship`, `EntityManager`, `EntityGameObjectManager`, `MapIconManager`, `LoadoutTemplate`, `RunData`, `ShipTheme`, `ApplyShipTheme`, `Station`, `Unit.Data`, `ModuleGridOwner.Data`.

### ShipMovement
- Kind: MonoBehaviour, `[RequireComponent(typeof(Rigidbody2D))]`.
- Purpose: Physics force-based ship locomotion: thrust, fuel drain, dash, boost, hover.
- Public Action events: `DashStarted`, `DashEnded`, `BoostStarted`.
- Key public fields (all serialized/public, the prime mod-tuning surface):
  - `owner : Unit`, `fuelResource : Resource`, `damagableResource : DamagableResource`, `impactDamage : ImpactDamage`.
  - `fuelCostPerSecond : float`, `boostedFuelCostPerSecond : float`.
  - `simulateInput : bool`, `inputSimulationSpeed : float`.
  - `acceleration : float`, `accelerationWhileBoosted : float`.
  - `maxSpeed : float`, `maxSpeedWhileBoosted : float`.
  - `initialDashForce : float`, `dashCost : float`, `constantDashForce : float`, `dashDuration : float`, `dashCooldown : float`, `dashSfx : string`.
  - `boostStartDelay : float`, `boostParticle`/`boostStartParticle : ParticleSystem`, `boostImpactDamage : float`, `boostSfx`/`boostLoopedSfx : string`, `boostExtraSpeedLimit : float`, `boostExtraSpeedMultiplier : float`.
  - `hoverDragMultiplyer : float`, `hoverCost : float`.
  - `engines : Engine[]` — thruster definitions.
  - public `joystick : Joystick` — optional mobile stick.
  - `[HideInInspector]` public `flyDirection : Vector2`, `isHovering : bool`, `dashHeld : bool`.
- Key properties: `IsBoosted {get; private set}`, `FlyDirection`, `Rigidbody`, `MaxSpeed` (boost-aware), `Acceleration` (boost-aware).
- Key methods:
  - `Dash()` — if off cooldown and enough fuel: spend `dashCost`, impulse `initialDashForce` along `flyDirection`, fire `DashStarted`.
  - `Update()` — drains fuel by thrust, stops boost/hover when fuel empty, manages boost start/stop based on `dashHeld`.
  - `FixedUpdate()` — per-engine thrust via `AddForceAtPosition`, velocity-relative `CircEaseIn` falloff, dynamic `linearDamping` to cap speed, anti-gravity lift while hovering, constant dash force for `dashDuration`.
  - `StartBoosting()`/`StopBoosting()`/`SetBoosted(bool)` — toggles `IsBoosted`, boost particles emission, and `impactDamage.enabled` (ram damage only while boosted).
  - `OnDamaged()` — cancels boost and resets `boostStartTime`.
  - static `CircEaseIn(float)` — easing used in thrust falloff.
- Nested: `Engine` (Serializable) — `direction : Vector2`, `maxThurstMultiplyer : float`, `verticalOffset : float`.
- Relationships: `Unit`, `Resource`, `ResourceTank`, `DamagableResource`, `ImpactDamage`, `AudioManager`, `Joystick`, `Rigidbody2D`.

### ShipInput
- Kind: MonoBehaviour
- Purpose: Translates Unity Input System actions into ship behavior (move, dash/boost hold, hover, aim, fire, module activation, hook, self-destruct) and tracks control scheme/device.
- Key fields (serialized): `ship : ShipMovement`, `aimer : Aimer`, `primaryShooter`/`secondaryShooter : Shooter`, `moduleActivator1/2/3 : ModuleActivator`, `playerInput : PlayerInput`, `hook : Hook`; action-name strings: `dashActionName`, `hoverActionName`, `aimActionName`, `primaryFireActionName`, `secodaryFireActionName` (sic), `activateModule1/2/3ActionName`, `useActionName`, `grappleHookActionName`, `selfDestructActionName`; `gamepadAimDistance : float`.
- Key properties: `PlayerInput`, `SelfDestructPressed`, `ShipControlActionMap`, `ItemWheelActionMap`, `UIActionMap`, `InstrumentMenuActionMap`, `UsesGamepad` (scheme == "Gamepad"), `UsedGamepad : Gamepad`, `AimDirection : Vector2`.
- Events: `ControlSchemeChanged`, `DeviceLost`, `DeviceRegained` (all `Action<ShipInput>`).
- Key methods:
  - `HandleAction(InputAction.CallbackContext)` — central dispatch: calls `ship.Dash()`, sets `ship.dashHeld`/`ship.isHovering`, sets aim direction, `primaryShooter.SetShooting`, `moduleActivatorN.IsActivated`, `hook.Activate()`, `selfDestructPressed`.
  - `Update()` — aims at gamepad stick offset or `MouseWordPosition.Current`.
  - `AssignDevice(InputDevice)` — locks auto-switch and selects "Gamepad" or "KBM" control scheme.
- Relationships: `ShipMovement`, `Aimer`, `Shooter`, `ModuleActivator`, `Hook`, `PlayerInput`, the four action-map classes, `MouseWordPosition`.

### ShipVirtualJoyInput
- Kind: MonoBehaviour
- Purpose: Mobile twin-stick driver. Finds "LeftJoystick"/"RightJoystick" `Joystick`s, sets `ship.flyDirection`, aims with right stick, fires primary when right stick deflected > 0.5 sqr.
- Key fields (serialized): `ship : ShipMovement`, `aimer : Aimer`, `primaryShooter : Shooter`, `gamepadAimDistance : float`.
- Relationships: `ShipMovement`, `Aimer`, `Shooter`, `Joystick`.

### ShipActionMap (abstract)
- Purpose: Base wrapper over a named `InputActionMap` belonging to a `ShipInput`.
- Key members: protected `actionMap`, `shipInput`; `Enabled : bool`; `Enable()`/`Disable()` (call virtual `OnEnable`/`OnDisable`).
- Constructor: `ShipActionMap(ShipInput, string actionMapName)` — finds the map by name on `PlayerInput.actions`.

### ShipControlActionMap
- Kind: class : ShipActionMap (map name "ShipControl").
- Purpose: Active gameplay map; wires Move/Use/Pause/OpenConsumableWheel.
- Events: `UseActivated : Action<Ship>`, `PausePerformed : Action<ShipInput>`, `OpenItemWheel : Action<ShipInput>`.
- Key methods: `OnEnable`/`OnDisable` subscribe to "OpenConsumableWheel"/"Use"/"Move"/"Pause"; `Move` sets `shipMovement.flyDirection = ClampMagnitude(value,1)`; disabling zeroes `flyDirection`.

### ShipHud
- Kind: MonoBehaviour
- Purpose: Builds and maintains the in-game HUD for one ship: resource bars, ability slots, minions widget, log display; swaps button-prompt sprite sets by platform/gamepad.
- Key fields (public): `abilitySlotsPanel : AbilitySlotsPanel`, `resourceBarParent : Transform`, `resourceBarPrefab : GameObject`, `logDisplay : ShipLogDisplay`, `minionsWidget : MinionsWidget`, `pcSpriteSet`/`xboxSpriteSet`/`psSpriteSet : PlatformSpriteSet`.
- Key methods: `AssignShip(Ship)` — subscribes to tank install/remove, builds bars, assigns ability slots/log/minions; `GetProperSpriteSet()` (PC vs DualShock vs Xbox); `OnTankInstalled`/`OnTankRemoved`/`ReorderResourceBars`/`RefreshResourcePanelSize`.
- Relationships: `Ship`, `Unit`, `ResourceTank`, `ResourceBar`, `Resource`, `AbilitySlotsPanel`, `ModuleGrid`, `PlatformSpriteSet`.

### ShipLegs
- Kind: MonoBehaviour
- Purpose: Cosmetic landing-leg controller — opens legs (animator bool "IsOpened") when nearly stationary and ground is within `maxDistance` below; closes when moving (`FlyDirection.sqrMagnitude > 0.5`).
- Key fields (serialized): `layerMask : LayerMask`, `maxDistance : float`, `animator : Animator`, `shipMovement : ShipMovement`.

### Leg
- Kind: MonoBehaviour
- Purpose: Single procedural IK leg. Raycasts an arc to find foot placement, steps via DOTween along horizontal/vertical curves, validates 3-segment joint angles.
- Key fields (serialized): `optimalLegPosition`, `ikTarget`, `raycastCenter`, `segment1/2/3 : Transform`; `layerMask : LayerMask`; `raycastDistance`, `raycastVariance`, `raycastStartAngle`, `raycastArcAngle : float`; `side : Side`; `limbSolver : CyclicQuadrilateralSolver`; `segment1/2/3AngleRange : MinMaxFloat`; `forwardStepDistance`, `stepDuration`, `minStepDelay`, `minStepDistance : float`; `horizontalMoveCurve`/`verticalMoveCurve : AnimationCurve`; `debugState : bool`.
- Key properties: `TargetPosition`, `MovementDirection {get;set}`, `IsPlacedOnGround`, `IsMoving`, `LegSide`, `MovingEnabled {get;set}`, `ReachesTarget`.
- Key methods: `ShouldStep() : bool`, `Step()` (raycast-based foot placement + DOTween move), `GetNewTargetPosition(...)`.
- Enums: `Side { Left, Right }`.
- Relationships: `CyclicQuadrilateralSolver`, DOTween, `MinMaxFloat` (MyBox).

### LegCoordinator
- Kind: MonoBehaviour
- Purpose: Orchestrates an array of `Leg`s — feeds them velocity as movement direction, rotates a `rootRigidbody` to align body with average grounded foot positions, schedules one leg to step at a time per side.
- Key fields (serialized): `rigidbody : Rigidbody2D`, `movement : UnitMovement`, `layerMask : LayerMask`, `legs : Leg[]`, `rootRigidbody : Rigidbody2D`, `rotationSpeed : float`.
- Key methods: `Rotate()`, `GetAvaragePosition(Leg.Side, out Vector2)`, `StepIfNeeded()`, `GetTerrainDirection(Vector2)`.

### Crawler
- Kind: MonoBehaviour
- Purpose: Boss/enemy-style many-legged crawler. Instantiates `legCount` `CrawlerLeg`s, raycasts in directions (toward `PushMovement.MovementDirection` or randomly when stopped) to place feet, forces a step after travelling `forceStepTravelDistance`.
- Key fields (serialized): `rigidbody2D`, `legPrefab : CrawlerLeg`, `legCount : int`, `legLength`, `legMoveDuration`, `forceStepTravelDistance : float`, `movement : PushMovement`, `stepDurationMultiplierAtFullSpeed`, `maxRigidbodySpeed : float`.
- Relationships: `CrawlerLeg`, `PushMovement`, ground `LayerMask`.

### CrawlerLeg
- Kind: MonoBehaviour
- Purpose: Procedural bezier-mesh leg with terrain attachment (tracks a `LevelSegmentComponent` cell so it detaches when terrain is destroyed) and DOTween stepping.
- Key fields (serialized): mesh refs (`meshFilter`, `meshRenderer`, `tipTransform`), shape params (`startControlPointLength`, `endControlPointLength`, `segmentTextureCount`, `overlap`, `lightDirection`, `midControlPointDistance`, `segmentLength`, `segmentWidth`, `legStartDistance`), curves (`legMovementCurve`, `stepCurve`, `tooCloseLegCurve`), `minDistanceWhileMovingLeg`, `startAngleMultiplier`, `endAngleMultiplier`, `impactParticle`, `visualLengthMultiplier`.
- Key properties: `IsGrounded`, `IsMoving`, `IsActive`, `MaxLength`, `IsLeft` (all {get;set}), `IsEndTooFar`, `CurrentLength`.
- Key methods: `StepTo(RaycastHit2D, float duration)`, `MoveLeg(Vector2,Vector2,float)`.
- Relationships: PathCreation (`BezierPath`/`VertexPath`), `SegmentedPipeGenerator`, `LevelChangeBuffer`, `LevelSegmentComponent`.

### ShipTheme
- Kind: ScriptableObject, `[CreateAssetMenu menuName="Punk/Ship theme"]`.
- Key fields (public): `spriteColor`, `boostParticleColor1`, `boostParticleColor2 : Color`.

### ApplyShipTheme
- Kind: MonoBehaviour
- Purpose: Applies a `ShipTheme` — tints all `spriteRenderers`, sets boost particle start colors, tints crosshair.
- Key fields (serialized): `spriteRenderers : SpriteRenderer[]`, `boostTrailParticle`/`boosParticleSub`/`boostStartParticle : ParticleSystem`, `crosshair : SpriteRenderer`.
- Key method: `Apply(ShipTheme)`.

### ShipsConfig
- Kind: ScriptableObject, `[CreateAssetMenu menuName="Punk/Ships config"]`.
- Purpose: Central registry of ship prefab variants and presentation.
- Key fields (public): `KBMShipPrefab`, `GamepadShipPrefab`, `AutoSwichShipPrefab`, `VirtualJoyShipPrefab : Ship`; `sharedResources : Resource[]`; `shipThemes : ShipTheme[]`.

### ShipInstaller
- Kind: MonoBehaviour. Empty placeholder — no fields or methods in the decompiled output.

### ShipCameraShaker
- Kind: MonoBehaviour
- Purpose: Drives ProCamera2D screen shake. On `Awake` subscribes to primary/secondary `Shooter.OnShoot` and `damagableResource.onDamage`/`onDeath`.
- Key fields (serialized): `damagableResource`, `primaryShooter`/`secondaryShooter : Shooter`, `damageShakePreset`/`deathShakePreset : ShakePreset`.
- Note: weapon shots use `Shooter.Weapon.ShakePreset`; uses `ProCamera2DShake.Instance.Shake(...)`.

### ShipEngineSound
- Kind: MonoBehaviour
- Purpose: Plays looped/start/stop engine and hover audio based on `ShipMovement.flyDirection`/`IsBoosted`/`isHovering`.
- Key fields (serialized): `shipMovement`, four `AudioSource`s (`engineLoopedAudioSource`, `engineStartAudioSource`, `engineStopAudioSource`, `hoverAudioSource`), `minStartSoundDelay : float`.

### ShipGamepadRumble
- Kind: MonoBehaviour
- Purpose: Sets gamepad motor speeds on damage/death/dash/boost/weapon-fire; scales by per-player rumble setting; auto-stops after preset duration.
- Key fields (serialized): `ship`, `shipInput`, `damagableResource`, `shipMovement`, `primaryShooter`/`secondaryShooter`, and `RumblePreset` fields `damageRumble`, `deathRumble`, `dashRumble`, `boostRumble`.
- Key method: `Rumble(RumblePreset)` — gated by `shipInput.UsesGamepad` and `settingsManager.GameplayOptions.p1/p2GamepadRumble`.
- Relationships: `SettingsManager`, `RumblePreset`, `Gamepad` (Input System), `Shooter.Weapon.RumblePreset`.

### ShipGroundParticle
- Kind: MonoBehaviour
- Purpose: Positions and plays a ground dust particle where the downward raycast hits ground while `engineParticle.isEmitting`.
- Key fields (serialized): `engineParticle`, `dustParticle : ParticleSystem`, `layerMask : LayerMask`, `maxDistance`, `offset : float`.

### ShipSelfDestructVisual
- Kind: MonoBehaviour
- Purpose: Self-destruct UI: animator bool "Visible" + radial `Image.fillAmount`.
- Key fields (serialized): `animator : Animator`, `image : Image`. Method: `SetProgress(bool visible, float progress)`.

### ShipMenuToggler
- Kind: MonoBehaviour
- Purpose: The pause/station tabbed menu (Map / Module Grid / Consumables). Opens on action or at a station, pauses time, switches all player inputs to "MapControl", disables ship control; supports cycling tabs and special "showcase" sequences (instrument-discovered location reveal, scanner area reveal).
- Key fields (serialized, selection): `canvas`, `header`, `minimapCanvas : Canvas`; `tabs : ShipMenuTab[]`; `tabButtons : ShipMenuTabButton[]`; `InputActionReference`s `openAction`, `previousTabAction`, `nextTabAction`, `closeAction`, `backAction`; `gameController : GameController`; `timeManager : TimeManager`; sprite-set/UI fields. Tab index constants: `tabIndexMap=0`, `tabIndexGrid=1`, `tabIndexConsumables=2`.
- Key methods: `Open(PlayerInput, int tabIndex, Station)`, `Close()`, `ShowTab(int)`, `OpenShop(Ship, Station)`, `RevealScannedArea(...)`, `SetEntityDiscoveredByInstrument(...)`, `OnActionTriggered(...)`.
- Relationships: `ShipMenuTab`, `ShipMenuTabButton`, `ShipManager`, `MapMover`, `InGameHud`, `TimeManager`, `Station`, `Scanner.Data`.

### ShipMenuTab
- Kind: MonoBehaviour (base for menu tabs).
- Members: protected `Ship`, `Station`; `Open(Ship,Station)`/`Close()`; virtual `OnOpened`, `OnClosed`, `OnInputActionPerformed(InputAction)`, `OnBackPressed() : bool`.

### ShipMenuTabButton
- Kind: MonoBehaviour
- Fields (serialized): `animator`, `label : TMP_Text`, `normalLabelText`/`stationLabelText : string`. Methods: `UpdateText(bool isNearStation)`, `SetActive(bool)` (animator bool "IsActive").

### ShipLogOutput
- Kind: plain class
- Purpose: Per-ship message log model. Holds `List<ShipLogEntry>`; events `LogAdded`/`LogRemoved : Action<ShipLogEntry>`.
- Key methods: `Update()` (expires by duration), `Log(int id, ShipLogProperties)`, `Log(int id, string message, Sprite=null, float duration=5, ColorAsset iconColor=null, ColorAsset textColor=null, bool flashing=false)`, `Clear(int id)`.

### ShipLogEntry
- Kind: plain class
- Purpose: One log line. Const ids: `LOG_ID_BOSS_KILLED=1`, `LOG_ID_FUEL_LOW=2`, `LOG_ID_RESURRECT=3`, `LOG_ID_SELF_DESTRUCT_HINT=4`, `LOG_ID_INSTRUMENT_ICON_ADDED=5`.
- Fields: `id`, `icon : Sprite`, `message : string`, `duration : float`, `iconColor`/`textColor : ColorAsset`, `flashing : bool`, `creationTime : float`; prop `TimeSinceCreation`.

### ShipLogProperties
- Kind: struct (Serializable). Fields: `message : string`, `icon : Sprite`, `duration : float`, `textColor`/`iconColor : ColorAsset`, `isFlashing : bool`.

### ShipLogDisplay
- Kind: MonoBehaviour
- Purpose: View for `ShipLogOutput`. On `Assign(Ship)` subscribes to `LogAdded`/`LogRemoved`, instantiates/destroys `LogEntry` widgets.
- Fields (public): `logEntryPrefab : LogEntry`, `entriesParent : Transform`, `newMessageSfx : string`.

### Unit
- Kind: `SavableComponent<Unit.Data>`, implements `IPropertyListOwner`.
- Purpose: Core combat actor shared by ship and enemies — owns resource tanks, rechargers, shields, faction/power relations, and the burn (fire) model. Acts as the runtime facade over the persistent `Unit.Data`.
- Key fields (serialized): `faction : Faction`, `powerLevel : int = 10`, `maxPowerLevelToFight : int = 100`, `[Layer] projectileLayer : int`, `resourceInsufficientSfx : string`, `_rigidBody : Rigidbody2D`, `burnProperties : Data.BurnProperties`, `blocksStationUnlock : bool`.
- Key properties: `HasInfiniteResource {get;set}` (forwards to Data), `BlocksStationUnlock`, `RigidBody`, `Faction`, `Owner : EntityData`.
- Events: `MaximumMinionReached : Action<OwnerConnectionType>`, `KilledAnotherUnit : Action<Unit,Unit>`.
- Key methods: `GetResource(Resource)`, `HasTank`, `GetTank`, `GetAllTanks`, `GetNotSharedTanks`, `GetSharedTanks`, `IncreaseCapacity`, `GetRechargeRate`, `IncreaseRechargeRate`, `InstallNewTank`, `AddSharedTank`, `SetOwner(SavableEntity, OwnerConnectionType)`, `IsFriendsWith`/`IsEnemiesWith`/`IsAfraidOf(Unit)`, `RegisterKill(Unit)`, `TriggerResourceInsufficient`, `TriggerMaximumMinionReached`, `CreateData()` (calls every child `IUnitInitializer.Initialize`), `GetPropertyList(...)`.
- Nested `Unit.Data` (ComponentData, IMementoOriginator): holds `resourceTanks`, `resourceRechargers`, `shields`, `minions`, `burnProperties`, `BurnLevel`/`IsOnFire`, `HasInfiniteResource`, `Owner`, `IsInvisible`, `SpawnRoomIndex`, `ConnectionToOwner`; events `StatsRecalculated`, `ResourceTankInstalled`, `ResourceTankRemoved`; methods incl. `RecalculateStats(IModuleGrid)`, `RefillResources`, `InstallNewTank`, `AddShield`, `CreateMemento`/`RestoreFromMemento`. Nested `BurnProperties` struct: `fireThreshold`, `extraBurnLevelWhenCatchingFire`, `coolingSpeed`, `fireTickRate`, `fireDmgPerTick`, `maxBurnLevel : float`.
- Enums: `OwnerConnectionType { Undefined, PrimaryWeapon, SecondaryWeapon, Active1, Active2, Active3 }`.
- Struct: `ShieldData { Resource resource; float effectiveness; }`.
- Relationships: `Resource`, `ResourceTank`, `ResourceRecharger`, `Faction`, `IModuleGrid`, `EntityData`, `EntityManager`, `SavableEntity`.

### UnitMovement (abstract)
- Kind: abstract MonoBehaviour. Movement strategy contract (used by AI; player ship uses `ShipMovement` directly, not this).
- Abstract members: `Vector2 MovementDirection {get}`, `MoveTo(Vector2)`, `ApplyOverride(object)`, `ApplyNamedOverride(string)`, `object GetCurrentValues()`, `Stop()`.

### IUnitInitializer
- Kind: interface. Single method `void Initialize(Unit.Data unit)`. Called by `Unit.CreateData()` on every child component implementing it, so each subsystem can seed its persistent state (tanks, etc.) onto the new `Unit.Data`. (No implementors are present in this Player/Ship slice — they live in the module/resource code.)

### Inertia
- Kind: MonoBehaviour. Sets `rigidbody.inertia = inertia` in `Awake`. Fields (serialized): `rigidbody : Rigidbody2D`, `inertia : float`.

### CenterOfMass
- Kind: MonoBehaviour, `[RequireComponent(Rigidbody2D)]`. Sets `rigidbody.centerOfMass = centerOfMass` in `Awake`. Fields (serialized): `rigidbody : Rigidbody2D`, `centerOfMass : Vector2`.

### LimitVelocity
- Kind: MonoBehaviour. `FixedUpdate` clamps `rigidbody.linearVelocity` to `maxVelocity`. Fields (serialized): `rigidbody : Rigidbody2D`, `maxVelocity : float`.

### LimitAngularVelocity
- Kind: MonoBehaviour. `FixedUpdate` clamps `rigidbody.angularVelocity` to ±`maxAngularVelocity`. Fields (serialized): `rigidbody : Rigidbody2D`, `maxAngularVelocity : float`.

### SwayMovement
- Kind: MonoBehaviour. Adds Perlin-noise force to `target` each `FixedUpdate` for idle drift. Fields (public): `target : Rigidbody2D`, `frequency : float`, `amplitude : float`.

### PushMovement
- Kind: UnitMovement. AI "push toward target" movement: at intervals applies an impulse (with random angle spread) toward `targetPosition`.
- Key fields (serialized): `rigidbody : Rigidbody2D`, `pushForce : MinMaxFloat`, `pushDelay : MinMaxFloat`, `maxAngle : float`, `forcePointLocalPosition : Vector2`, `namedOverides : NamedValueOverride[]`.
- Properties: `IsStopped`, `MovementDirection`. Event `Pushed`. Methods: `MoveTo`, `Stop`, `Push()`, `ApplyOverride`, `ApplyNamedOverride`, `GetCurrentValues`, `GetProperties`.
- Nested public struct `ValueOverride` (override flags + `pushForce`, `pushDelay`, `maxAngle`, `forcePointLocalPosition`) — used by `MovementAction`.

### SwimmingMovement
- Kind: UnitMovement. AI "swim toward target" movement: torque-based steering + forward thrust along `transform.right`, with sideways drag.
- Key fields (serialized): `rb : Rigidbody2D`, `torque`, `overSteeringAngle`, `maxAngularVelocity`, `movementForce`, `maxSpeed : float`, `maxSpeedCurve : AnimationCurve`, `sidewaysDrag : float = 0.9`, `namedOverides : NamedValueOverride[]`.
- Methods: `MoveTo`, `Stop`, `ApplyNamedOverride`, `ApplyOverride`, `GetCurrentValues`, `GetProperties`. Nested public struct `ValueOverride` mirrors all tunables.

### MovementAction (abstract)
- Kind: abstract MonoBehaviour, `IValidatable`. An AI state-machine action that pathfinds (A* Pathfinding `Path`) and drives a `UnitMovement` toward waypoints, applying value overrides on enter/exit.
- Key fields (public): `agent : AIAgent`, `movement : UnitMovement`, `arrivalDistance : float`, `transitionOnArrival : State`, `stopOnExit : bool`, `useNamedOverride : bool`, `restrictMovementToSpawnRoom : bool`, `roomRadiusMultiplier : float`, `namedOverride : string`, `pushMovementOverride : PushMovement.ValueOverride`, `swimmingMovementOverride : SwimmingMovement.ValueOverride`, `changeStateAfterFirstPush : bool`, `transitionAfterFirstPush : State`.
- Abstract `FindPath()`; `OnPathFound(Path)`, `RestrictTargetPositionToSpawnRoom(Vector2)`, `Validate()`.
- Relationships: `AIAgent`, `UnitMovement`, `State`/`StateMachine`, `Level` graph, Pathfinding.

## Modding Notes

### Tuning ship feel (no patch needed — edit serialized values)
`ShipMovement` exposes all handling as public fields, so most mods are field tweaks on the live component (find it on the player ship instance, e.g. via `ShipManager.Ships[i].GetComponentInChildren<ShipMovement>()`):
- Top speed: `maxSpeed`, `maxSpeedWhileBoosted`.
- Acceleration / handling: `acceleration`, `accelerationWhileBoosted`, per-`Engine.maxThurstMultiplyer`.
- Dash: `initialDashForce`, `constantDashForce`, `dashDuration`, `dashCooldown`, `dashCost`.
- Boost: `boostExtraSpeedLimit`, `boostExtraSpeedMultiplier`, `boostStartDelay`, `boostedFuelCostPerSecond`.
- Fuel economy: `fuelCostPerSecond`, `boostedFuelCostPerSecond`, `hoverCost`, and `dashCost`.

### Infinite / free fuel
Two clean approaches:
- Set `Unit.HasInfiniteResource = true` (or `Unit.Data.HasInfiniteResource`) which marks every `ResourceTank.isInfinite`. This is the game's own "infinite resource" switch (used by DebugMenu) and affects all resources.
- Harmony-patch `ShipMovement.Update` (prefix that early-returns or skips the fuel-drain block) or `ShipMovement.Dash` (skip the `dashCost` check). Note fuel drain happens in `ShipMovement.Update`, not `FixedUpdate`.

### Common Harmony patch targets
- `ShipMovement.Dash()` — gate/force dashes, remove cooldown/cost.
- `ShipMovement.FixedUpdate()` — override thrust/damping math (whole-method replacement is easiest; it is one method, no Burst).
- `ShipMovement.StartBoosting()` / `SetBoosted(bool)` — force permanent boost (e.g. prefix `StopBoosting` to no-op for infinite boost), or always enable the ram `impactDamage`.
- `ShipInput.HandleAction(InputAction.CallbackContext)` — intercept/remap any action; this is the single dispatch point for dash/hover/fire/modules/hook/self-destruct.
- `Ship.Update()` — alter self-destruct charging or fuel-warning behavior; `Ship.Die()` / `Ship.OnDeath()` / `Ship.Resurrect()` for god-mode/respawn mods (note actual damage routes through `DamagableResource`, documented in the Health category).
- `Ship.OnDamage()` / `Ship.OnShieldDamage()` — hit-stop hooks.
- `ShipManager.SpawnShipGameObjects` / `ShipManager.Spawn` — swap prefab, inject extra components, or force a control scheme at spawn.
- `Unit.RecalculateStats` (on `Unit.Data`) / `Unit.CreateData` — alter starting tanks/stats; `Unit.CreateData` is where every `IUnitInitializer.Initialize` runs.
- `ShipMenuToggler.Open` / `Close` / `ShowTab` — customize the pause/station menu.

### DI / wiring caveats
- `ShipManager`, `ShipsConfig`, `RunData`, `TimeManager`, `EntityManager`, `AudioManager`, `SettingsManager`, etc. are resolved via a static `ServiceLocator.Get<T>()` — you can fetch the same singletons from your mod, but they are only valid after the relevant scene/installer has run. `ShipManager` is an `IInitializable`; do your hooks after `GameController.GameStarted`.
- Ships are spawned at runtime around `EntityData`, not placed in-scene. Use `ShipManager.Ships` / `FirstAliveShip` to find live instances rather than `FindObjectOfType` at load.
- Prefab selection is platform/mode dependent (`AutoSwichShipPrefab` desktop, `VirtualJoyShipPrefab` Android, `KBM/GamepadShipPrefab` exist for explicit schemes). Patch `ShipsConfig` fields or `SpawnShipGameObjects` to override.

### Structs and value-type overrides (patching friction)
- `PushMovement.ValueOverride` and `SwimmingMovement.ValueOverride` are `struct`s passed as `object` through `UnitMovement.ApplyOverride(object)`. These are AI-movement tunables, not the player ship. `MovementAction` boxes/unboxes them; if you patch `ApplyOverride`, respect the boxed-struct contract or you'll get the `ArgumentException` it throws on type mismatch.
- `ShipLogProperties` and `Unit.Data.BurnProperties` are serialized structs; mutate via the owning component's serialized field, not by reassigning a returned copy.

### Burst / Jobs
None of the Player/Ship types use Burst or the Jobs system — all movement is plain `MonoBehaviour` `FixedUpdate`/`Update` physics, so methods are directly Harmony-patchable. (Burst jobs in this game are confined to terrain/biome/lightmap generation, outside this category.)

### External types
`Joystick` (mobile) is referenced by `ShipMovement`/`ShipInput`/`ShipVirtualJoyInput` but is defined in an external asset assembly, not Punk.Main.dll. `CyclicQuadrilateralSolver` (IK), DOTween, A* Pathfinding (`Path`), and ProCamera2D (`ProCamera2DShake`, `ShakePreset`) are likewise third-party.
