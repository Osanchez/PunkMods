# Shops, Upgrades, Economy & Meta Progression
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

This document covers how PUNK handles money, buying/selling, station upgrades, the run lifecycle, what carries over between runs, and the leaderboards. All types live in the global namespace (no `namespace` declaration) unless noted, so Harmony patches reference them by bare class name.

### Currency & prices

PUNK has **two kinds of currency**, both expressed through the `Price` struct (`Price.CurrencyType`):

- **`Resource`** — fuel/score/energy-style tanks that live on a `Unit` (the ship). Spending a resource decrements `unit.GetTank(resource).Value`. Resources are also the currency for station upgrades.
- **`Ingredient`** — discrete collectible items stored in the shared `Vault` (`vault.AmountOf(ingredient)` / `vault.Remove(ingredient, n)`).

A `Price` carries a `float amount` but is always spent/compared floored to an int (`AmountFloored => Mathf.FloorToInt(amount)`). `Price.CanAfford(Unit, Vault)` is the single affordability check used everywhere. Prices are **per-currency lists** (`List<Price>`) — an item can cost several currencies at once.

Shop prices are data-driven from a CSV parsed by `ShopItemsConfig` (a `ConfigRegistry<ShopItemConfig,string>`). Each row maps a Module or Consumable to a base `price`, a `priceIncrement` (added each time a repeatable item is rebought), and optional ingredient `unlockRequirements`. The CSV header row names the currency columns; each currency takes 3 CSV columns (price, increment, unlock-requirement flag).

Two cost interfaces exist for weapons/abilities (not shop prices): `IHasCost` (`CostAmount`, `CostCurrency`) and `IHasPerProjectileCost` (`CostPerProjectile`, `CostResource`) — these describe firing costs, documented here only because they share the "cost" vocabulary.

### Shops

Each `Station` owns a `Shop` (MonoBehaviour). The shop's inventory is **not** stored on the Shop — it is the run-global `RunData.GeneralShopItemList` (a `ShopItemList`). So all stations share one evolving module inventory, plus a separate `RunData.ConsumableShopItems` list. `Shop.Purchase` spends the price, then either removes the item or (if `ModuleData.repeatInShop`) raises its price via `ShopItem.IncreasePrice` and regenerates a fresh module instance. Buying moves the module into the player's grid flow; the consumable shop (`ConsumablesShopWidget`) adds the consumable to the `Vault`.

Inventory generation is driven by `ShopUpgradeData` (a ScriptableObject of fixed starter items plus per-shop-level weighted `ShopItemGroup` distributions). Every time a station is unlocked, `RunData.RegisterShopUnlock` rolls each group's probability and draws a not-yet-offered module into the shop.

### Upgrades

There are two distinct "upgrade" concepts:

- **Station upgrades** (`StationUpgrade`): unlocking/leveling a station itself. Cost is a flat `int cost` in a single `Resource`, with `priceIncreaseMode` (Add or Multiply) scaling per previous purchase. Handled directly in `Station.TryInstallUpgrade` (not through `Shop`/`Price`). Purchase counts persist in `RunData`.
- **Shop "upgrade data"** (`ShopUpgradeData`): despite the name, this is the *inventory configuration* for shops, not a player upgrade tree.

### Run lifecycle & what persists

A run is configured by `RunArguments` (coop flag, seed, optional `DailyChallengeData`, `startingLoadout`, continue flag, save folder). `RunSetupScene`/`RunSetupScreen` build the arguments and hand them to `GameScene.GoToGameScene`. `RunData` (installed per-run by `RunDataInstaller`) holds all in-run progression and is serialized via the `IMementoOriginator` save system (see the save/load doc). **Within a run**, `RunData` persists shop inventory, shared resource tanks, ingredients-ever-owned, dropped/picked-up/added modules, station-upgrade purchase counts, run time, and kill counts — these survive save/continue but are wiped on a fresh run.

**Between runs (true meta progression)** almost nothing carries over. Persistent state lives only in `PlayerPrefs` via `MetaProgressManager`: total death count and the set of unlocked loadouts. That's it — no persistent currency or upgrade tree. `RunEndedEvent` is an analytics event, not saved progression.

### Leaderboards

