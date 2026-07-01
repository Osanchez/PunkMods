# Audio, Camera, Rendering & FX

> Part of the PUNK modding docs. Source: decompiled Punk.Main.dll (Unity 6000.3.4f1, Mono).

## Overview

This document covers three subsystems of PUNK that mods most often want to touch for cosmetic or quality-of-life changes:

- **Audio & music** — A pooled SFX system (`AudioManager`) driven by a GUID-keyed `AudioDatabase`. SFX are defined as `Sfx` data objects with weighted clip distributions; music is handled by a separate `MusicManager` that fades, beat-tracks and crossfades `MusicTrack`s. Higher-level controllers (`InGameMusicController`, `AmbientSoundManager`) decide *which* track/ambience plays based on combat intensity and biome. Volume flows through a single `AudioMixer` with exposed parameters `MasterVolume`, `MusicVolume`, `EffectsVolume`. There is also a small WAV/BWF metadata reader (`MordiAudio.Wave`) used to read cue points (Buildup/Drop/Outro) out of music files.

- **Camera** — The game uses the third-party **ProCamera2D** asset (`Com.LuisPedroFonseca.ProCamera2D`) as the actual camera driver. PUNK adds its own *camera targets* (`CameraTargetBase` subclasses + `POICameraTarget`) that register virtual follow points with ProCamera2D, an `EnemyTrackingCamera` turret-style tracker, a debug `FreeMoveCamera` fast-travel tool, and shake helpers (`ShipCameraShaker`, `ShakeOnStart`, `ObjectShaker`) that mostly forward to `ProCamera2DShake.Instance`. Zoom is implemented as movement along the camera's local Z (perspective camera), via `CameraExtentions`.

- **URP rendering & FX** — Custom **ScriptableRendererFeature / ScriptableRenderPass** classes implement a screen-space fog system: `FogRendererFeature` enqueues `FogMaskRenderPass` (renders Fog-layer geometry into `_FogMaskTexture`), `BlurFogMaskPass` (two-pass separable blur) and `RenderFogPass` (final blit). `FogManager` is the gameplay/simulation side: it runs a Burst job to spread fog across the level grid and feeds compute buffers (`_FogBuffer`, `_FogTypes`) and globals to the fog shaders. Lighting uses URP `Light2D` (`LightShapeBuilder`, `StationLightSource`, `BlinkingLight`, `LightSensor`). Outlines are generated at level-gen time (`OutlineFinder`/`OutlineGenerator`/`Outline`). Plus a set of small particle-driver behaviours and two tilemap renderers.

> Many shader/material parameters are global (`Shader.SetGlobalBuffer`/`SetGlobalVector`/`SetGlobalInt`), so they can be overridden at runtime from a mod without touching the originating component.

---

## Class Index

