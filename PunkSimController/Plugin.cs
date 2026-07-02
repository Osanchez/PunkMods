using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace PunkSimController
{
    /// <summary>
    /// Dev tool: emulate multiple controllers with a single keyboard, so a local-multiplayer
    /// join screen can be tested solo. Each "sim controller" is a real virtual <see cref="Gamepad"/>
    /// added to the Input System — the game sees them exactly like physical pads (they appear on the
    /// join screen and can drive ships). Your keyboard puppets the currently-selected one.
    ///
    /// While any sims exist, PunkFourPlayer suppresses the keyboard's OWN join row, so the keyboard
    /// just puppets the currently-selected sim with normal controls (no double-control). Sims also
    /// auto-join the session (they skip the "press Start to join" gate).
    ///
    /// Keys:
    ///   F5  add a sim controller (and select it)
    ///   F6  remove the selected sim controller
    ///   F7 / F8   select the previous / next sim (the player you're currently controlling)
    ///   WASD            move the selected player (its left stick)
    ///   Mouse           aim (right stick toward the cursor)
    ///   LMB / RMB       primary / secondary fire (triggers)
    ///   Q E R F Shift   abilities / modules / dash (X, Y, B, RB, LB)
    ///   Space / Enter   A button (confirm / ready / pick profile / use)
    ///   Arrows          D-pad (menus / item wheel)
    ///   F9              start the game (host)
    ///   F11             show / hide this overlay (emulation keeps working)
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.simcontroller";
        public const string Name = "PUNK Sim Controller (debug)";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        private readonly List<Gamepad> _sims = new List<Gamepad>();
        private readonly List<string> _status = new List<string>();
        private int _selected = -1;
        private int _counter;
        private GUIStyle _style;

        // Whether the on-screen overlay is drawn. F11 toggles it; emulation (sims + hotkeys) keeps
        // working while hidden — this only suppresses the OnGUI render. Runtime-only (resets to shown).
        private bool _overlayVisible = true;

        // Whether the emulator is active. Persisted to this mod's own config (so it survives
        // restarts); also toggled in the Mods menu. When off: no overlay, no hotkeys, sims removed.
        private ConfigEntry<bool> _enabled;
        internal bool Enabled => _enabled != null && _enabled.Value;

        private void Awake()
        {
            Log = Logger;

            // Per-mod config file, in this DLL's own folder (BepInEx/plugins/PunkSimController/config.cfg).
            var cfg = new BepInEx.Configuration.ConfigFile(Path.Combine(ModFolder.Dir, "config.cfg"), saveOnInit: true);
            _enabled = cfg.Bind("General", "Enabled", false,
                "Controller emulation on/off (persisted). Also toggleable in the in-game Mods menu.");

            // Register an ON/OFF row in the Mods menu IF that framework is installed (else no-op).
            ModMenuBridge.AddToggle("Sim Controller (emulation)", () => Enabled, SetEnabled);

            Log.LogInfo($"{Name} v{Version} loaded. Emulation {(Enabled ? "ON" : "OFF")}" +
                        (ModMenuBridge.Available ? " | toggle in Mods menu." : " | no Mods menu (edit config.cfg).") +
                        " Keys: F5 add, F6 remove, F7/F8 select; WASD move, mouse aim, LMB/RMB fire, Q/E/R/F/Shift skills, Space use, arrows dpad. Start: F9.");
        }

        internal void SetEnabled(bool value)
        {
            if (_enabled != null) _enabled.Value = value;   // persisted (auto-saves)
            if (!value) RemoveAllSims();
        }

        private void RemoveAllSims()
        {
            foreach (var g in _sims) if (g != null && g.added) InputSystem.RemoveDevice(g);
            _sims.Clear();
            _status.Clear();
            _selected = -1;
        }

        private void OnDestroy() => RemoveAllSims();

        private void Update()
        {
            if (!Enabled) return;
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.f5Key.wasPressedThisFrame) AddSim();
            if (kb.f6Key.wasPressedThisFrame) RemoveSelected();
            if (kb.f7Key.wasPressedThisFrame) Cycle(-1);   // select previous player to control
            if (kb.f8Key.wasPressedThisFrame) Cycle(+1);   // select next player to control
            if (kb.f11Key.wasPressedThisFrame) _overlayVisible = !_overlayVisible;   // hide/show overlay only

            DriveSims(kb);
            BuildStatus();
        }

        // Per-frame status per sim, cached so OnGUI just renders strings:
        //  - On the join screen: the column it's picking (P1..P4) + READY.
        //  - In game (join screen gone): the actual player tag of the ship it's driving ("Player N").
        private void BuildStatus()
        {
            _status.Clear();
            var rows = Resources.FindObjectsOfTypeAll<InputSelectorDeviceRow>();
            for (int i = 0; i < _sims.Count; i++)
            {
                var pad = _sims[i];
                InputSelectorDeviceRow row = null;
                if (pad != null)
                    foreach (var r in rows)
                        if (r != null && r.gameObject.scene.IsValid() && r.Device == pad) { row = r; break; }

                string info;
                if (row != null)
                {
                    // join phase: show the chosen column + ready state
                    string ready = row.IsReady ? "  <color=#7CFC7C>READY</color>" : "";
                    info = ColLabel(row.Position) + ready;
                }
                else
                {
                    // in game: show the player tag of the ship this sim controls
                    int slot = -1;
                    if (pad != null) FindShipForPad(pad, out slot);
                    info = slot >= 0
                        ? $"<color=#FFD166>Player {slot + 1}</color>"
                        : "<color=#888>—</color>";
                }

                string marker = (i == _selected) ? "<b>►</b>" : "   ";
                _status.Add($"{marker} #{i + 1}   {info}");
            }
        }

        // The ship a given sim pad is driving (matched by input device), plus its 0-based player
        // slot = its index in ShipManager.Ships (== the PLAYER N number after the slot-order fix).
        // Returns null / slot -1 before ships exist or if the pad isn't bound to one.
        private static Ship FindShipForPad(Gamepad pad, out int slot)
        {
            slot = -1;
            try
            {
                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships == null) return null;
                int idx = 0;
                foreach (var s in ships)
                {
                    var pi = s?.shipInput?.PlayerInput;
                    if (pi != null)
                    {
                        var devs = pi.devices;
                        for (int d = 0; d < devs.Count; d++)
                            if (devs[d] == pad) { slot = idx; return s; }
                    }
                    idx++;
                }
            }
            catch { }
            return null;
        }

        // Map a row's Position to its player number via PunkFourPlayer's JoinLayout (which knows the
        // N-column layout). Falls back to the old P1/P2 split if that mod isn't installed.
        private static readonly MethodInfo PosToPlayerIndexM =
            Type.GetType("PunkFourPlayer.JoinLayout, PunkFourPlayer")
                ?.GetMethod("PosToPlayerIndex", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        private static string ColLabel(int pos)
        {
            if (pos == 0) return "<color=#888>unassigned</color>";
            int playerIndex = -1;
            try { if (PosToPlayerIndexM != null) playerIndex = (int)PosToPlayerIndexM.Invoke(null, new object[] { pos }); }
            catch { }
            if (playerIndex < 0) playerIndex = pos < 0 ? 0 : 1;   // fallback
            return $"<color=#FFD166>P{playerIndex + 1}</color>";
        }

        private void AddSim()
        {
            // Never let total controllers (real + emulated) exceed 4 players on screen.
            if (Gamepad.all.Count >= 4)
            {
                Log.LogInfo("Max 4 player controllers already present; not adding another sim.");
                return;
            }
            var pad = InputSystem.AddDevice<Gamepad>($"PunkSim{++_counter}");
            _sims.Add(pad);
            _selected = _sims.Count - 1;
            Log.LogInfo($"Added sim controller #{_sims.Count} ({pad.deviceId}); selected.");
        }

        private void Cycle(int dir)
        {
            if (_sims.Count == 0) return;
            _selected = ((_selected + dir) % _sims.Count + _sims.Count) % _sims.Count;
        }

        private void RemoveSelected()
        {
            if (_selected < 0 || _selected >= _sims.Count) return;
            var pad = _sims[_selected];
            if (pad != null && pad.added) InputSystem.RemoveDevice(pad);
            _sims.RemoveAt(_selected);
            if (_selected >= _sims.Count) _selected = _sims.Count - 1;
            Log.LogInfo($"Removed sim controller; {_sims.Count} remain.");
        }

        // Each frame, push state into every sim pad: the selected one mirrors J/L/K, the rest stay neutral.
        private void DriveSims(Keyboard kb)
        {
            for (int i = 0; i < _sims.Count; i++)
            {
                var pad = _sims[i];
                if (pad == null || !pad.added) continue;

                var state = new GamepadState();
                if (i == _selected) BuildSelectedState(ref state, kb, pad);
                InputSystem.QueueStateEvent(pad, state);
            }
        }

        // Map the full keyboard+mouse onto the selected sim's gamepad so it can do everything a real
        // player can: move, aim at the mouse, fire, and every ability/module/dash button.
        private void BuildSelectedState(ref GamepadState state, Keyboard kb, Gamepad pad)
        {
            // movement (left stick) — WASD
            float x = 0f, y = 0f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.wKey.isPressed) y += 1f;
            if (kb.sKey.isPressed) y -= 1f;
            state.leftStick = new Vector2(x, y);

            // aim (right stick) — toward the mouse cursor, once in game
            state.rightStick = AimStick(pad);

            // face buttons / shoulders / dpad — abilities, dash, use, item wheel, etc.
            if (kb.spaceKey.isPressed || kb.enterKey.isPressed) state = state.WithButton(GamepadButton.South);          // A (confirm / dash / use)
            if (kb.qKey.isPressed)          state = state.WithButton(GamepadButton.West);                              // X
            if (kb.eKey.isPressed)          state = state.WithButton(GamepadButton.North);                             // Y
            if (kb.rKey.isPressed)          state = state.WithButton(GamepadButton.East);                              // B
            if (kb.fKey.isPressed)          state = state.WithButton(GamepadButton.RightShoulder);                     // RB
            if (kb.leftShiftKey.isPressed)  state = state.WithButton(GamepadButton.LeftShoulder);                      // LB (dash on many layouts)
            if (kb.upArrowKey.isPressed)    state = state.WithButton(GamepadButton.DpadUp);
            if (kb.downArrowKey.isPressed)  state = state.WithButton(GamepadButton.DpadDown);
            if (kb.leftArrowKey.isPressed)  state = state.WithButton(GamepadButton.DpadLeft);
            if (kb.rightArrowKey.isPressed) state = state.WithButton(GamepadButton.DpadRight);

            // triggers — mouse buttons (primary / secondary fire)
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.isPressed)  { state.rightTrigger = 1f; state = state.WithButton(GamepadButton.RightTrigger); }
                if (mouse.rightButton.isPressed) { state.leftTrigger  = 1f; state = state.WithButton(GamepadButton.LeftTrigger); }
            }
        }

        // The right-stick vector that points this sim's ship toward the mouse cursor.
        private static Vector2 AimStick(Gamepad pad)
        {
            try
            {
                var mouse = Mouse.current;
                var cam = Camera.main;
                if (mouse == null || cam == null) return Vector2.zero;

                var ship = FindShipForPad(pad, out _);
                if (ship == null) return Vector2.zero;

                Vector3 sp = cam.WorldToScreenPoint(ship.transform.position);
                Vector2 dir = mouse.position.ReadValue() - new Vector2(sp.x, sp.y);
                return dir.sqrMagnitude > 1f ? dir.normalized : Vector2.zero;
            }
            catch { return Vector2.zero; }
        }

        private void OnGUI()
        {
            if (!Enabled || !_overlayVisible) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = false, normal = { textColor = Color.white } };

            const float pad = 10f, headH = 26f, hintH = 22f, gap = 10f, rowH = 22f;
            int statusLines = (_sims.Count == 0) ? 1 : _status.Count;
            float h = pad + headH + hintH * 2f + gap + statusLines * rowH + pad;
            var rect = new Rect(12, 12, 470, h);

            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float x = rect.x + 12, w = rect.width - 24, y = rect.y + pad;

            GUI.Label(new Rect(x, y, w, headH), $"<b>SIM CONTROLLERS: {_sims.Count}</b>", _style); y += headH;
            GUI.Label(new Rect(x, y, w, hintH), "<size=12><color=#9aa0aa>F5 add   ·   F6 remove   ·   F7/F8 select player   ·   F11 hide</color></size>", _style); y += hintH;
            GUI.Label(new Rect(x, y, w, hintH), "<size=12><color=#9aa0aa>WASD move · mouse aim · LMB/RMB fire · Q/E/R/F/Shift skills · Space use</color></size>", _style); y += hintH + gap;

            if (_sims.Count == 0)
                GUI.Label(new Rect(x, y, w, rowH), "<color=#9aa0aa>press F5 to add a simulated controller</color>", _style);
            else
                foreach (var line in _status) { GUI.Label(new Rect(x, y, w, rowH), line, _style); y += rowH; }
        }
    }
}