Score = the current value of a designated "score" shared resource (`GameController.Score`). On game over or win, `LeaderboardScoreSubmitter` uploads to the daily-challenge leaderboard via `PlatformFeatures.SubmitScoreToLeaderboard`. The Steam implementation (`SteamPlatformFeatures` + `UploadSteamLeaderboardRequest`/`LoadSteamLeaderboardRequest`) talks to `SteamUserStats`. The daily seed + leaderboard id come from a remote HTTP endpoint (`DailyChallengeInfo`). **Score is computed entirely client-side and uploaded with `ForceUpdate`, so it is fully spoofable — see Modding Notes.**

## Class Index

| Class | Kind | Role |
|---|---|---|
| `Price` | struct | A single currency cost (Ingredient or Resource + amount); affordability check. |
| `IHasCost` | interface | Firing/usage cost (`CostAmount`, `CostCurrency`). |
| `IHasPerProjectileCost` | interface | Per-projectile firing cost. |
| `Vault` | class (service) | Player inventory: modules, consumables, ingredients. |
| `ShopItemConfig` | class | CSV-parsed config row: base price, increment, unlock requirements. |
| `ShopItemsConfig` | ScriptableObject (ConfigRegistry) | Parses shop CSV into `ShopItemConfig`s. |
| `ShopUpgradeData` | ScriptableObject | Starter items + per-shop-level weighted group distributions. |
| `ShopItemGroup` | ScriptableObject | A weighted `ModuleDataDistribution` of modules. |
| `ShopItem` | class | A buyable module instance with price/level; memento-saved. |
| `ConsumableShopItem` | class | A buyable consumable with price; memento-saved. |
| `ShopItemList` | class | The run's module shop inventory; tracks "new" items. |
| `Shop` | MonoBehaviour | Per-station purchase logic over `RunData.GeneralShopItemList`. |
| `StationUpgrade` | class | A station unlock/upgrade: flat cost + scaling mode. |
| `Station` | SavableComponent | Owns a `Shop`; installs `StationUpgrade`s. |
| `RunData` | class (service) | All in-run progression + shop inventory; memento-saved. |
| `RunDataInstaller` | MonoBehaviour | Installs `Seed` + `RunData` services per run. |
| `RunArguments` | struct | Run config (coop, seed, loadout, daily challenge, continue). |
| `RunSetupScene` | static class | Builds `RunArguments`, loads loadout selector scene. |
| `RunSetupScreen` | MonoBehaviour | Loadout/input selection UI; starts the game. |
| `RunEndedEvent` | class (Analytics Event) | Analytics payload for run end. |
| `MetaProgressManager` | class (service) | PlayerPrefs persistence: death count + unlocked loadouts. |
| `LoadoutTemplate` | ScriptableObject | A starting module loadout; `unlockingModules` gate it. |
| `LoadoutPool` | ScriptableObject | List of all `LoadoutTemplate`s. |
| `LoadoutUnlocker` | MonoBehaviour | Unlocks loadouts when their modules are installed. |
| `LoadoutSelector` | MonoBehaviour | Run-setup loadout picker; respects lock state. |
| `DailyChallengeInfo` | MonoBehaviour | Fetches daily seed + leaderboard id from web API. |
| `PlatformFeatures` | abstract class (service) | Platform abstraction for leaderboards/auth. |
| `SteamPlatformFeatures` | class | Steam implementation of `PlatformFeatures`. |
| `LeaderboardScoreSubmitter` | MonoBehaviour | Uploads score on game over/win. |
| `LeaderboardComponent` | MonoBehaviour | Leaderboard UI (day navigation, entry list). |
| `LeaderboardAdapter` | abstract class | Binds platform entry data into UI rows. |
| `SteamLeaderboardAdapter` | class | Steam leaderboard row binder. |
| `LeaderboardEntry` | class | Plain rank/score/name DTO. |
| `LeaderboardEntryComponent` | MonoBehaviour | A single leaderboard row UI. |
| `LeaderboardScene` | static class | Loads/returns from the highscores scene. |
| `UploadSteamLeaderboardRequest` | class | Steam score upload call wrapper. |
| `LoadSteamLeaderboardRequest` | class | Steam score download call wrapper. |
| Shop widgets | MonoBehaviours | `ShopWidget`, `ShopItemWidget`, `ConsumablesShopWidget`, `PriceWidget`. |

