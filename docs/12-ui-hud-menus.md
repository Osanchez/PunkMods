# UI, HUD, Menus & Screens
> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

PUNK's interface is **not** a single central UI manager that owns a stack of screens. Instead it is a set of largely independent `MonoBehaviour` controllers wired together in the Unity scenes, coordinated through a handful of services obtained via `ServiceLocator.Get<T>()` (e.g. `CursorController`, `TimeManager`, `ShipManager`, `GameController`, `RunData`, `Vault`, `LastUsedDeviceTracker`). Understanding the UI means understanding *which controller owns which canvas* and *which events it subscribes to*, because that is where a modder hooks in.

The pieces fall into five groups:

- **Generic screen plumbing** — `UIScreen` (the reusable "open/close a canvas, switch the input map, pause time, animate" component), `AnimatedScreen` + `AnimatedScreenElement` (staggered show/hide animations), and `Prompt`/`Popup` (yes/no confirmation dialogs). Many concrete screens are a thin controller that holds a serialized `UIScreen screen` field and calls `screen.Open()` / `screen.Close()`.
- **Selection / focus management** — `UiManager` keeps a stack of `DefaultSelector`s so the correct UI element is auto-selected when a gamepad is used. This is the closest thing to a global "UI manager," but it only manages *which control is focused*, not screen visibility.
- **The in-game HUD** — `InGameHud` (damage/cell-type animator driver + show/hide), `ShipHud` (per-ship resource bars, ability slots, minions, ship log), and the floating world-space HUD elements `Crosshair`, `OffscreenIndicator`, `Minimap`, `MapStateIndicator`, `InteractionPrompt`, healthbars.
- **Screens & menus** — full-screen states: `MainMenu`, `RunSetupScreen`/`InputSelectorScreen`, `LoadingScreen`, `SplashScreen`, `PauseScreen`, `GameOverScreen`, `GameWonScreen`, `OptionsScreen` (+ its tabs), `DebugMenu`/`SecondaryDebugMenu`, `InstrumentMenu`, the tabbed in-game ship menu (`ShipMenuToggler` driving `ShipMenuTab` subclasses: map, `ModuleGridScreen`, `ConsumablesScreen`), and the radial `ConsumableWheel`.
- **Widgets & input maps** — many small data-bound widgets (resource bars, module icons, shop items, price tags, etc.) and the per-ship UI input maps (`UIActionMap`, `ItemWheelActionMap`, `InstrumentMenuActionMap`) built on the shared `ShipActionMap` base.

Scene transitions are static helpers, not UI objects: `MainMenuScene.Load()`, `GameScene.GoToGameScene(args)` / `GameScene.Continue(coop)`, `RunSetupScene.GoToLoadoutSelector(coop, isContinue)`, `GameOverMenu.Load()`. These call `UnityEngine.SceneManagement.SceneManager.LoadScene(...)`.

Device-adaptive hinting is everywhere: `LastUsedDeviceTracker.GamepadLastUsed` decides whether keyboard or gamepad button hints show (`AdaptiveInputHint`, `ButtonHint`, `PlatformSpriteSet`).

## Screen / Menu Flow

**Boot → Main menu**
1. `SplashScreen` fades a `Fader` in/out for `defaultDuration` (or until any button) then `SceneManager.LoadScene(buildIndex + 1)`.
2. Main menu scene runs `MainMenu`. `MainMenu.SelectGameMode(bool coop)` checks `GameSaver.SavedGameExists(coop)`: if a save exists it shows "step 2" buttons (Continue / New Run via `newRunPrompt`), otherwise calls `StartNewRun()` directly.
   - **New run:** `RunSetupScene.GoToLoadoutSelector(coop, isContinue:false)` → loads `"LoadoutSelector"` scene.
   - **Continue (single):** `GameScene.Continue(coop)` → loads `"Game"`.
   - `StartGameButton` / `StartGameOnAnyButton` / `ContinueButton` are just thin `Button` wrappers that call back into `MainMenu`.