| Class | Area | Kind | Purpose |
|---|---|---|---|
| `AudioManager` | Audio | MonoBehaviour, `IGameService` | Pooled positional SFX playback + mixer volume control |
| `AudioDatabase` | Audio | `SerializedScriptableObject` | Lists of `MusicTrack` and `Sfx` (the master sound registry) |
| `AudioInstaller` | Audio | MonoBehaviour, `IServiceInstaller` | Instantiates `AudioManager`+`MusicManager` as DontDestroyOnLoad services |
| `Sfx` | Audio | `[Serializable]`, `IAudioDatabaseItem` | One sound effect definition (clips, volume, 3D, looping…) |
| `SfxSettings` | Audio | `[Serializable]` | Lightweight inline clip+volume (no GUID) |
| `SfxPlayer` | Audio | MonoBehaviour | Component that plays a named SFX on Start/Enable, follows transform |
| `AudioClipDistribution` / `AudioClipDistributionItem` | Audio | `[Serializable]` | Weighted random `AudioClip` picker |
| `AudioSourceInfo` | Audio | MonoBehaviour | Debug UI widget for one playing music source |
| `IAudioDatabaseItem` | Audio | interface | `Guid`/`Name` contract for `Sfx` and `MusicTrack` |
| `CameraAudioListenerPosition` | Audio | MonoBehaviour | Pins `AudioListener` to camera XY at fixed Z |
| `MusicManager` | Audio | MonoBehaviour | Pooled music playback, fade/beat/transition/crossfade loop |
| `MusicTrack` | Audio | `[Serializable]`, `IAudioDatabaseItem` | One music track (clip, bpm, loop, cue metadata) |
| `MusicTrackActivator` | Audio | MonoBehaviour | Plays a named track on Start; stops on disable |
| `MusicTester` | Audio | MonoBehaviour | Debug harness for playing ambient/drop tracks |
| `InGameMusicController` | Audio | MonoBehaviour | Intensity-driven music state machine (Foley/Ambient/Drop/Boss) |
| `AmbientSoundManager` | Audio | MonoBehaviour | Crossfades biome ambience based on dominant on-screen biome |
| `ShipEngineSound` | Audio | MonoBehaviour | Drives engine loop/start/stop/hover `AudioSource`s |
| `ButtonSounds` | Audio | MonoBehaviour, `ISelectHandler` | UI click/select SFX |
| `AudioOptionTab` | Audio | `OptionsTab` | Options-menu volume sliders |
| `MordiAudio.Wave.Reader` | Audio | static | Reads WAV/BWF metadata + cue points |
| `MordiAudio.Wave.Metadata` | Audio | `[Serializable]` | Parsed WAV header/cue data |
| `MordiAudio.Wave.Cue` | Audio | `[Serializable]` struct | One named cue marker |
| `FreeMoveCamera` | Camera | MonoBehaviour | Debug free-fly camera → fast-travel on exit |
| `EnemyTrackingCamera` | Camera | MonoBehaviour | Turret that rotates to face nearest visible unit |
| `CameraTargetBase` | Camera | abstract MonoBehaviour | Base for input-driven ProCamera2D follow targets |
| `GamepadCameraTarget` / `MouseCameraTarget` / `VirtualJoyCameraTarget` | Camera | `CameraTargetBase` | Concrete aim-offset camera targets per input device |
| `POICameraTarget` | Camera | MonoBehaviour | Adds/removes a point-of-interest camera target by proximity |
| `ShipCameraShaker` | Camera | MonoBehaviour | Triggers ProCamera2D shakes on shoot/damage/death |
| `ShakeOnStart` | Camera | MonoBehaviour | One-shot ProCamera2D shake if on-screen at Start |
| `ObjectShaker` | Camera/FX | MonoBehaviour | Local additive positional shake (no camera) |
| `OrthoSizeFromFiewOfView` | Camera | MonoBehaviour | Matches an ortho camera's size to a perspective camera |
| `ResizeRenderTextureToScreenSize` | Camera/Render | MonoBehaviour | Allocates a screen-sized RT for a UIToolkit panel |
| `CameraExtentions` | Camera | static | `Zoom()` extension (DOTween Z move) |
| `FogManager` | Render | MonoBehaviour | Fog simulation (Burst job) + fog shader buffers |
| `FogSource` | Render | MonoBehaviour | Emits fog into `FogManager` each tick |
| `FogRendererFeature` | Render | `ScriptableRendererFeature` | URP feature wiring the fog passes |
| `FogMaskRenderPass` | Render | `ScriptableRenderPass` | Renders Fog-layer into `_FogMaskTexture` |
| `BlurFogMaskPass` | Render | `ScriptableRenderPass` | Separable blur of the fog mask |
| `RenderFogPass` | Render | `ScriptableRenderPass` | Final fog composite blit |
| `CustomFrameData` | Render | `ContextItem` | Render-graph frame data carrying `fogMaskTexture` |
| `RenderingHelper` | Render | static | Reflection helper to fetch a renderer feature from URP asset |
| `Outline` | Render | `IDisposable` | Native edge list for level outlines |
| `OutlineFinder` | Render | class | Marching-square outline path tracer |
| `OutlineGenerator` | Render | class | Builds level edge graph at gen time |
| `LightSource` | FX/Light | MonoBehaviour | Plain intensity carrier for `LightSensor` |
| `LightSensor` | FX/Light | MonoBehaviour | Accumulates light from overlapping `LightSource`s |
| `LightBasedAnimation` | FX/Light | MonoBehaviour | Sets an Animator bool from `LightSensor.IsInLight` |
| `LightShapeBuilder` | FX/Light | MonoBehaviour | Builds `Light2D` shape paths from level outlines |
| `BlinkingLight` | FX/Light | MonoBehaviour | Toggles a `Light2D` on/off timer |
| `StationLightManager` | FX/Light | MonoBehaviour | Spawns `StationLightSource` per upgraded station |
| `StationLightSource` | FX/Light | MonoBehaviour | Animated circle→polygon `Light2D` reveal |
| `CustomTilemapRenderer` | Render | MonoBehaviour | GPU-instanced tile mesh rendering |
| `UnityTilemapRenderer` | Render | MonoBehaviour | Tracks on-screen cells, fires visibility events |
| `TextureUpdater` | Render | `IDisposable` | Partial-rect `Texture2D` blits |
| Particle drivers | FX | MonoBehaviour | `DashParticle`, `EngineParticle`, `HoverParticles`, `ImpactParticle`, `ParticleLifetime`, `ShipGroundParticle`, `StatusEffectParticleManager` |

---

## Classes

### AUDIO

### AudioManager
- **Kind:** `MonoBehaviour`, implements `IGameService`. Registered as a service (see `AudioInstaller`); access via `ServiceLocator.Get<AudioManager>()`.
- **Purpose:** Central SFX playback. Pools `AudioSource`s, plays clips drawn from an `Sfx`, tracks playing handles, follows transforms, and exposes mixer volume setters.
- **Key serialized fields:** `effectAudiosourcePrefab` (`AudioSource`), `audioListener` (`AudioListener`), `audioDatabase` (`AudioDatabase`), `audioMixer` (`AudioMixer`).
- **Internal state:** `ObjectPool<AudioSource> _pool`; `Dictionary<int,PlayingAudio> playingAudios`; `Dictionary<Sfx,float> sfxLastPlayedDictionary` (enforces `repeatMinDelay`). Nested struct **`PlayingAudio`** { `Sfx sfx; AudioSource audioSource; float cleanupTime; Transform transformToFollow;` }.
- **Static API (the main public entry points):**
  - `static int PlaySfx(string sfx)` — play by GUID at listener position.
  - `static int PlaySfx(string sfx, Vector2 position)` — play at a world position.
  - `static int PlaySfx(string sfx, Transform transformToFollow)` — play and follow a transform.
  - `static void Stop(int handle)` — stop a handle (uses `TryGet`, safe if no manager).
  - All return/accept an `int` *handle* (`-1` = failed/none).