## Classes

### Price
- **Kind:** `[Serializable] struct`; implements `IMementoOriginator<Price.Memento>`.
- **Purpose:** One unit of cost in a single currency.
- **Key fields:** `CurrencyType currencyType`, `Ingredient ingredient`, `Resource resource`, `float amount`.
- **Key members:** `int AmountFloored`; `bool CanAfford(Unit, Vault)`; `bool HasSameCurrency(Price other)`; `CreateMemento()`/`RestoreFromMemento()` (memento stores `amount`, `currencyType`, `currencyId`).
- **Enum:** `CurrencyType { Ingredient, Resource }`.
- **Relationships:** Used by `ShopItem`, `ConsumableShopItem`, `ShopItemConfig`, `PriceWidget`. Resource costs hit `Unit` tanks; ingredient costs hit `Vault`.

### IHasCost / IHasPerProjectileCost
- **Kind:** interfaces.
- **Purpose:** Describe firing/activation cost of weapons/abilities (not shop pricing).
- **Members:** `IHasCost { float CostAmount; Resource CostCurrency; }`, `IHasPerProjectileCost { float CostPerProjectile; Resource CostResource; }`.

### Vault
- **Kind:** class, registered as a service (resolved via `ServiceLocator.Get<Vault>()`); implements `IMementoOriginator<Vault.Memento>`.
- **Purpose:** The player's persistent-within-run inventory of modules, consumables (8 fixed slots), and ingredients (the ingredient currency wallet).
- **Key fields/props:** `IReadOnlyDictionary<Ingredient,int> Ingredients`, `IEnumerable<Module> Modules`, `int ModuleCount`, `int ConsumablesCount`, `bool HasNew`.
- **Key methods:** `Store(Module, markAsNew)`, `Remove(Module)`, `Contains(Module)`; `Add(Consumable,int)`, `GetAmount(Consumable)`, `Remove(Consumable,int)`, `GetConsumableAt(int)`; `Add(Ingredient,int)` (also calls `RunData.RegisterIngredientAcquired`), `Remove(Ingredient,int)`, `AmountOf(Ingredient)`; `IsNew`/`MarkModuleSeen`.
- **Events:** `ConsumableAmountChanged(int)`, `IngredientAmountChanged(Ingredient,int,int)`, `NewModuleSeen(Module)`.
- **Relationships:** Ingredient currency lives here; spent by `Shop`/`ConsumablesShopWidget`. Memento serializes modules, ingredient ids/counts, and consumable slot ids/amounts.

### ShopItemConfig
- **Kind:** `[Serializable]` class; `IIdentifiable<string>`.
- **Purpose:** One CSV row's pricing config for a module/consumable id.
- **Key fields:** `string id`, `int lineNumber`, `List<Price> price`, `List<Price> priceIncrement`, `List<Ingredient> unlockRequirements`.

### ShopItemsConfig
- **Kind:** `[CreateAssetMenu] ScriptableObject : ConfigRegistry<ShopItemConfig, string>`.
- **Purpose:** Parses the shop CSV into the `ShopItemConfig` registry.
- **Key fields:** `ResourceRegistry`, `IngredientRegistry`, `ModuleRegistry`, `ConsumableRegistry`.
- **Key methods:** `protected override void Parse(string csv)` → `ParseCurrencyOrder`, `ParseLine`. Header row defines currency columns; data rows start at index 2; row name must start with "Module" or "Consumable".

### ShopUpgradeData
- **Kind:** `[CreateAssetMenu] ScriptableObject`.
- **Purpose:** Defines the shop's contents: fixed starters + per-shop-level weighted groups.
- **Key fields:** `List<ModuleData> fixStarterItems`, `PerLevelData[] perLevelData`, `List<Consumable> fixStarterConsumables`.
- **Nested structs:** `PerLevelData { PerLevelGroup[] groups }`, `PerLevelGroup { float probablity; ShopItemGroup group }`.
- **Relationships:** Consumed by `RunData.Initialize`, `RunData.RegisterShopUnlock`, `RunData.AddAllItemsToShop`.

### ShopItemGroup
- **Kind:** `[CreateAssetMenu] ScriptableObject`.
- **Purpose:** A weighted pool of modules for shop draws.
- **Key field:** `ModuleDataDistribution moduleDistribution`.