**Run setup (loadout + co-op device assignment)**
3. `RunSetupScreen` reads `RunSetupScene.arguments`. In co-op it first shows `InputSelectorScreen` (drag each device left/right; fires `DevicesAssigned(left,right)`), then the `LoadoutSelector`. In single-player it shows only the loadout selector. `RunSetupScreen.OnLoadoutSelected(template)` sets `arguments.startingLoadout` and calls `GameScene.GoToGameScene(arguments)`.

**Loading → in-game**
4. `LoadingScreen` (in the Game scene) listens to `LevelGenerator.StepStarted/StepFinished` and `GameController.LevelGenerated`, printing generation progress. When generation is done it shows a "press any button" prompt; on input it calls `GameController.StartGame()` and hides itself.
5. `GameController` fires `GameStarted`. The HUD controllers (`InGameHud`, `ShipHud` via assignment, `ShipMenuToggler`, `ConsumableWheel`, `MinionsWidget`, `IngredientsBar`, `InputSelectorPopup`) subscribe to `GameController.GameStarted` to bind to the live `Ship`s.

**In-game overlays**
- **Ship menu (map / modules / consumables):** `ShipMenuToggler` listens to each ship's `PlayerInput.onActionTriggered`. The `open` action calls `Open(playerInput, tabIndexMap, nearStation)`; it pauses time, switches every player to the `"MapControl"` action map, disables ship control, and shows a `ShipMenuTab`. `next/previousTab` cycle tabs; `back` calls `activeTab.OnBackPressed()` (and closes if it returns false); `close` closes. `OpenShop(ship, station)` opens directly on the module-grid tab. Closing switches everyone back to `"ShipControl"`.
- **Consumable wheel:** `ShipControlActionMap.OpenItemWheel` → `ConsumableWheel.OpenWheel(shipInput)`; radial selection via `ItemWheelActionMap.Selection`; closing uses the hovered `Consumable`.
- **Instrument menu:** `InstrumentMenu.Open(instrument, ship)` pauses time, enables the ship's `InstrumentMenuActionMap`, and lists `InstrumentDiscoverable`s.
- **Pause:** `PauseScreen.Update()` polls `pauseAction` via `ShipManager.WasPerformedThisFrame`; `Open()` sets `gameController.isPaused = true` and calls `screen.Open()`. Buttons: Resume / Restart (`confirmRestartPrompt`) / Quit (`MainMenuScene.Load()`) / Save&Quit / Report bug. Also opened by `InputSelectorPopup` when a single-player device is lost.

**End states**
- `GameOverScreen` subscribes to `GameController.GameOver`, waits `openDelay`, fills a stats string from `RunData`, and opens its `UIScreen`. Buttons restart (`GameController.Restart()`) or quit.
- `GameWonScreen` mirrors this on `GameController.GameWon`.

**Options** can be opened from main menu or pause. `OptionsScreen` drives three `OptionsTab`s (Gameplay / Video / Audio) with its own input actions for tab switching, navigation, apply, and close.

## Class Index

