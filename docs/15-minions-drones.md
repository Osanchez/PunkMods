# Minions ("Drones"), Vision & Targeting
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## What a "drone" is

There is **no `Drone` class** in PUNK. What players call a drone is a **`Minion`** — an ordinary
`Unit` that the player spawns and *owns*. Minions are produced two ways:

- **`SpawnMinionModule`** (an `ActiveModule`) — spawns up to `Level` minions, copying the cluster's
  other powered modules into the minion's `SimpleModuleGrid` (see `04-modules-grid.md`).
- **`MinionSpawnerWeapon`** — a `WeaponBase` that fires by *instantiating minion `Unit`s* instead of
  projectiles (see `05-enemies-ai.md`).

The single most important fact for this doc: **a minion runs the exact same AI brain as an enemy.**
Its targeting is nothing minion-specific — it is the shared `AIAgent` + `Vision` perception pipeline
that every AI `Unit` in the game uses. So "why is my drone slow to notice enemies?" is really "how
does `Vision` feed `AIAgent`?", answered below.

## Ownership vs. faction — they are independent

When a minion is created, `MinionOwnerSetter` hooks the spawner's `OnPrepareMinion` event and calls
`minion.SetOwner(owner, connectionType)`:

```csharp
// MinionOwnerSetter.OnPrepareMinion
private void OnPrepareMinion(MinionSpawnerWeapon weapon, Unit minion)
{
    minion.SetOwner(owner, connectionType);
}
```

But `SetOwner` **only** records the owner and the connection slot — it does **not** change the
minion's faction:

```csharp
// Unit.Data.SetOwner
public void SetOwner(EntityData owner, OwnerConnectionType connectionType)
{
    Owner = owner;
    ConnectionToOwner = connectionType;
    ...
}
```

A minion's friend/foe relationships come entirely from the **`Faction` asset serialized on its
prefab** (`[SerializeField] private Faction faction;` on `Unit`). Ownership governs only:

- **Orbit** — `MoveAroundOwnerAction` paths the minion to a point circling `unit.Owner.position`.
- **Leash conditions** — `OwnerIsWithinRangeCondition` (dist to owner < `maxDistance`),
  `HasOwnerCondition` (owner != null).
- **HUD** — `MinionsWidget`/`MinionCountWidget` count minions per `OwnerConnectionType` slot.
- **Cleanup** — `Unit.Data.OnDestroy` unregisters the minion from its owner; `SpawnMinionModule`
  despawns excess/uninstalled minions.

**`OwnerConnectionType`**: `{ Undefined, PrimaryWeapon, SecondaryWeapon, Active1, Active2, Active3 }`.

## The perception → targeting pipeline

Three components on the minion, all shared with enemies:

```
Unit (+ faction, powerLevel, maxPowerLevelToFight)
 └ Vision : ComponentScanner<Unit>   ← periodic proximity + line-of-sight scan
 └ AIAgent                           ← sorts visible units into friends/enemies, picks a target
 └ StateMachine (orbit owner → attack/flee)
```

### 1. Vision — the scan cadence (this is the aggro-latency source)

`Vision` inherits `ComponentScanner<Unit>` and re-scans **only every `refreshDelay` seconds**:

```csharp
// Vision
private void Start()
{
    // NOTE: quirky — the offset is scaled by refreshDelay TWICE (see "Quirks" below)
    lastRefreshTime = Time.time - refreshDelay * Random.Range(0f, refreshDelay);
}

private void Update()
{
    if (!(Time.time - lastRefreshTime < refreshDelay))
    {
        Scan();
        lastRefreshTime = Time.time;
    }
}

protected override bool IsVisible(Unit component)
{
    return !component.ComponentData.IsInvisible;   // invisible units are never seen
}
```

`ComponentScanner<Unit>.Scan()` is the actual sensor:

```csharp
public IReadOnlyCollection<T> Scan()
{
    visibleComponents.Clear();
    int num = Physics2D.OverlapCircleNonAlloc(transform.position, range, overlappingColliders, targetLayers);
    for (int i = 0; i < num; i++)
    {
        if (overlappingColliders[i].TryGetComponent<T>(out var component))
        {
            int count = Physics2D.LinecastNonAlloc(transform.position, component.transform.position, results, blockingLayers.value);
            if (!AnyColliderBlocks(results, count) && IsVisible(component))
                visibleComponents.Add(component);
        }
    }
    return visibleComponents;
}
```

So each scan: an **`OverlapCircle` of radius `range`** on `targetLayers`, then a **line-of-sight
linecast** against `blockingLayers` per candidate. An enemy is only "seen" if it is (a) within
`range`, (b) not occluded by a `blockingLayers` collider, and (c) not invisible.

> **LoS gotcha** — `AnyColliderBlocks` returns `true` (blocked) if any linecast hit exists *and*
> `ignoredColliders` is empty. Occlusion is strict: an enemy behind terrain on a blocking layer is
> invisible even at point-blank range until it clears cover.