- **Instance methods:** `void UpdatePosition(int handle, Vector2 position)`; `void ApplySettings(OptionsData.AudioOptions)`; `void SetMasterVolume(float)`, `SetMusicVolume(float)`, `SetEffectsVolume(float)` (multiply a captured default; converted to dB via `20*log10`, clamped −80..20). Private `int PlaySfx(Sfx, Vector2, Transform)` does the real work: honors `sfx.cancelPrevious`, `repeatMinDelay`, sets `volume/priority/outputAudioMixerGroup/loop/spatialBlend`, draws a clip via `sfx.GetClip()`.
- **Properties:** `Vector2 ListenerPosition`; `AudioListener AudioListener`.
- **Relationships:** reads `AudioDatabase.sfxs`; mixer params `"MasterVolume"`, `"MusicVolume"`, `"EffectsVolume"`.

### AudioDatabase
- **Kind:** `SerializedScriptableObject` (Odin). `[CreateAssetMenu(menuName = "Punk/Audio/Audio database")]`.
- **Fields:** `List<MusicTrack> musicTracks`; `List<Sfx> sfxs`. This is the lookup table both `AudioManager` (by `Sfx.guid`) and the music controllers (by `MusicTrack.guid`) use.

### AudioInstaller
- **Kind:** `MonoBehaviour`, `IServiceInstaller`.
- **`InstallServices(ServiceContainer)`** instantiates `audioManagerPrefab`, marks `DontDestroyOnLoad`, then installs both the `AudioManager` and the `MusicManager` sibling component into the container.

### Sfx
- **Kind:** `[Serializable]` class, implements `IAudioDatabaseItem`. Generates a `guid` in its constructor.
- **Fields:** `string guid, name`; `AudioClipDistribution audioClips`; `float volume` (0..1); `int priority` (0..256, default 128); `bool is3d` (→ `spatialBlend`); `AudioMixerGroup mixerGroup`; `bool looping`; `float repeatMinDelay` (0.01); `bool cancelPrevious`; `bool ignoreValidation`.
- **Members:** `bool HasSound`; `AudioClip GetClip()` (weighted draw); `bool AllSoundsNotNull()`; `void Test()` (editor play-mode test → `AudioManager.PlaySfx(guid)`).

### SfxSettings
- **Kind:** `[Serializable]`. Inline alternative to `Sfx` with no GUID: `AudioClipDistribution audioClips`, `float volume`, `bool HasSound`, `AudioClip GetClip()`.

### SfxPlayer
- **Kind:** `MonoBehaviour`. Component that plays a named SFX automatically.
- **Fields:** `string sfx`; `enum TriggerEvent { Start, Enable, None }` `triggerEvent`; `float delay`; `bool updatePosition` (default true — keeps the source on this transform); `bool stopOnDisable`.
- **Methods:** `void Play()` / `void Play(string sfx)` → async `PlayAsync` (UniTask delay then `AudioManager.PlaySfx`). Stores the handle for position updates and `OnDisable` stop.

### AudioClipDistribution / AudioClipDistributionItem
- `AudioClipDistribution : Distribution<AudioClip, AudioClipDistributionItem>` and `AudioClipDistributionItem : DistributionItem<AudioClip>` (namespace `WeightedDistribution`). A weighted random clip set; `.Draw()` picks one, `.Items` is the list.

### AudioSourceInfo
- **Kind:** `MonoBehaviour` debug widget. Holds a `MusicManager.PlayedMusic`, updates `timeText/timeSlider/volumeSlider` each frame; `event Action<AudioSourceInfo> StopClicked`; `SetAudioSource(...)`, `Stop()`.

### IAudioDatabaseItem
- Interface: `string Guid { get; }`, `string Name { get; }`. Implemented by `Sfx` and `MusicTrack`.

### CameraAudioListenerPosition
- **Kind:** `MonoBehaviour`. In `LateUpdate` moves `audioManager.AudioListener.transform` to `(transform.x, transform.y, zPosition)`. Field `float zPosition`. Keeps the 2D listener at a fixed depth.

### MusicManager
- **Kind:** `MonoBehaviour` (installed as a service alongside `AudioManager`). Pools `AudioSource`s for music.
- **Serialized fields:** `AudioMixer audioMixer`, `AudioSource _audioSourcePrefab`, `float fadeOutDuration`.
- **Nested class `PlayedMusic`:** wraps a `MusicTrack`+`AudioSource`. Computes beats from `audioSource.time` and `musicTrack.bpm`; `int CurrentBeat`; `bool CanBeRecycled`; events `Action<PlayedMusic,int> Beat` and `Action<PlayedMusic> TransitionPoint` (fires every 8 beats). Methods `Play(endVolume, fadeInDuration)`, `Stop(fadeOutDuration)`, `FadeIn/FadeOut` (DOTween `DOFade`, `SetUpdate(isIndependentUpdate:true)`). Uses `AudioSettings.dspTime` + `SetScheduledEndTime` for sample-accurate stop. `endDspTimeWithoutTrailing` accounts for `trailingBeatCount`.
- **Public API:** `PlayedMusic Play(MusicTrack, float fadeInDuration = 0.001f)`; `void Stop(MusicTrack, bool useFadeOut = true)`; `bool IsPlaying(MusicTrack)`; `void StopAll()`; `event Action<PlayedMusic> MusicRecycled`.
- **Update loop:** recycles finished sources; for looping tracks restarts (with crossfade if `loopWithCrossfade`). Mixer param `"MusicVolume"` is toggled to −80 dB via private `OnMusicEnabledSettingChanged(bool)`.