| Class | Kind | Role |
|---|---|---|
| `UiManager` | Manager (service) | Stack of `DefaultSelector`s; auto-selects default control on gamepad input. |
| `DefaultSelector` | Helper | Pushes/pops itself onto `UiManager`; holds the default selected `GameObject`. |
| `UIScreen` | Generic screen | Reusable open/close: toggles a `Canvas`, switches input action map, pauses time, shows cursor, runs `AnimatedScreen`. Fires `Opened`. |
| `AnimatedScreen` | Generic screen | Staggered open/close coroutines over child `AnimatedScreenElement`s. |
| `AnimatedScreenElement` | Widget | Single animated element; `Show()`/`Hide()` toggle animator `Visible`. |
| `Prompt` | Dialog | Yes/No confirmation (uGUI/TMP + `AnimatedScreen`). `Open(posCb, negCb)`. |
| `Popup` | Dialog | Yes/No confirmation built on **UI Toolkit** (`VisualElement`), not uGUI. |
| `InfoPopup` / `ConsumableInfoPopup` | Popup | Follow-a-RectTransform tooltip with icon/name/desc; consumable variant binds a `Consumable`. |
| `InputSelectorPopup` | Popup | Co-op device-lost handler; pauses, reopens `InputSelectorScreen` (or `PauseScreen` in single). |
| `MainMenu` | Menu | Title screen flow; game-mode select, new/continue, options, exit, links. |
| `MainMenuButton`/`ContinueButton`/`StartGameButton`/`StartGameOnAnyButton`/`LoadLoadoutButton` | Buttons | Thin `Button` wrappers calling menu/loadout actions. |
| `RunSetupScreen` | Screen | Loadout + (co-op) device assignment before a run. |
| `InputSelectorScreen` | Screen | Drag devices left/right; fires `DevicesAssigned(InputDevice,InputDevice)`. |
| `LoadingScreen` | Screen | Level-gen progress + press-any-key to `GameController.StartGame()`. |
| `SplashScreen` | Screen | Fader + advance to next build-index scene. |
| `PauseScreen` | Menu | In-game pause; resume/restart/quit/save/report. |
| `GameOverScreen` / `GameWonScreen` | Screen | End-of-run overlays on `GameController.GameOver`/`GameWon`. |
| `GameOverMenu` | Static helper | Scene loader for the standalone game-over menu. |
| `OptionsScreen` | Menu | Tabbed options; own input map for nav/apply/close. |
| `OptionsTab` (abstract) + `GameplayOptionsTab`/`VideoOptionsTab`/`AudioOptionTab` | Tabs | One settings panel each; bind to `SettingsManager`/`OptionsData`. |
| `OptionsMenuitemBase` + `OptionsMenuItemButtons`/`OptionsMenuItemList`/`OptionsMenuItemSlider` | Widgets | Selectable option rows (toggle buttons / list / slider). |
| `PunkSlider` | Widget | Slider with live numeric label. |
| `DebugMenu` | Menu | Large dev cheat menu (spawn, teleport, toggles). |
| `SecondaryDebugMenu` | Menu | Generic scrollable list popup for the debug menu. |
| `DebugSaveSlotWidget` | Widget | Debug save/load slot row. |
| `SpawnEnemyButton`/`SpawnIngredientButton`/`SpawnModulePickupButton` | Buttons | Debug spawn rows. |
| `InstrumentMenu` | Menu | Radial-ish discoverable picker; uses `InstrumentMenuActionMap`. |
| `ShipMenuToggler` | Manager | In-game tabbed menu controller (map / modules / consumables). |
| `ShipMenuTab` (base) + `ModuleGridScreen`/`ConsumablesScreen` | Tabs | In-game ship-menu pages. |
| `ShipMenuTabButton` | Widget | Tab header (active state + station-aware label). |
| `ConsumableWheel` / `ConsumableWheelItem` | HUD overlay | Radial consumable selector. |
| `InGameHud` | HUD | Damage overlay + ship/cell-type HUD animators; `SetHudVisible`. |
| `ShipHud` | HUD | Per-ship: resource bars, ability slots, minions, log; platform sprite set. |
| `ShipLogDisplay` | HUD | Spawns/removes `LogEntry`s from a ship's `LogOutput`. |
| `Crosshair` | HUD (world) | Follows `Aimer.TargetPosition`. |
| `OffscreenIndicator` | HUD (world) | Clamps an off-screen target's indicator to the canvas edge. |
| `Minimap` | HUD | Burst-jobbed minimap texture + scanline; binds to `Level`. |
| `MapStateIndicator` | HUD | Status banner (e.g. "SCANNING AREA"). |
| `InteractionPrompt` | HUD (world) | "Use" prompt on an `Interactable`'s hover state. |
| `HealthbarWidget` | Widget | Generates `ResourceBar`s for a `HealthbarOwner`. |
| `ResourceBar` / `ResourceBarRow` | Widget | Unit-pip resource/health bar; binds to a `ResourceTank`. |
| `IngredientsBar` | HUD | Collected-ingredient strip; binds to `Vault`. |
| `MinionsWidget` / `MinionCountWidget` | HUD | Per-slot minion counts; binds to `Unit`. |
| `UnitResourceRechargeIndicator` | HUD (world) | Recharge VFX/SFX on a unit. |
| `PriceWidget` / `ModuleCostWidget` | Widget | Cost display (affordability-tinted). |
| `PagerWidget` | Widget | Horizontally lerps content to center a target. |
| `UndraggableScollRect` | Widget | Wheel-only (no drag) tweened scroll rect. |
| `ModuleGridWidget`/`VaultGridWidget`/`ShopWidget` (`: ModuleContainerWidget`) | Widget | Module grid surfaces (grid / vault / shop). |
| `ModuleContainerWidget` (abstract) | Widget base | Common module-container selection/move API. |
| `ModuleIconWidget` | Widget | Single module tile (icon, connections, power level). |
| `DraggedItemWidget` | Widget | The icon that follows the cursor while dragging a module. |
| `ShopItemWidget`/`ConsumableShopItemWidget`/`ConsumablesShopWidget` | Widget | Shop item rows / consumable shop list. |
| `ClusterWidget`/`ConnectionWidget`/`SpecialSlotWidget`/`ModuleEffectFieldWidget`/`ModulePropertyWidget`/`ModuleTypeSeparatorWidget` | Widget | Module-grid detail visuals. |
| `PunkButton` | Widget | Animated button wrapper; `SetToggled`/`SetInteractable`, exposes `OnClick`. |
| `ButtonHint`/`AdaptiveInputHint` | Widget | Keyboard-vs-gamepad hint swappers. |
| `ButtonSounds` | Widget | Click/select SFX on a `Button`. |
| `ShipActionMap` (abstract) + `UIActionMap`/`ItemWheelActionMap`/`InstrumentMenuActionMap` | Input maps | Per-ship UI input maps. |