### ShopItem
- **Kind:** `[Serializable]` class; `IMementoOriginator<ShopItem.Memento>`.
- **Purpose:** One buyable module entry in a shop, with its own price/level.
- **Key fields:** `ModuleData moduleTemplate`, `List<Price> price`, `List<Price> priceIncrement`, `int level = 1`, `int orderInShop`. Prop `Module Module` (the live instance), `bool RepeatInShop => ModuleTemplate.repeatInShop`.
- **Key methods:** `static ShopItem CreateNew(ModuleData, ShopItemConfig)` (deep-copies module, randomizes connections, sets `orderInShop = moduleType.orderInShop*1000 + lineNumber`); `CreateNewItem()` (regenerate instance after a repeat purchase); `IncreasePrice(float incrementMultiplier)` (adds `priceIncrement` into `price` per matching currency); memento save/restore.
- **Relationships:** Held in `ShopItemList`; bought through `Shop.Purchase`.

### ConsumableShopItem
- **Kind:** class; `IMementoOriginator<ConsumableShopItem.Memento>`.
- **Purpose:** Buyable consumable with price; mirrors `ShopItem` but simpler.
- **Key fields:** `Consumable consumable`, `List<Price> price`, `List<Price> priceIncrement`.
- **Key methods:** `static CreateNew(Consumable, ShopItemConfig)`, `IncreasePrice(float)`, memento save/restore.
- **Note:** Referenced in the task as `ConsumableShopItem(ref)`. Bought via `ConsumablesShopWidget.TryBuy`, which adds it to the `Vault` and raises its price.

### ShopItemList
- **Kind:** class; `IMementoOriginator<ShopItemList.Memento>`.
- **Purpose:** The run's module shop inventory + "new item" tracking.
- **Key members:** `List<ShopItem> Items`, `bool HasNewItems`; `Add` (inserts at front, marks new), `Remove`, `Contains(ShopItem)`, `Contains(ModuleData)`, `IsNew`, `MarkItemSeen(Module)`. Memento serializes all items.
- **Relationships:** Owned by `RunData.GeneralShopItemList`; every `Shop` reads it.

### Shop
- **Kind:** `MonoBehaviour`.
- **Purpose:** Per-station purchase logic; UI entry point for shopping.
- **Key fields:** `coopPriceIncrementMultiplayer`, `purchaseSfx`; cached `ShipMenuToggler`, `LootFactory`, `Vault`, `RunData`, `Ship`.
- **Key props:** `ShopItemList ShopItemList => runData.GeneralShopItemList`, `IReadOnlyList<ShopItem> Items`.
- **Key methods:**
  - `StartShopping(Ship)` → `runData.AddShopItemsWhereRequirementsMet()` then opens the ship menu shop.
  - `Purchase(Module)` / `Purchase(ShopItem)` — if `CanPurchase`: when **not** `runData.AllShopItemsAreFree`, deducts each `Price` (ingredient → `vault.Remove`, resource → `ship.Unit.GetTank(res).Value -= amount`); if `RepeatInShop` raises price (×coop multiplier in coop) and regenerates, else removes from list; registers the module as dropped and plays SFX.
  - `CanPurchase(ShopItem)` — returns true immediately if `AllShopItemsAreFree`, else checks every `Price.CanAfford`.
  - `GetShopItemForModule(Module)`.
- **Event:** `Action<ShopItem> ItemPriceChanged`.
- **Harmony-relevant:** `Purchase`, `CanPurchase` are the prime free-shop / no-cost targets.

### StationUpgrade
- **Kind:** `[Serializable]` class.
- **Purpose:** A station unlock/upgrade definition.
- **Key fields:** `string id`, `int cost`, `Resource resourceUsed`, `GameObject activatedObject`, `string animTriggerName`, `Sprite mapIconSprite`, `PriceIncreaseMode priceIncreaseMode`, `float priceIncreaseAmount`.
- **Enum:** `PriceIncreaseMode { Add, Multiply }`.