### MusicTrack
- **Kind:** `[Serializable]`, `IAudioDatabaseItem`, generates `guid`.
- **Fields:** `string guid, name`; `AudioClip audioClip`; `AudioClip sweep`; `int sweepLengthInBars, exitPointDistanceInBars`; `float volume` (0..1); `int bpm` (128); `int trailingBeatCount`; `bool loop`; `bool loopWithCrossfade`; `Metadata metadata` (WAV cue data).
- **Members:** `float Length`; cue helpers `TryGetBuildupPosition/TryGetDropPosition/TryGetOutroPosition(out uint)` which scan `metadata.cues` by name ("Buildup"/"Drop"/"Outro").

### MusicTrackActivator
- **Kind:** `MonoBehaviour`. Looks up a `MusicTrack` in an injected `AudioDatabase` by `musicTrack` GUID; on `Start` plays it if not already playing; `OnDisable` stops it if `stopOnDisable`.

### MusicTester
- **Kind:** `MonoBehaviour` debug harness. Plays `ambientMusicTrack`/`dropMusicTrack` GUIDs, spawns `AudioSourceInfo` widgets, cleans up on `MusicRecycled`.

### InGameMusicController
- **Kind:** `MonoBehaviour`. The runtime music director.
- **`enum State { Foley, Ambient, Drop, Boss }`** (`State CurrentState`).
- **Fields (serialized tuning):** `AudioDatabase audioDatabase`; track GUIDs `ambientTrack`, `List<string> combatTracks`, `bossTrack`; intensity tuning: `intensityPerKill`, `intensityPerStationUnlock`, `intensityDrainSpeed`, `ambientThreshold`, `dropThreshold`, `extraIntensityWhenEnteringAmbient`, `maxIntensity`, `intensityAfterDrop`. Public `float intensity`.
- **Logic:** subscribes to `GameController.GameStarted/GameOver`, `BossStateManager.EnteredBossState/ExitedBossState`, per-ship `Unit.KilledAnotherUnit`, and station `UpgradeInstalled`. Kills/unlocks raise `intensity`; thresholds promote Foley→Ambient→Drop; boss state overrides. `SetState` plays/stops the appropriate `MusicManager` tracks. Public debug `IncreaseIntensity()` (+100) and `ResetIntensity()`.

### AmbientSoundManager
- **Kind:** `MonoBehaviour`. Crossfades two `AudioSource[] audioSources` (ping-pong via `flip`) for biome ambience.
- **Fields:** `float fadeDuration, volume, biomeSampleInterval`; `int biomeSampleRadius`.
- **Logic:** every `biomeSampleInterval`, samples a square region around the camera (`GetDominantBiome`) from `Level`/`IRegistry<Biom,byte>`; if the dominant biome's `ambientClip` changed, `FadeTo` it (DOTween `DOFade`). Also loops the current clip seamlessly near its end.

### ShipEngineSound
- **Kind:** `MonoBehaviour`. Reads `ShipMovement` (`flyDirection`, `IsBoosted`, `isHovering`) and plays/stops four `AudioSource`s: `engineLoopedAudioSource`, `engineStartAudioSource` (with `minStartSoundDelay` gate), `engineStopAudioSource`, `hoverAudioSource`.

### ButtonSounds
- **Kind:** `MonoBehaviour`, `ISelectHandler`. Plays `clickSfx` on `Button.onClick` and `selectSfx` on `OnSelect`. (Note: the decompiled `OnDisable` re-adds the listener — likely an original bug, harmless to mods.) `OnValidate` auto-fills `button` and `audioDatabase` via `AssetAutoFill`.

### AudioOptionTab
- **Kind:** `OptionsTab`. Binds master/sfx/music `OptionsMenuItemSlider`s to `OptionsData.AudioOptions`, saving through `settingsManager.Apply(audioOptions)` (which ultimately calls `AudioManager.ApplySettings`).

### MordiAudio.Wave (WAV/BWF metadata)
- **`Reader`** (static): `static Metadata GetMetadata(string path)` parses RIFF/FMT/DATA/CUE/LIST(adtl/labl) chunks of `.wav`/`.bwf` files into a `Metadata`, including named cue markers. Helper byte readers (`GetUInt`, `GetInt16`, `GetString`, …).
- **`Metadata`** (`[Serializable]`): WAV header fields (`sampleRate`, `channelCount`, `bitRate`, `sampleCount`, `duration`, …) plus `Cue[] cues`; `GetAllMetadataAsString()`.
- **`Cue`** (`[Serializable]` struct): `uint ID; string name; uint position; uint dataChunkID;`. Consumed by `MusicTrack.TryGetXxxPosition`.

---

### CAMERA

> The actual camera controller is the **ProCamera2D** asset (`Com.LuisPedroFonseca.ProCamera2D.ProCamera2D`, accessed via `ProCamera2D.Instance` / `FindObjectOfType`). PUNK's types are *targets* and *shake triggers* layered on top. Shakes go through `ProCamera2DShake.Instance.Shake(ShakePreset)`.