## Classes

### UiManager
`MonoBehaviour`, registered as a service (`ServiceLocator.Get<UiManager>()`). Despite the name it is a **focus manager**, not a screen manager.
- Holds `Stack<DefaultSelector> activeSelectors`.
- `void Push(DefaultSelector)` — pushes a selector; if `LastUsedDeviceTracker.GamepadLastUsed`, immediately `selector.SelectDefault()`.
- `void Pop()` — pops; re-selects the new top's default if on gamepad.
- `Update()` — when the serialized `moveUiAction` (a `Vector2`) is performed this frame and the stack is non-empty, calls `Peek().SelectDefaultIfNeeded()`.
- Each `DefaultSelector` pushes itself in `OnEnable` and pops in `OnDisable`, so visibility of selectors is driven by enabling/disabling their GameObjects.

### UIScreen
`MonoBehaviour`. The generic "screen" component most menus delegate to. Serialized: `Canvas canvas`, `bool showCursor`, `bool pauseTime`, `string inputManuActionMapName` (note the misspelling — used verbatim), `GameObject selectOnOpen`, `EventSystem eventSystem`, `float closeDelay`, `AnimatedScreen animatedScreen`, `bool closeOnAwake`, `TimeManager timeManager`, `DefaultSelector defaultSelector`.
- `event Action<UIScreen> Opened`.
- `void Open()` — enables the canvas; for **every** `PlayerInput` in the scene, stores its current map and `SwitchCurrentActionMap(inputManuActionMapName)`; optionally `TimeManager.Pause(this)`, sets the selected object, shows the cursor, runs `animatedScreen.AnimateOpen()`, enables the default selector, fires `Opened`.
- `void Close()` — clears selection, hides cursor, disables selector, runs `CloseCoroutine()` (restores the previous action map on all `PlayerInput`s, runs the close animation, waits `closeDelay` realtime, disables the canvas, removes time modifiers).
- Good patch point: `Open`/`Close` are public instance methods → easy Harmony postfix to inject a custom panel whenever any screen opens.