### Station
- **Kind:** `SavableComponent<Station.Data>`.
- **Purpose:** A station building; gates its shop behind installing its first `StationUpgrade`.
- **Key fields:** `StationUpgrade[] upgrades`, `Shop shop`, plus prompt/platform/tracking objects.
- **Key methods (economy-relevant):**
  - `OnUseActivated(Interactor)` — if locked, `TryInstallUpgrade(upgrades[0], unit)` then `runData.RegisterShopUnlock()`; else `shop.StartShopping(ship)`.
  - `TryInstallUpgrade(StationUpgrade, Unit installer)` — if `installer.GetResource(upgrade.resourceUsed) >= cost`, deduct from the tank, `runData.RegisterStationUpgradePurchase(id)`, then `ComponentData.Install(upgrade)`.
  - `CalculateUpgradeCost(StationUpgrade)` — `Add`: `cost + purchases*priceIncreaseAmount`; `Multiply`: `cost * priceIncreaseAmount^purchases`.
- **Nested `Station.Data`:** `List<StationUpgrade> allUpgrades`/`installedUpgrades`, `bool IsUnlocked => installedUpgrades.Count > 0`, event `UpgradeInstalled`, `Install(id)`/`Install(StationUpgrade)`. Memento stores installed upgrade ids.
- **Harmony-relevant:** `TryInstallUpgrade`, `CalculateUpgradeCost` for free/cheap station upgrades.

### RunData
- **Kind:** class, service (`ServiceLocator.Get<RunData>()`); `IInitializable`, `IMementoOriginator<RunData.Memento>`.
- **Purpose:** The central in-run state container — shop inventory, shared resources, run statistics, and the source of all per-run progression flags.
- **Key fields/props:** `ShopItemList GeneralShopItemList`, `List<ConsumableShopItem> ConsumableShopItems`, `IEnumerable<ResourceTank> SharedResourceTanks`, `IEnumerable<ModuleData> DroppedModules`, `int KilledBossCount`, `int KilledEnemyCount`, `int UnlockedStationCount`, `float TotalRunTime { get; set; }`, **`bool AllShopItemsAreFree { get; set; }`** (the free-shop master switch), private `purchasedStationUpgradeCounts`, `shopUpgradeData`, `shopUnlockRnd`, ingredient/module tracking lists.
- **Key methods:**
  - `Initialize()` — builds starter shop from `ShopUpgradeData.fixStarterItems`/`fixStarterConsumables`, seeds shared resource tanks (infinite capacity), calls `RegisterShopUnlock`.
  - `RegisterShopUnlock()` — per-level weighted draw of new modules into the shop; increments `unlockedShopCount`.
  - `AddShopItemsWhereRequirementsMet()` — adds config items whose `unlockRequirements` are all in `ingredientsEverOwned`.
  - `AddAllItemsToShop()` — dumps every module from every group into the shop (used by debug "unlock everything").
  - `GetTimesStationUpgradePurchased(id)`, `RegisterStationUpgradePurchase(id)`, `RegisterModuleDropped/PickedUp`, `RegisterIngredientAcquired`, `RegisterEnemyKilled`, `RegisterBossKilled`.
  - `CreateMemento()`/`RestoreFromMemento()` — serialize shop, consumables, shared resource values, ingredients-ever-owned, module id lists, run time, kill counts. (Note: `AllShopItemsAreFree` and `purchasedStationUpgradeCounts` are **not** in the memento.)

### RunDataInstaller
- **Kind:** `MonoBehaviour, IServiceInstaller`.
- **Purpose:** Installs a fresh `Seed` (from `GameScene.arguments.seed`, random if 0) and a `RunData` into the service container at run start.

### RunArguments
- **Kind:** `[Serializable] struct`.
- **Purpose:** Everything needed to start/continue a run.
- **Key fields:** `InputDevice leftDevice/rightDevice`, `bool isCoop`, `int seed`, `DailyChallengeInfo.DailyChallengeData dailyChallengeData`, `LoadoutTemplate startingLoadout`, `bool isContinue`, `string saveFolder`.
- **Factories:** `static NewRun(bool isCoop)`, `static Continue(bool isCoop, string saveFolder)`.

### RunSetupScene / RunSetupScreen
- **RunSetupScene** (static): holds `static RunArguments arguments`; `GoToLoadoutSelector(coop, isContinue)` builds args (new vs continue, pulling save folder from `GameSaver.GetSaveFolderName`) and loads the "LoadoutSelector" scene.
- **RunSetupScreen** (MonoBehaviour): drives loadout/input selection. On loadout chosen → `arguments.startingLoadout = template; GameScene.GoToGameScene(arguments)`. Coop assigns devices first.