### CameraTargetBase
- **Kind:** `abstract MonoBehaviour`, `[RequireComponent(typeof(ShipInput))]`. Base class for input-driven follow targets.
- **Fields:** `ShipInput shipInput`; `float maxDistance, inertia, targetInfluenceH, targetInfluenceV, duration`; `Vector2 targetOffset`.
- **Behaviour:** on enable creates a child `"CameraTarget"` transform and calls `ProCamera2DInstance.AddCameraTarget(target, influenceH, influenceV, duration, offset)`; removes it on disable. `Update` smooths a local offset (`TargetLocalPosition`, clamped to `maxDistance`) with `inertia`, and blends toward center by `cameraLockAmount`. Enables/disables itself based on `IsUsedForInput(shipInput)` when the control scheme changes.
- **Public:** `LockCamera(float duration)` / `UnlockCamera(float duration)` (DOTween the lock amount). `protected abstract bool IsUsedForInput(ShipInput)`, `protected virtual void Update()`.
- **Static cache:** `ProCamera2D _proCamera2DInstance` (shared lookup).

### GamepadCameraTarget / MouseCameraTarget / VirtualJoyCameraTarget
- All `: CameraTargetBase`, each overrides `Update` to set `TargetLocalPosition` and `IsUsedForInput`:
  - **`GamepadCameraTarget`** — aim = `ShipInput.AimDirection` eased by `Easing.EaseFunction easing` × `MaxDistance`; active when `shipInput.UsesGamepad`.
  - **`MouseCameraTarget`** — offset from screen-center mouse position scaled by `screenHeightRatio` (0..1); active when **not** gamepad. Caches `Camera.main`.
  - **`VirtualJoyCameraTarget`** — uses a `ShipVirtualJoyInput virtualJoyInput`; always active (`IsUsedForInput` returns true). For touch builds.

### POICameraTarget
- **Kind:** `MonoBehaviour` (not a `CameraTargetBase`). When the average alive-ship position comes within `activationDistance` of this object, it `AddCameraTarget(transform, …)`; when it leaves, `RemoveCameraTarget(transform, duration)`. Fields mirror `CameraTargetBase` influence/offset/duration. Uses `ProCamera2D.Instance`.

### EnemyTrackingCamera
- **Kind:** `MonoBehaviour`. A turret/security-camera that rotates `rotatingPart` to face a `Unit Target` within `VisionAngle`. `RefreshVisibleUnits(IEnumerable<Unit>)` filters by angle and picks a target; drives an `Animator` bool `"Active"`. Not the main game camera.

### FreeMoveCamera
- **Kind:** `MonoBehaviour` debug/cinematic tool. On enable: disables `ProCamera2D`, switches every ship's input map to `"FreeMoveCamera"`, zeroes gravity, hides `objectsToDisable`. Reads `InputActionReference`s (`moveAction`, `fastMoveAction`, `cancelAction`, `zoomAction`) to fly the `Camera.main` (lerped) and zoom via Z. On disable restores everything, restores camera Z, re-enables ProCamera2D, and calls `fastTravelManager.TravelTo(targetPosition)`. Useful reference for a "free camera / cinematic" mod.

### ShipCameraShaker
- **Kind:** `MonoBehaviour`. Subscribes to shooter `OnShoot` and `DamagableResource.onDamage/onDeath`, calling `ProCamera2DShake.Instance.Shake(preset)` with `damageShakePreset`, `deathShakePreset`, or each weapon's `ShakePreset`.

### ShakeOnStart
- **Kind:** `MonoBehaviour`. On `Start`, if `transform.IsOnScreen(Camera.main, 0.2f)`, fires `ProCamera2DShake.Instance.Shake(shakePreset)`. Public field `ShakePreset shakePreset`.

### ObjectShaker
- **Kind:** `MonoBehaviour`. Local additive transform shake **independent of the camera**. `void Shake(Vector2 direction, float amplitude, float shakeDuration, float shakeFrequency)` queues a damped-cosine `ShakeAnim`; `Update` sums active shakes onto `originalPosition`.

### OrthoSizeFromFiewOfView
- **Kind:** `MonoBehaviour` (sic, "Fiew"). Each `Update`, sets `orthoCamera.orthographicSize = -referenceToMatch.z * tan(fov/2)`, keeping an orthographic camera (e.g. UI/secondary) framed identically to the perspective game camera.

### ResizeRenderTextureToScreenSize
- **Kind:** `MonoBehaviour`. In `Awake` allocates a `Screen.width × Screen.height` `RenderTexture` (ARGB32, 16-bit depth), assigns it to a UIToolkit `PanelSettings.targetTexture` and a `RawImage.texture`.

### CameraExtentions
- **Kind:** `static` extension class. `Camera.Zoom(float fromZ, float toZ, float duration)` and `Camera.Zoom(float toZ, float duration)` — DOTween `DOMoveZ`. Zoom = camera Z movement (perspective). Handy for a zoom mod.

---

### URP RENDERING — FOG

The fog effect is split into a **simulation** half (`FogManager`/`FogSource`, CPU + Burst, writes shader globals) and a **rendering** half (`FogRendererFeature` + three `ScriptableRenderPass`es using the URP RenderGraph API).