### InGameHud
`MonoBehaviour`, fetched via `ServiceLocator.Get<InGameHud>()`. Serialized: `GameController gameController`, `Image damageOverlay`, three `Animator`s (`ship1HudAnimator`, `ship2HudAnimator`, `sharedResourcesAnimator`); also grabs its own `Animator` in `Awake`.
- Subscribes to `GameController.GameStarted`; on start, for each `Ship` it hooks `DamagableResource.onDamage`/`onShieldDamaged` and `ContainingCellPoller.ContainingCellTypeChanged`.
- `void DisplayDamage()` — `animator.SetTrigger("damage")`. `DisplayShieldDamage(Resource)` is currently an empty stub.
- `ContainingCellTypeChanged(prev,new)` — toggles animator bools named by `CellType.hudAnimParamName`.
- `void SetHudVisible(bool visible, bool animate)` — triggers `Show`/`Hide`/`SetToVisible`/`SetToHidden` on the three ship/resource animators. This is the public method other systems (e.g. `ShipMenuToggler` showcase mode) call to hide the HUD.

### ShipHud
`MonoBehaviour`, one per ship. Public fields: `AbilitySlotsPanel abilitySlotsPanel`, `Transform resourceBarParent`, `GameObject resourceBarPrefab`, `ShipLogDisplay logDisplay`, `MinionsWidget minionsWidget`, and three `PlatformSpriteSet`s (PC/Xbox/PS). `Ship Ship { get; }`.
- `void AssignShip(Ship)` — binds: subscribes to `Unit.ComponentData.ResourceTankInstalled/Removed`, spawns a `ResourceBar` per non-shared `ResourceTank`, assigns the ability slots panel/minions/log, and chooses the platform sprite set from `ship.shipInput`.
- `LateUpdate` keeps resource bars active/sized and swaps the platform sprite set when the input device changes.
- Resource bars are keyed by `Resource` in a dictionary; `OnTankInstalled`/`OnTankRemoved` add/remove and reorder by `Resource.orderInHud`.

### ShipMenuToggler
`MonoBehaviour`. The in-game tabbed ship menu (this is the de-facto in-run menu manager, though it is **not** named `*Manager`). Serialized: `Canvas canvas`/`header`/`minimapCanvas`, `ShipMenuTab[] tabs`, `ShipMenuTabButton[] tabButtons`, input action references (`open`, `previousTab`, `nextTab`, `close`, `back`), `GameController`, `TimeManager`, `TMP_Text networkLevelText`, header animator, tab-hint images, KBM/Xbox `PlatformSpriteSet`s. Tab indices: `tabIndexMap=0`, `tabIndexGrid=1`, `tabIndexConsumables=2`.
- On `GameController.GameStarted`, subscribes to every ship's `PlayerInput.onActionTriggered`.
- `OnActionTriggered` routes input: when closed, the `open` action calls `Open(playerInput, tabIndexMap, ship.NearStation)`; when open, handles close/back/next/prev and forwards to `activeTab.OnInputActionPerformed`. Only the `playerInputInControl` is honored while open.
- `void OpenShop(Ship, Station)` — public entry to open on the module-grid tab (called when interacting with a station/shop).
- `Open(...)` pauses time, shows cursor (KBM only), enables the menu canvas, switches all players to `"MapControl"`, disables ship control, updates tab-button labels (station vs normal) and platform hints.
- `Close()` reverses it and switches players back to `"ShipControl"`.
- `ShowTab(int)` closes the old `ShipMenuTab` and `Open(ship, station)`s the new one.
- Also drives "showcase" sequences (`RevealScannedArea`, instrument discovery) that temporarily hide the HUD and disable input.