### RunEndedEvent
- **Kind:** `Unity.Services.Analytics.Event` ("runEnded").
- **Purpose:** Analytics only. Settable params: `RunDuration` (int), `EndType` (string), `FuelLevel` (float). Not persisted progression.

### MetaProgressManager
- **Kind:** plain class, service.
- **Purpose:** The **only** cross-run persistence, backed by `PlayerPrefs`.
- **Keys:** `META_UNLOCKED_LOADOUTS`, `META_TOTAL_DEATH_COUNT`.
- **Methods:** `int GetTotalDeathCount()`, `void RegisterDeath()` (called from `GameController.OnGameOver`); `string[] GetUnlockedLoadouts()` (semicolon-split), `bool UnlockLoadout(LoadoutTemplate)` (returns false if already unlocked), `static void ResetUnlockedLoadouts()`.
- **Harmony-relevant:** patch `GetUnlockedLoadouts` or `UnlockLoadout` to unlock all loadouts.

### LoadoutTemplate
- **Kind:** `[CreateAssetMenu] ScriptableObject`.
- **Purpose:** A starting ship configuration.
- **Key fields:** `string displayName`, `string description`, `ModuleData embedded/primary/secondary/active1/active2/active3`, **`ModuleData[] unlockingModules`** (if non-empty, the loadout is locked until one of these modules is installed in a run).
- **Key method:** `Apply(ModuleGridOwner.Data)` — installs each module into fixed grid positions. (Applied at run start by `ShipManager`.)

### LoadoutPool
- **Kind:** `[CreateAssetMenu] ScriptableObject`. `List<LoadoutTemplate> loadouts`.

### LoadoutUnlocker
- **Kind:** `MonoBehaviour`.
- **Purpose:** Listens for module installs during a run; when an installed module is in a loadout's `unlockingModules`, calls `MetaProgressManager.UnlockLoadout` and logs it.
- **Hooks:** subscribes on `GameController.GameStarted` to each ship's `ModuleGrid.ModuleInstalled`.

### LoadoutSelector
- **Kind:** `MonoBehaviour`.
- **Purpose:** Run-setup loadout picker.
- **Key logic:** `IsLocked(LoadoutTemplate)` returns true when the loadout has `unlockingModules` and its name isn't in `metaProgressManager.GetUnlockedLoadouts()`. `PickLoadout` fires `LoadoutSelected` only if not locked.
- **Harmony-relevant:** patch `IsLocked` to return false → all loadouts selectable.

### DailyChallengeInfo
- **Kind:** `MonoBehaviour`.
- **Purpose:** Fetches the daily seed and leaderboard id from a remote HTTP API.
- **Key members:** `static DailyChallengeData latestData`, `static Action DailyChallengeInfoChanged`, `Refresh()` → coroutine `GET https://www.powernapgames.com/api/get_daily_seed.php`, JSON-deserialized.
- **Nested `DailyChallengeData`:** `int seed`, `string leaderboardId`, `DateTime serverTime` (microsecond-epoch JSON converter), `responseClientTime`, `EstimateCurrentServerTime()`.

### PlatformFeatures (abstract) / SteamPlatformFeatures
- **PlatformFeatures:** service base. `Action Initialized`, `Action<int> LeaderboardEntriesChanged`, `bool IsInitialized`. Abstract: `Initialize`, `Authenticate`, `SubmitScoreToLeaderboard(name, score, isCoop)`, `LoadLeaderboardEntries(name)`, `HasPlayerParticipateOnDailyChallenge(...)`, `GetLeaderboardAdapter()`. (A `DummyPlatformFeatures` also exists for non-Steam builds.)
- **SteamPlatformFeatures:** `const int LEADERBOARD_VERSION = 1`. Implements submit via `UploadSteamLeaderboardRequest`, load via `LoadSteamLeaderboardRequest`, exposes `LeaderboardScoresDownloaded_t LeaderboardResult`, returns `SteamLeaderboardAdapter`.

