using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PunkInputTweaks
{
    /// <summary>
    /// Reduces gamepad input latency. The game never raises the Input System polling rate, so
    /// gamepads are sampled at Unity's default 60 Hz (~16 ms) while keyboard/mouse are event-driven.
    /// That makes the controller player feel laggier than the KB/M player. Raising
    /// <see cref="InputSystem.pollingFrequency"/> samples gamepads far more often (~4 ms at 250 Hz).
    ///
    /// Also helps a Remote Play friend's controller: Steam injects their input as a virtual gamepad
    /// on the host, which Unity polls at the same rate — so the host-side sampling delay drops too
    /// (the network/stream round-trip is separate and unaffected).
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.inputtweaks";
        public const string Name = "PUNK Input Tweaks";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;
        private static ConfigEntry<int> _pollingHz;

        // Kept so OnDestroy can unsubscribe the handler and restore the original polling rate on reload.
        private UnityAction<Scene, LoadSceneMode> _onSceneLoaded;
        private float _previousHz = -1f;   // InputSystem.pollingFrequency is a float

        private void Awake()
        {
            Log = Logger;
            var cfg = new ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), saveOnInit: true);
            _pollingHz = cfg.Bind("Input", "GamepadPollingHz", 250,
                new ConfigDescription("How often (Hz) the Input System polls gamepads. Unity's default is 60. " +
                                      "Higher = lower controller input latency. 125-250 is a good range.",
                                      new AcceptableValueRange<int>(60, 1000)));

            _previousHz = InputSystem.pollingFrequency;   // capture so we can restore it on reload
            Apply();
            // Re-apply on scene loads in case anything resets it.
            _onSceneLoaded = (_, __) => Apply();
            SceneManager.sceneLoaded += _onSceneLoaded;
        }

        // Hot-reload teardown: Harmony-free mod. Unsubscribe the scene-load handler (else a reload would
        // stack a second one) and restore the polling frequency we changed.
        private void OnDestroy()
        {
            try { if (_onSceneLoaded != null) SceneManager.sceneLoaded -= _onSceneLoaded; } catch { }
            try { if (_previousHz > 0f) InputSystem.pollingFrequency = _previousHz; } catch { }
        }

        private void Apply()
        {
            try
            {
                int hz = _pollingHz.Value;
                if (InputSystem.pollingFrequency != hz)
                {
                    InputSystem.pollingFrequency = hz;
                    Log.LogInfo($"Gamepad polling frequency set to {hz} Hz (was Unity default 60).");
                }
            }
            catch (Exception e) { Log.LogWarning($"Failed to set polling frequency: {e.Message}"); }
        }
    }
}