### ShipMenuTab (base) — ModuleGridScreen, ConsumablesScreen
`ShipMenuTab : MonoBehaviour` is the base for in-game menu pages. Key API:
- `protected Ship Ship { get; }`, `protected Station Station { get; }`.
- `void Open(Ship, Station)` → sets fields, activates, calls `virtual OnOpened()`.
- `void Close()` → deactivates, calls `virtual OnClosed()`.
- `virtual void OnInputActionPerformed(InputAction)`, `virtual bool OnBackPressed()` (return true to consume the back press and stay open).

**ModuleGridScreen** (`: ShipMenuTab, IPointerDownHandler, IPointerUpHandler`) — the module-editing screen. Owns `ModuleGridInput input`, `ModuleGridWidget gridWidget`, `VaultGridWidget vaultGridWidget`, `ShopWidget shopWidget`, `DraggedItemWidget draggedItemWidget`, `HoveredModuleInfo hoveredModuleInfo`. Subscribes to `input` events (`SelectionChanged`, `ModuleMoveStarted/Finished`, `ShopOpenClicked`, `VaultOpenClicked`, `ModuleUnequip`). Implements drag/drop between grid, vault and shop (`OnModuleDropped`, `MoveToGrid`, `MoveFromVaultToGrid`, `BuyFromShop`, `MoveToVault`, `Unequip`). Public flag `bool EditEnabledOutsideStation { get; set; }` (toggled by `DebugMenu`). Registered as a service (`ServiceLocator.Get<ModuleGridScreen>()`).

**ConsumablesScreen** (`: ShipMenuTab`) — consumable shop + wheel preview. Binds to `Vault`; `OnOpened()` refreshes the `ConsumablesShopWidget` (when near a station) and rebuilds the radial `ConsumableWheelItem` preview; hides other ships' HUDs; mirrors the panel to the correct side for player two.

### OptionsScreen and the tab system
`OptionsScreen : MonoBehaviour` drives an array `OptionsTab[] tabs = { gameplayOptionsTab, videoOptionsTab, audioOptionsTab }`. It owns its own `InputActionReference`s (`verticalNavigation`, `changeOption`, `nextTab`, `previousTab`, `apply`, `close`) and translates them into `tabs[currentTab].HandleUp/Down/Left/Right`, `ShowTab`, `ApplyPendingChanges`, `Close`. `Open()`/`Close()` drive an `Animator` bool `Visible`. Gamepad-vs-keyboard hint containers swap each frame via `LastUsedDeviceTracker`.

`OptionsTab` (abstract): serialized `OptionsMenuitemBase[] items`; `Show()`/`Hide()` coroutines run the tab's `AnimatedScreen`; `HandleUp/Down` move selection, `HandleLeft/Right` forward to the selected item; `abstract OnOpened/OnClosed`, `virtual ApplyPendingChanges`. Concrete tabs bind to `SettingsManager`/`OptionsData`:
- `AudioOptionTab` — master/SFX/music volume sliders.
- `VideoOptionsTab` — screen mode + vsync buttons; applies on `ApplyPendingChanges`.
- `GameplayOptionsTab` — camera sway, aim assist, rumble.

`OptionsMenuitemBase` items: `OptionsMenuItemButtons` (toggle-group, fires `SelectionChanged(int)`), `OptionsMenuItemList` (cycles string options — used for resolutions from `Screen.resolutions`), `OptionsMenuItemSlider` (exposes `Value` 0..1 and `ValueChanged`). All share animator states `Selected`/`Dirty`/`Active` and `SetGamepadHintsVisible`.

### DebugMenu / SecondaryDebugMenu