### LeaderboardScoreSubmitter
- **Kind:** `MonoBehaviour`.
- **Purpose:** On `GameController.GameOver` **or** `GameController.GameWon`, uploads `gameController.Score` to `DailyChallengeData?.leaderboardId ?? "test"` via `platformFeatures.SubmitScoreToLeaderboard(id, score, isCoop)`.
- **Integrity note:** score is read straight from `GameController.Score` (a shared-resource tank value) with no validation.

### LeaderboardComponent
- **Kind:** `MonoBehaviour`.
- **Purpose:** Highscores UI. Tracks `daysOffset`; `Reload()` computes the date string `yyyyMMdd` from `DailyChallengeInfo.latestData.EstimateCurrentServerTime()` and calls `platformFeatures.LoadLeaderboardEntries`. On `LeaderboardEntriesChanged`, instantiates one `LeaderboardEntryComponent` per entry and binds via the adapter.

### LeaderboardAdapter (abstract) / SteamLeaderboardAdapter
- **LeaderboardAdapter:** `UpdateView(LeaderboardEntryComponent, int position)`.
- **SteamLeaderboardAdapter:** reads `SteamUserStats.GetDownloadedLeaderboardEntry`, fills rank/name/score/avatar, and decodes coop flag from the entry's detail ints (`details[0]==1` ⇒ coop = `details[1] > 0`). Note the upload writes details `{1, isCoop?0:1}`.

### LeaderboardEntry / LeaderboardEntryComponent
- **LeaderboardEntry:** plain DTO `{ int rank; int score; string name; }`.
- **LeaderboardEntryComponent:** UI row — `Image avatar`, `TMP_Text rank/playerName/score`, keyboard/gamepad icons, `SetCoopMode(bool)` (currently empty body).

### LeaderboardScene
- **Kind:** static class. `static string previousSceneName`; `Load()` records current scene and loads "HighscoresScreen"; `Back()` returns.

### UploadSteamLeaderboardRequest
- **Kind:** class wrapping a Steam call chain.
- **Ctor:** `(string leaderboardName, int score, bool isCoop)`. `Excecute(Action<bool> callback)` → `FindOrCreateLeaderboard` (descending, numeric) → `UploadLeaderboardScore` with **`k_ELeaderboardUploadScoreMethodForceUpdate`** and details `int[]{1, isCoop?0:1}`.

### LoadSteamLeaderboardRequest
- **Kind:** class wrapping a Steam download chain.
- **Ctor:** `(string leaderboardName, ELeaderboardDataRequest = Global, int rangeStart = 0, int rangeEnd = 100)`. `Excecute(Action<LeaderboardScoresDownloaded_t, bool>)` → `FindLeaderboard` → `DownloadLeaderboardEntries`.

### Shop UI widgets (reference)
- **ShopWidget** (`: ModuleContainerWidget`): renders module shop list ordered by `ShopItem.orderInShop`, with type separators; `CanMove` calls `currentShop.CanPurchase`; handles selection/drag-to-buy and "new item" animations.
- **ShopItemWidget:** one module row — icon, name, instantiates a `PriceWidget` per `Price`; `Clicked` event.
- **ConsumablesShopWidget:** the consumable shop. `TryBuy(ConsumableShopItem)` checks `CanPurchase`, deducts price (unless `AllShopItemsAreFree`), `vault.Add(consumable, 1)`, raises price (×coop multiplier in coop), refreshes.
- **PriceWidget:** shows one `Price`'s icon + amount; colors text by `Price.CanAfford`.

## Modding Notes (Harmony / BepInEx)

All types are in the global namespace; reference them by bare name in `AccessTools`/`HarmonyPatch`.

### Free shops (modules & consumables)
Two clean approaches:
1. **Flip the built-in flag.** Set `RunData.AllShopItemsAreFree = true`. Every purchase path (`Shop.Purchase`, `Shop.CanPurchase`, `ConsumablesShopWidget.TryBuy`/`CanPurchase`) already short-circuits cost when this is true. The game's own `DebugMenu` toggles exactly this (`DebugMenu.cs:527`).
   - `var rd = ServiceLocator.Get<RunData>(); rd.AllShopItemsAreFree = true;` (do it after `RunData` is installed; e.g. patch `RunData.Initialize` postfix).