### FogManager
- **Kind:** `MonoBehaviour`. Drives fog spread and uploads shader data.
- **Serialized fields:** `CellType fogCellType`; `float updateInterval`; `byte fogThreshold, fogSpreadThreshold, startingFogLevel`; `GameObject overlay`; `bool refreshVisualsEveryFrame`; `List<FogVisual> fogVisuals`; `float changeAnimDuration`.
- **Nested types:** `struct FogUpdateJob : IJob` (`[BurstCompile]`) — spreads fog across `NativeArray<byte> fogLevels`, adds/removes fog cells; `struct FogVisual` (serialized per-fog-type colors + blink params, keyed by `CellType fogCellType`); `struct FogType` (GPU layout, `const int Stride = 72`).
- **Key methods:** `void FillInitialFogLevels()`; `void Register(FogSource)` / `void UnRegister(FogSource)`; `void AddFogLevel(Vector2Int, byte)`; `void RefreshMask()` (LateUpdate — builds an 80×45 mask around the camera and sets globals **`_FogBuffer`**, **`_FogMaskTextureOffset`**, **`_FogMaskTextureSize`**); private `RefreshFogTypes()` sets globals **`_FogTypes`** (`ComputeBuffer`) and **`_FogTypeCount`**.
- **Config gates (via `LocalConfig.data`):** `renderFogOverlay`, `enableFogUpdate`, `enableFogBufferUpdate` — these toggle fog work at runtime and are easy mod targets.
- **Relationships:** reads `Level` (`cellTypes`, `fogLevels`, `CellChanged`); subscribes to `Level.CellChanged`.

### FogSource
- **Kind:** `MonoBehaviour`. Registers itself with `FogManager` after a `delay`, emitting `byte EmissionPerTick` of fog at its grid position each tick; unregisters on disable.

### FogRendererFeature
- **Kind:** `ScriptableRendererFeature`. Public fields: `Material fogMaskMaterial`, `Material blurMaterial`, `BlurFogMaskPass.BlurSettings blurSettings`.
- **`Create()`** builds a `FogMaskRenderPass(fogMaskMaterial)` and a `BlurFogMaskPass(blurMaterial, blurSettings)`, both `requiresIntermediateTexture = true`, `renderPassEvent = BeforeRenderingTransparents`. **`AddRenderPasses`** only enqueues the mask pass for `CameraType.Game`. *(Note: as decompiled, only `fogMaskRenderPass` is enqueued here; `blurFogMaskPass`/`RenderFogPass` are constructed but not enqueued in this method.)*

### FogMaskRenderPass
- **Kind:** `ScriptableRenderPass`. Renders all renderers on the **`"Fog"`** layer (transparent queue, `ShaderTagId "SRPDefaultUnlit"`, override material = `fogMaskMaterial`) into a new `R32G32B32A32_SFloat` texture, exposed as global texture **`_FogMaskTexture`** and stored in `CustomFrameData.fogMaskTexture`. Uses the RenderGraph `RecordRenderGraph` API. Nested `PassData` holds the material + `RendererListHandle`.

### BlurFogMaskPass
- **Kind:** `ScriptableRenderPass`. Two-pass separable blur of the fog mask using `blurMaterial` passes 0/1, writing shader floats **`_HorizontalBlur`** / **`_VerticalBlur`** from the serializable **`BlurSettings`** (`horizontalBlur`, `verticalBlur`, each `[Range(0,0.4)]`). Reads `CustomFrameData.fogMaskTexture`.

### RenderFogPass
- **Kind:** `ScriptableRenderPass`. Final composite: blits the camera color through `material` (the fog material), passing the four screen-corner world positions as **`_BottomLeft/_BottomRight/_TopLeft/_TopRight`** and binding the global **`_FogMaskTexture`**. Writes back to `cameraColor`.

### CustomFrameData
- **Kind:** `ContextItem` (RenderGraph). Single field `TextureHandle fogMaskTexture`; `Reset()` nulls it. Shared hand-off between the fog passes.

### RenderingHelper
- **Kind:** `static`. `ScriptableRendererFeature GetRenderFeature<T>(this UniversalRenderPipelineAsset asset)` — reflects the private `m_RendererDataList` to find a renderer feature of type `T`. **Useful for mods** that want to grab `FogRendererFeature` at runtime to toggle/replace it.

---

### URP RENDERING — OUTLINES

### Outline
- **Kind:** `IDisposable`. Holds `NativeList<Edge> edges` where `struct Edge { float2 start, end, normal; }`. Allocated `Persistent`; created per level.

### OutlineFinder
- **Kind:** plain class. Marching-square style contour tracer. `Vector2[][] FindOutline(RectInt area, Func<Vector2Int,bool> shouldContain)` walks cell boundaries (internal `enum Di { none, up, down, left, right }`) and returns closed paths. Used by `LightShapeBuilder` and outline generation.

### OutlineGenerator
- **Kind:** plain class. `void Generate(LevelGenerationContext context)` — at level-gen time marks `edgeCells`, connects nearest edges, and fills `context.outline.edges` with `Outline.Edge`s (computing an outward `normal` by counting empty cells on each side).

---

### URP RENDERING — LIGHTING (URP `Light2D`)

### LightSource
- **Kind:** `MonoBehaviour`. Just `public float intensity;` — a marker/intensity carrier detected by `LightSensor` via 2D triggers.

### LightSensor
- **Kind:** `MonoBehaviour`. Accumulates `lightLevel` from `LightSource`s overlapping its trigger (minus `ignoredSources`). Fields: `float lightThreshold, darknessDelay`; `LightSource[] ignoredSources`; public `float lightLevel`. `bool IsInLight` flips true above threshold and resets after `darknessDelay` with no sources.

### LightBasedAnimation
- **Kind:** `MonoBehaviour`. Each `Update` sets `animator.SetBool(animParamName, lightSensor.IsInLight)`.

### LightShapeBuilder
- **Kind:** `MonoBehaviour`. Builds `Light2D` shape paths for level segments. Public `CellType lightCellType`, `Light2D lightPrefab`. `Build(Vector2Int segmentPosition, LevelSegmentComponent)` runs `OutlineFinder.FindOutline` and assigns paths into instantiated `Light2D`s. Sets the private `m_ShapePath` field via reflection (`SetFieldValue`). *(Note: the `HasLight` predicate returns `false` as decompiled.)*