**Key binding (verified from the `level3`/GameScene asset):** opened with **Ctrl + Alt + D**
(`showDebugInputAction`, a `TwoModifiers` composite: modifier1=`<Keyboard>/ctrl`,
modifier2=`<Keyboard>/alt`, button=`<Keyboard>/d`), closed with **Esc**
(`hideDebugInputAction`). `spawnEyeInputAction` is **J**. The `DebugMenu` GameObject is
present in the shipped playtest GameScene with **no `isDebugBuild`/development-build gating**.

> **Gotcha:** Unity's `TwoModifiers` composite requires the modifiers to be actuated **before**
> the button. If the player is holding **D** to fly, the composite never sees a fresh D press,
> so the combo silently fails. Release movement keys, hold **Ctrl+Alt**, *then* tap **D**.
> If it still won't open, the menu object may be inactive in a given build — a mod can wake it:
> `Resources.FindObjectsOfTypeAll<DebugMenu>().FirstOrDefault()?.gameObject.SetActive(true)`,
> then call its public toggles directly (no key needed).

`DebugMenu : MonoBehaviour` — large developer cheat panel gated by `showDebugInputAction`/`hideDebugInputAction`. Holds a serialized `UIScreen screen` plus many `PunkButton`s. `Update()` opens via `screen.Open()` (and slows time, disables ship control). Dozens of public methods are wired to buttons: `ToggleInvincibility`, `ToggleNoclip`, `ToggleInfiniteResource`, `ToggleFreeShop`, `AddMoney`, `DiscoverMap`, `TeleportToBoss`, `ShowModulePickupList`/`ShowPoiList`/`ShowEnemyList`/`ShowIngredientList`, `QuickSave`/`QuickLoad`, `ToggleHud`, `AddAllModuleToVault`, etc. List popups are rendered by `SecondaryDebugMenu.Display<T>(List<MenuItemData<T>>)`, a generic scrollable list (each item has `label`, `icon`, `iconColor`, `data`, `Clicked`). This is the cleanest existing example of a data-driven custom menu to copy for a mod.

### ConsumableWheel
`MonoBehaviour`. Radial item selector opened by `ShipControlActionMap.OpenItemWheel`. Builds one `ConsumableWheelItem` per `Vault.ConsumablesCount`, positioned by angle. While open it reads `activeShipInput.ItemWheelActionMap.Selection`, finds the nearest item by angle (with dead-zone handling for gamepad vs mouse), and on close uses the hovered `Consumable` (`consumable.Use(ship)` + `Vault.Remove`). Pauses via a `TimeManager.TimeScaleModifier`. Fires `event Action Opened`.

### Prompt vs Popup
Two confirmation dialogs with the same shape (`Open(positiveCb, negativeCb=null)`, positive/negative button callbacks, negative defaults to `Close()`), but different tech:
- `Prompt` — classic uGUI (`Canvas` + TMP + `AnimatedScreen` + `DefaultSelector`). Used by `MainMenu` (exit/new-run) and `PauseScreen` (restart). This is the one to reuse for in-engine prompts.
- `Popup` — **UI Toolkit** (`VisualElement`/`Label`/`Button`), created via `Popup.Create(VisualTreeAsset, parent, title)`. Self-contained, removed from the hierarchy on close.