### 2. AIAgent — target selection runs every frame

`AIAgent.Update` runs **every frame** (not gated by `refreshDelay`). It re-sorts whatever `Vision`
last saw and picks a target immediately:

```csharp
private void Update()
{
    RefreshEnemyAndFriendLists();
    if (HasTarget && IsTargetVisible)
        targetLastKnownPosition = Target.transform.position;
    else
        SelectTargetFromVisibleEnemies();
}
```

`RefreshEnemyAndFriendLists` buckets each visible unit — **per-instance overrides first, then
faction**:

```csharp
foreach (Unit visibleUnit in vision.VisibleUnits)
{
    if (enemyBlackList.Contains(id))      visibleEnemies.Add(visibleUnit);   // attacked-me override
    else if (friendWhiteList.Contains(id)) visibleFriends.Add(visibleUnit);
    else if (thisUnit.IsFriendsWith(visibleUnit)) visibleFriends.Add(visibleUnit);
    else if (thisUnit.IsEnemiesWith(visibleUnit))  visibleEnemies.Add(visibleUnit);
    // else: neither — the unit is IGNORED (never targeted)
}
```

Faction membership is literal list containment:

```csharp
public bool IsFriendsWith(Unit unit) => faction.Allies.Contains(unit.faction);
public bool IsEnemiesWith(Unit unit) => faction.Enemies.Contains(unit.faction);
```

Then target selection:

```csharp
private void SelectTargetFromVisibleEnemies()
{
    if (!SeesEnemy) return;
    foreach (Unit e in visibleEnemies)
        if (IsAfraidOf(e)) { SetTarget(e); return; }   // scary enemy prioritized...
    SetTarget(visibleEnemies.First());                 // ...else an ARBITRARY HashSet element
}
```

Two quirks that shape "feel":

- **Not nearest.** `visibleEnemies.First()` is an arbitrary `HashSet<Unit>` element — insertion/hash
  order, **not distance**. A drone can lock onto a far enemy while a closer one is ignored.
- **Fear is targeted first.** A unit it `IsAfraidOf` is selected *before* the fallback. `IsAfraidOf`
  is `unit.powerLevel > maxPowerLevelToFight` (defaults `powerLevel = 10`, `maxPowerLevelToFight =
  100`, so *false* for normal enemies). If a minion *is* afraid of its target, `WaitForTargetAction`
  routes it to a **flee** state instead of attack — which looks like "refuses to engage."

### 3. StateMachine — orbit until an enemy is visible

A minion's behaviour tree (a GameObject `StateMachine`, see `05-enemies-ai.md`) typically orbits the
owner via `MoveAroundOwnerAction` and transitions to chase/attack when `EnemyVisibleCondition` /
`TargetVisibleCondition` fire. `OwnerIsWithinRangeCondition` can pull it back toward the player, so a
drone may briefly disengage to re-tether. The concrete states/conditions are **serialized on the
prefab**, not in the DLL.

## Vision "defaults" — where the numbers actually live

This is the crux of the "slow to aggro" question, and the honest answer:

| Value | Where it lives | Verifiable from DLL? |
|---|---|---|
| `Vision.refreshDelay` | **serialized on the minion prefab** | ❌ no — C# field has no initializer |
| `Vision.range` (`ComponentScanner.range`) | **serialized on the minion prefab** | ❌ no |
| `targetLayers` / `blockingLayers` / `ignoredColliders` | **serialized on the minion prefab** | ❌ no |
| `maxVisibleUnits` (scan buffer cap) | code constant = **1000** | ✅ yes |
| `Unit.powerLevel` | code default **10** (prefab may override) | ✅ default only |
| `Unit.maxPowerLevelToFight` | code default **100** (prefab may override) | ✅ default only |

`refreshDelay` and `range` are `[SerializeField]` floats with **no C# initializer**, so their
compiled default is `0`; the real values are whatever the minion prefab author set in the Unity
inspector. **They cannot be read from `Punk.Main.dll`** — they live in the serialized prefab/asset
bundles.

**To read the live values**, patch at runtime and log them. Minimal BepInEx/Harmony postfix on
`Vision.Start` (or a one-shot dump on the spawned minion):

```csharp
[HarmonyPatch(typeof(Vision), "Start")]
static class DumpVision {
    static void Postfix(Vision __instance) {
        var range = Traverse.Create(__instance).Field("range").GetValue<float>();       // from ComponentScanner
        var delay = Traverse.Create(__instance).Field("refreshDelay").GetValue<float>();
        Log.LogInfo($"[Vision] {__instance.name} range={range} refreshDelay={delay}");
    }
}
```

(Or enable `StateMachine.logTransitions` on a spawned minion to watch exactly when/why it flips into
its attack state.)

## Why drones feel slow to aggro — ranked

None of these is a friendly-fire safeguard (see next section). In rough order of likely impact:

1. **`Vision.range` too small** — the enemy must get close before the drone can *see* it at all. #1
   suspect for "won't attack an enemy right next to me until it's very close."