2. **Patch the gates.** Prefix `Shop.CanPurchase(ShopItem)` and `ConsumablesShopWidget.CanPurchase` to `return true`, and prefix `Shop.Purchase`/`ConsumablesShopWidget.TryBuy` to skip deduction (or just rely on the flag — simpler).

### Unlimited currency
- **Ingredients:** patch `Price.CanAfford(Unit, Vault)` postfix to `__result = true`, or prefix `Vault.AmountOf(Ingredient)` to return a large number. To actually grant ingredients, call `Vault.Add(ingredient, n)`.
- **Resources (fuel/score/energy):** resource costs deduct `unit.GetTank(resource).Value`. Setting `AllShopItemsAreFree` covers shop costs; for station upgrades and weapon costs, patch `Price.CanAfford` and/or the tank value. Station upgrades read `installer.GetResource(upgrade.resourceUsed)` in `Station.TryInstallUpgrade`.

### Free / unlocked station upgrades
- Prefix `Station.CalculateUpgradeCost` → `__result = 0` (also makes the hover price show 0), or prefix `Station.TryInstallUpgrade` to force-install. The cost check is `installer.GetResource(...) >= cost`.

### Unlock all upgrades / shop inventory
- Call `RunData.AddAllItemsToShop()` to push every configured module into the shop immediately (this is what debug uses). For per-requirement items, `RunData.AddShopItemsWhereRequirementsMet()` runs on every `Shop.StartShopping`.

### Unlock all loadouts (meta)
- Best single target: prefix `LoadoutSelector.IsLocked` → `__result = false` (UI-level unlock for the current session).
- Persistent: patch `MetaProgressManager.GetUnlockedLoadouts` to return all `LoadoutPool` loadout names, or call `MetaProgressManager.UnlockLoadout(template)` for each. Underlying store is `PlayerPrefs["META_UNLOCKED_LOADOUTS"]` (semicolon-joined `LoadoutTemplate.name`s) — you can also write it directly.

### Free / reset meta progression
- There is essentially no persistent currency to grant; meta state is only death count (`META_TOTAL_DEATH_COUNT`) and unlocked loadouts (`META_UNLOCKED_LOADOUTS`) in `PlayerPrefs`. `MetaProgressManager.ResetUnlockedLoadouts()` clears the loadout key.

### Key Harmony target summary
| Goal | Target member |
|---|---|
| Free module shop | `RunData.AllShopItemsAreFree` (set true) or `Shop.CanPurchase` / `Shop.Purchase` |
| Free consumable shop | `ConsumablesShopWidget.CanPurchase` / `TryBuy` (or the flag) |
| Always affordable | `Price.CanAfford` (postfix `__result=true`) |
| Free station upgrades | `Station.CalculateUpgradeCost` / `Station.TryInstallUpgrade` |
| All shop items | `RunData.AddAllItemsToShop()` |
| All loadouts (UI) | `LoadoutSelector.IsLocked` (postfix `__result=false`) |
| All loadouts (persistent) | `MetaProgressManager.GetUnlockedLoadouts` / `UnlockLoadout` |

### ⚠️ Leaderboard integrity warning
**Cheating and leaderboard submission together are a real problem.** The daily-challenge leaderboard is the game's competitive feature, and the score pipeline is trivially exploitable:

- `GameController.Score` is just the value of a shared "score" `ResourceTank` — anything that grants resources (free shops, unlimited currency, AddAllItemsToShop) can inflate it.
- `LeaderboardScoreSubmitter` uploads that number on **every** game over or win with **zero server-side validation**.
- `UploadSteamLeaderboardRequest` uses `k_ELeaderboardUploadScoreMethodForceUpdate`, so a modded client can post any score it wants.

If your mod alters the economy, run difficulty, score resource, loadouts, station upgrades, or `RunData`, you should **disable leaderboard submission** to avoid polluting the official daily boards and risking other players' trust (and your own Steam standing). Practical options:
- Prefix `LeaderboardScoreSubmitter.UploadScore` (private) — or its `OnGameOver`/`OnGameWon` callers — to `return false` (skip).
- Prefix `PlatformFeatures.SubmitScoreToLeaderboard` / `SteamPlatformFeatures.SubmitScoreToLeaderboard` to no-op.
- Or only submit on genuinely unmodified runs. Treat leaderboard submission as opt-out by default in any cheat/economy mod.