### BlinkingLight
- **Kind:** `MonoBehaviour`, `[RequireComponent(typeof(Light2D))]`. Toggles `light2D.enabled` between `onDuration` and `offDuration`.

### StationLightManager
- **Kind:** `MonoBehaviour`. On `LevelGenerated`, finds all `Station.Data`, and `SpawnLight` (a `StationLightSource lightPrefab`) for stations with installed upgrades; spawns more on `UpgradeInstalled`. `DestroyLight(Station.Data)` removes one.

### StationLightSource
- **Kind:** `MonoBehaviour`. Animated reveal using two `Light2D`s (`circleLight`, `polygonLight`). `Appear(Station.Data)` sets the polygon shape path from `station.lightPolygon`, then DOTween-animates inner/outer point-light radii (with `innerCurve`/`outerCurve`, delays) before switching from the circle to the polygon light. Fields: `falloff, overlap, tweenDuration, innerTweenDelay, outerTweenDelay`, `CircleCollider2D lightCollider`.

---

### URP RENDERING — TILEMAPS / TEXTURES

### CustomTilemapRenderer
- **Kind:** `MonoBehaviour`. GPU-instanced tile rendering. On `LevelGenerated`, builds a `GraphicsBuffer` of per-tile `MeshProperties` (`Matrix4x4 mat; Color color; int tilePositionX, tilePositionY;`, `Size()==88`), binds it as `_Properties` on a `RenderParams` (layer `"CustomTile"`), and each `Update` calls `Graphics.RenderMeshPrimitives(mesh, transforms.Count)`. Releases the buffer on destroy.

### UnityTilemapRenderer
- **Kind:** `MonoBehaviour`. Tracks which level cells are on-screen (perspective frustum projected to `backgroundZPosition`) and raises `event Action<Vector2Int> CellBecameVisible` / `CellBecameInvisible`. Exposes `IEnumerable<Vector2Int> VisibleCells`, `byte GetVariant(int,int)` (per-cell random variant), `bool IsCellVisible(Vector2Int)`.

### TextureUpdater
- **Kind:** `IDisposable` helper. Partial-rect texture writes: `Apply(RectInt, Func<Vector2Int,Color>)` or `Apply(RectInt, Color32[])` — fills a scratch `changeTexture` then `Graphics.CopyTexture` into the `targetTexture` at the rect offset.

---

### FX — PARTICLE DRIVERS

Small `MonoBehaviour`s that start/stop a `ParticleSystem` based on ship/physics state. None expose much public API; they are listed for completeness and as patch targets.

| Class | Trigger / behaviour | Notable fields |
|---|---|---|
| `DashParticle` | Plays on `ShipMovement.DashStarted`, stops after `duration` | `ShipMovement ship`, `float duration` |
| `EngineParticle` | Plays while `ship.flyDirection` aligns (<60°) with `transform.up` | `ShipMovement ship` |
| `HoverParticles` | Plays while `ship.isHovering` | `ShipMovement ship` |
| `ImpactParticle` | On `OnCollisionEnter2D` (layer + velocity + `minDelay` gated) instantiates `particlePrefab` at contact | `ParticleSystem particlePrefab`, `float velocityThreshold, minDelay`, `LayerMask layerMask` |
| `ParticleLifetime` | Sprite-frame animated lifetime; destroys self after random `lifetime`, shrinks over `shrinkDuration` | `SpriteRenderer spriteRenderer`, `Sprite[] spriteFrames`, `MinMaxFloat lifetime`, `float shrinkDuration` |
| `ShipGroundParticle` | Raycasts down; plays `dustParticle` at ground hit while `engineParticle` emits | `ParticleSystem engineParticle, dustParticle`, `LayerMask layerMask`, `float maxDistance, offset` |
| `StatusEffectParticleManager` | Emits burn particles on on-screen burning cells; drives `burnSfxSource.volume` by distance falloff; `EmitForUnit(Unit.Data)` | `ParticleSystem burnEffectParticlePrefab`, `float emissionRate, emissionRateVariance`, `AudioSource burnSfxSource`, `AnimationCurve burnSfxFalloff`, `float burnSfxMaxDistance, burnSfxScaler` — gated by `LocalConfig.data.enableFireParticles` |

---

## Modding Notes

### Audio: mute / replace / add SFX
- **Master mute or volume override (Harmony):** patch `AudioManager.SetMasterVolume`, `SetMusicVolume`, `SetEffectsVolume` (or `ApplySettings`). These set mixer params `"MasterVolume"`, `"MusicVolume"`, `"EffectsVolume"`; you can also drive the mixer yourself by getting `AudioManager`'s private `audioMixer` (reflection) and calling `SetFloat`.
- **Mute / replace a specific SFX:** Harmony-prefix the private instance method `AudioManager.PlaySfx(Sfx, Vector2, Transform)` (return `-1` to suppress) or the static `AudioManager.PlaySfx(string,…)`. To remap clips, patch `Sfx.GetClip()` / `Sfx.HasSound`, or mutate `AudioDatabase.sfxs` (find your `Sfx` by `guid`/`name` and swap `audioClips`).
- **Adding sounds at runtime:** add new `Sfx` entries to the `AudioDatabase` ScriptableObject's `sfxs` list (give them a `guid`); trigger with `AudioManager.PlaySfx(guid[, position|transform])`. Loading custom `AudioClip`s from disk → assign into a new `Sfx.audioClips` (`AudioClipDistribution.Items`).
- **UI sounds:** patch `ButtonSounds.OnClicked` / `OnSelect`.
- **Disable engine/ambient/burn audio:** `ShipEngineSound.Update`, `AmbientSoundManager.FadeTo`, `StatusEffectParticleManager` (`burnSfxSource`).