2. **`Vision.refreshDelay` too long** — the drone only re-scans every N seconds, so there's inherent
   lag between an enemy entering range and the drone reacting. Docs call this out: *"Vision.refreshDelay
   and AIAgent.Update determine how quickly an enemy notices/retargets."*
3. **LoS occlusion** — an enemy behind `blockingLayers` cover isn't seen even in range.
4. **Owner leash** — `MoveAroundOwnerAction` + `OwnerIsWithinRangeCondition` pull the drone back to
   the player, so it may disengage/re-tether and look hesitant.
5. **Arbitrary target pick** — `visibleEnemies.First()` isn't nearest, so it can chase the "wrong"
   enemy.
6. **Fear/flee** — if the target's `powerLevel > maxPowerLevelToFight`, the drone flees instead of
   attacking.

**`AIAgent.Update` itself is not a bottleneck** — retargeting is per-frame and immediate. The latency
is entirely in the `Vision` scan (range + cadence + LoS).

## Can a drone attack the player? (friendly-fire analysis)

**Through normal perception: no — and it's structural, not a throttle.**

`AIAgent` only ever selects a target from `visibleEnemies`, and a unit lands in `visibleEnemies` only
if it is in `enemyBlackList` *or* `IsEnemiesWith` returns true (its faction is in
`faction.Enemies`). A player-allied minion's faction lists the player as an ally (or simply not as an
enemy), so **the player never enters `visibleEnemies`** — there is nothing to target and therefore no
reason for, and no evidence of, a deliberate "delay so it doesn't hit the player." That safeguard
would be solving a problem the faction system already makes impossible.

**The one edge case** — `AIAgent.HandleHitBy(attacker)` (invoked by `AggroWhenHit` when a unit takes
enough damage) adds the *attacker* to `enemyBlackList` and immediately targets it, bypassing faction:

```csharp
public void HandleHitBy(Unit other)
{
    enemyBlackList.Add(other.ComponentData.entity.instanceId);
    SetTarget(other);
    ...
}
```

So *if* a minion prefab carries an `AggroWhenHit` component **and** the player manages to deal enough
damage to their own drone, the drone would retaliate against the player. In practice minions usually
lack `AggroWhenHit` and same-faction weapons don't damage allies — but it's the only code path by
which a drone could ever target a friendly, and worth checking on the specific prefab.

## Quirks worth knowing

- **`Vision.Start` jitter looks like a bug.** `lastRefreshTime = Time.time - refreshDelay *
  Random.Range(0f, refreshDelay)` scales the offset by `refreshDelay` *twice*. The intent was clearly
  to jitter the first scan by up to one `refreshDelay` to spread CPU load; as written the offset is
  `refreshDelay × U(0, refreshDelay)`. For `refreshDelay < 1` this delays the first scan by less than
  intended; for `refreshDelay > 1` some minions scan on their very first frame. Minor, but relevant if
  you tune `refreshDelay` high and see inconsistent first-aggro timing.
- **Faction "neither" units are ignored.** A unit whose faction is in neither `Allies` nor `Enemies`
  is neither friend nor foe — it's silently dropped from both sets and never targeted.
- **Invisible units are unseeable** — `Vision.IsVisible` excludes `ComponentData.IsInvisible`.

## Modding anchors

| Goal | Where |
|---|---|
| Widen drone sight | `ComponentScanner<Unit>.range` (`Vision.Range` setter) — raise it |
| Faster reaction | `Vision.refreshDelay` (lower it), or prefix `Vision.Update` to force `Scan()` |
| Target nearest, not arbitrary | postfix/replace `AIAgent.SelectTargetFromVisibleEnemies` to pick min-distance |
| Never flee | patch `Unit.IsAfraidOf` → `false` (or raise `maxPowerLevelToFight`) |
| Change friend/foe | edit the minion prefab's `Faction` asset (`Allies`/`Enemies`), or seed `AIAgent.Data.friendWhiteList`/`enemyBlackList` |
| Read live prefab values | Harmony-postfix `Vision.Start`, reflect `range`/`refreshDelay` (see snippet above) |
| Change orbit leash | `MoveAroundOwnerAction.distance`, `OwnerIsWithinRangeCondition.maxDistance` (prefab-serialized) |
| Ownership plumbing | `MinionOwnerSetter`, `Unit.SetOwner`, `Unit.OwnerConnectionType` |

## Related docs

- **`05-enemies-ai.md`** — the full AI state machine, actions/conditions, and the `Vision`/`AIAgent`
  classes shared with enemies.
- **`04-modules-grid.md`** — `SpawnMinionModule`/`SpawnMinionModuleData` and the minion
  `SimpleModuleGrid`.
- **`02-player-ship.md`** — `Unit` faction/power fields and `OwnerConnectionType`.
- **`03-weapons-projectiles.md`** — `MinionSpawnerWeapon`, `HomingTargetFromAIAgent` projectile
  homing.
</content>
</invoke>