### Input maps (ShipActionMap family)
`ShipActionMap` (abstract) wraps a named `InputActionMap` found on the ship's `PlayerInput.actions`. `Enable()`/`Disable()` toggle the map and call `OnEnable`/`OnDisable`. Subclasses:
- `UIActionMap` ("UI") — exposes `event Action CancelActivated`.
- `ItemWheelActionMap` ("ConsumableWheel") — `Vector2 Selection`, `event Action<ShipInput> CloseItemWheel`.
- `InstrumentMenuActionMap` ("InstrumentMenu") — `Action<Vector2> Navigate`, `Action Select`, `Action Cancel` (plain delegates, not C# events).
- (Related, gameplay side: `ShipControlActionMap` exposes `UseActivated`, `PausePerformed`, `OpenItemWheel`.)

## Modding Notes

**General hook strategy.** There is no single "show screen" choke point, so patch per-controller:
- `UIScreen.Open()` / `UIScreen.Close()` — the broadest hook. A Harmony postfix on `Open` fires whenever *any* `UIScreen`-based menu opens; read the instance's serialized fields (via reflection or `Traverse`) to identify which screen, then inject/parent your own canvas.
- `InGameHud.SetHudVisible(bool,bool)` — postfix to also toggle your custom HUD overlay in lockstep with the game HUD.
- `ShipHud.AssignShip(Ship)` — postfix to attach per-ship custom HUD widgets once a ship is bound.
- `ShipMenuToggler.Open` / `ShowTab` / `Close` — hook to add a custom tab or react to the in-game menu. To add a real tab you'd need to extend the serialized `tabs`/`tabButtons` arrays (reflection) with a `ShipMenuTab` subclass instance; simpler is to overlay your own canvas toggled by these methods.
- `OptionsScreen.ShowTab` / `OptionsTab.Show` — hook to inject extra settings UI.
- `GameController.GameStarted` / `GameOver` / `GameWon` and `GameController.LevelGenerated` are static `Action`s — the same events the HUD subscribes to. Subscribing to them from a `BepInEx` plugin is the easiest way to bind your own UI to the run lifecycle without patching.

**Adding a custom UI panel.**
1. Create a canvas (instantiate a prefab from your `AssetBundle`, or build one at runtime). Parent it under an existing UI canvas found via `GameObject.Find`/`FindObjectOfType<InGameHud>()`.
2. Reuse `UIScreen` by adding the component and calling `Open()`/`Close()`, OR drive your own canvas and just listen to the lifecycle events above. `UIScreen` will also handle action-map switching and time-pause for you if you set its serialized fields.
3. For gamepad focus, add a `DefaultSelector` (it self-registers with `UiManager`) and set its default selection so the right control is highlighted on gamepad.
4. For device-adaptive button glyphs, mirror `AdaptiveInputHint`/`ButtonHint`: read `ServiceLocator.Get<LastUsedDeviceTracker>().GamepadLastUsed`.

**Adding a custom debug/cheat menu.** The path of least resistance is to copy the `SecondaryDebugMenu` pattern: a prefab list-item + `Display<T>(List<MenuItemData<T>>)`. Or instantiate your own `PunkButton`s (`OnClick` exposes the `Button.onClick` event; `SetToggled`/`SetInteractable` for state). `DebugMenu` itself shows how to wire dozens of buttons to gameplay services obtained from `ServiceLocator`.

**Where to inject a mod menu.** Best targets: (a) a Harmony postfix on `DebugMenu.Awake`/`OnEnable` to append your buttons to its `loadoutButtonsParent`-style containers; (b) the `PauseScreen` canvas (postfix `PauseScreen.Open`) to add a "Mods" button; (c) a standalone always-on canvas you instantiate on `GameController.GameStarted`. Bind a fresh `InputAction` (as `DebugMenu`/`MainMenu` do with their own `InputAction` fields) to toggle it, so you don't have to extend the game's `.inputactions` asset.

**Gotchas.**
- Many fields are `[SerializeField] private`; to read/replace references at runtime use reflection (`AccessTools.Field`) — the public surface is mostly methods, not data.
- Time is paused through `TimeManager` (a service) with the menu instance as the owner key; if you pause/unpause yourself, use a unique owner object and `RemoveAllModifiers(owner)` to avoid stranding the game in slow-mo.
- Cursor visibility is reference-counted via `CursorController.ShowCursor(owner)`/`HideCursor(owner)` — always pair them with the same owner.
- Action-map switching in `UIScreen.Open` affects **all** `PlayerInput`s in the scene and stores only the last one's previous map; be careful patching around it in co-op.
- The serialized field name `inputManuActionMapName` (sic) and method `UpdateHoveredModuleIndfo`/`infiniteReousrce` reflect decompiler/original typos — match them exactly when reflecting.