### Music: custom tracks / control
- **Play/stop arbitrary music:** call `MusicManager.Play(MusicTrack)` / `Stop(MusicTrack)` / `StopAll()` / `IsPlaying(MusicTrack)`. Get the service via `ServiceLocator.Get<MusicManager>()`.
- **Replace tracks:** swap `MusicTrack.audioClip` on entries in `AudioDatabase.musicTracks` (match by `guid`/`name`), or add new `MusicTrack`s and reference their GUIDs.
- **Override the music director:** patch `InGameMusicController.SetState` (force a state) or `Update`/`OnNewKill` (intensity). `InGameMusicController.IncreaseIntensity()` / `ResetIntensity()` are public. To silence dynamic music entirely, disable the `InGameMusicController` component or prefix `SetState` to no-op.
- **Beat/transition hooks:** subscribe to `MusicManager.PlayedMusic.Beat` / `.TransitionPoint`, or `MusicManager.MusicRecycled`.
- Music mute also flows through mixer `"MusicVolume"` (`MusicManager.OnMusicEnabledSettingChanged`).

### Camera: zoom / free camera
- **Zoom:** the camera is perspective; "zoom" = move along Z. Use `CameraExtentions.Zoom(camera, toZ, duration)` or patch it. The ProCamera2D follow distance is governed by camera Z; a constant offset mod can patch `CameraTargetBase.Update` or add your own ProCamera2D target.
- **Free / cinematic camera:** `FreeMoveCamera` is a ready-made template — enabling it disables ProCamera2D, swaps input maps, and fast-travels on exit. A mod can instantiate/enable it or replicate its pattern (`proCamera2D.enabled = false` then move `Camera.main`).
- **Add a follow point:** call `ProCamera2D.Instance.AddCameraTarget(transform, influenceH, influenceV, duration, offset)` (see `POICameraTarget`/`CameraTargetBase`).
- **Disable/scale camera shake:** patch `ShipCameraShaker`'s handlers, `ShakeOnStart.Start`, or the upstream `ProCamera2DShake.Instance.Shake(...)`. Local (non-camera) object shakes are `ObjectShaker.Shake`.
- **Listener:** `CameraAudioListenerPosition.LateUpdate` controls where 3D audio is heard.

### Rendering: disable / tweak fog
- **Disable fog cheaply (no Harmony):** these `LocalConfig.data` flags gate `FogManager`: `renderFogOverlay`, `enableFogUpdate`, `enableFogBufferUpdate`. Setting them false stops fog simulation/upload. `enableFireParticles` gates burn FX.
- **Disable the fog *render*:** remove/disable the `FogRendererFeature` on the URP renderer (grab it via `RenderingHelper.GetRenderFeature<FogRendererFeature>(urpAsset)` and set `SetActive(false)` / null its material), or Harmony-prefix `FogMaskRenderPass.RecordRenderGraph` / `RenderFogPass.RecordRenderGraph` to skip.
- **Tweak fog look:** override shader globals at runtime — `Shader.SetGlobalBuffer("_FogTypes", …)`, `_FogTypeCount`, `_FogBuffer`, `_FogMaskTextureOffset/Size`; or patch `FogManager.RefreshFogTypes` / edit `fogVisuals`. Blur strength: `BlurFogMaskPass.BlurSettings` (`_HorizontalBlur`/`_VerticalBlur`).
- **Fog layer:** the mask renders the **`"Fog"`** layer; moving objects on/off that layer changes what occludes fog.

### Rendering: disable / tweak outlines & lights
- Outlines are baked at level generation: patch `OutlineGenerator.Generate` (skip to drop outlines) or the consuming material. `OutlineFinder.FindOutline` is reusable if you need contour data.
- Lights are URP `Light2D`. Disable `BlinkingLight`, `StationLightManager`, or `StationLightSource` components; `LightShapeBuilder` builds shadow-caster shapes. `LightSensor.IsInLight` / `lightLevel` are public for gameplay hooks.

### Screenshot / cinematic helpers
- `FreeMoveCamera` (free-fly + hides `objectsToDisable`) is the closest built-in to a photo mode.
- To render to an offscreen target, see `ResizeRenderTextureToScreenSize` (allocates a screen-sized `RenderTexture`) and `TextureUpdater` (partial texture blits). `OrthoSizeFromFiewOfView` shows how to mirror the main camera with a secondary one.
- Hide HUD/ship visuals for a clean shot by disabling the relevant `GameObject`s (as `FreeMoveCamera.objectsToDisable` does), and suppress shakes via the camera-shake patches above.

### General Harmony tips
- Services (`AudioManager`, `MusicManager`) are resolved through `ServiceLocator.Get<T>()` and live on a `DontDestroyOnLoad` object created by `AudioInstaller`. Grab them once after the game scene loads.
- The fog render passes use Unity's RenderGraph API (`RecordRenderGraph`), so patch those rather than the legacy `Execute`. The original source paths (`C:\Projects\Punk\Assets\Scripts\Rendering\Fog\…`) appear in pass strings, confirming class identity.
</content>
</invoke>
