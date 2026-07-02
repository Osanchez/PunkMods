using System;
using System.Linq;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace PunkSeedPicker
{
    /// <summary>
    /// A runtime-built overlay shown right before a new run generates: a title, an editable seed
    /// field (keyboard-captured, with paste/copy), and START / RANDOM / BACK. On START it writes the
    /// edited seed into the RunArguments and continues into the game via GameScene.GoToGameScene.
    ///
    /// Keyboard-captured rather than a TMP_InputField: the game ships no input field to clone, and a
    /// manual digit field is far more robust to build blind (no EventSystem/focus/font-on-field
    /// pitfalls). Mouse buttons still work as a bonus when the scene's EventSystem is present.
    /// </summary>
    public class SeedScreen : MonoBehaviour
    {
        private GameObject _canvas;
        private RunArguments _args;
        private RunSetupScreen _setup;
        private TMP_Text _display;
        private string _text = "0";
        private bool _confirmed;
        private float _openTime;
        private const int MaxLen = 10;          // int.MaxValue is 10 digits
        private const float InputDelay = 0.35f; // ignore input briefly so the class-select press doesn't carry in

        // Controller/keyboard focus among the buttons (START / RANDOM / BACK).
        private readonly System.Collections.Generic.List<(Image outer, Color border, Action act)> _btns
            = new System.Collections.Generic.List<(Image, Color, Action)>();
        private int _focus;
        private static readonly Color FocusBorder = new Color(1f, 0.78f, 0.32f, 1f);

        // Screens behind us whose input we suppress while open (so the same stick/keys don't also
        // navigate the loadout picker underneath), plus the scene EventSystem's saved nav state.
        private readonly System.Collections.Generic.List<MonoBehaviour> _suppressed
            = new System.Collections.Generic.List<MonoBehaviour>();
        private UnityEngine.EventSystems.EventSystem _es;
        private bool _prevNav;

        private static readonly FieldInfoCache LoadoutSelectedF =
            new FieldInfoCache(typeof(RunSetupScreen), "loadoutSelected");

        // ---- entry point (called from the Harmony patch) ----
        internal static void Open(RunArguments args, RunSetupScreen setup)
        {
            var go = new GameObject("SeedPickerCanvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            go.AddComponent<GraphicRaycaster>();
            var screen = go.AddComponent<SeedScreen>();
            screen.Build(go, args, setup);
        }

        // Theme colors (dark charcoal + amber accent, matching the game's UI).
        private static readonly Color PanelFill  = new Color(0.10f, 0.10f, 0.12f, 0.98f);
        private static readonly Color PanelBorder = new Color(0.28f, 0.28f, 0.34f, 1f);
        private static readonly Color BoxFill     = new Color(0.04f, 0.04f, 0.05f, 1f);
        private static readonly Color BtnFill     = new Color(0.15f, 0.15f, 0.18f, 1f);
        private static readonly Color BtnBorder   = new Color(0.34f, 0.34f, 0.40f, 1f);
        private static readonly Color Amber       = new Color(0.92f, 0.66f, 0.27f, 1f);
        private static readonly Color Muted       = new Color(0.62f, 0.62f, 0.66f, 1f);

        private void Build(GameObject canvas, RunArguments args, RunSetupScreen setup)
        {
            _canvas = canvas; _args = args; _setup = setup;
            _openTime = Time.unscaledTime;
            _text = args.seed.ToString();

            var font = UnityEngine.Object.FindObjectOfType<TMP_Text>()?.font;

            // dim, click-blocking backdrop
            var bg = NewImage(canvas.transform, new Color(0f, 0f, 0f, 0.85f));
            Stretch(bg.rectTransform);

            // centered bordered panel
            var panel = Framed(canvas.transform, Vector2.zero, new Vector2(1040f, 600f), PanelFill, PanelBorder, 3f);

            NewText(panel.transform, font, "WORLD SEED", 56, TextAlignmentOptions.Center,
                new Vector2(0f, 210f), new Vector2(960f, 84f), Color.white);

            var hint = NewText(panel.transform, font,
                "Type to edit    ·    Ctrl+V paste    ·    Ctrl+C copy    ·    Enter = start",
                22, TextAlignmentOptions.Center, new Vector2(0f, 138f), new Vector2(960f, 36f), Muted);
            AutoFit(hint, 12f, 22f);   // shrink to fit the panel width (no overflow)

            // the editable field box
            var box = Framed(panel.transform, new Vector2(0f, 28f), new Vector2(880f, 136f), BoxFill, PanelBorder, 2f);
            _display = NewText(box.transform, font, _text, 66, TextAlignmentOptions.Center,
                Vector2.zero, new Vector2(836f, 120f), Amber);
            AutoFit(_display, 28f, 66f);   // long (10-digit) seeds shrink to fit the field

            var size = new Vector2(284f, 96f);
            MakeButton(panel.transform, font, "START",  new Vector2(-302f, -190f), size, Confirm,   BtnFill, Amber,     Amber);
            MakeButton(panel.transform, font, "RANDOM", new Vector2(   0f, -190f), size, Randomize, BtnFill, BtnBorder, Color.white);
            MakeButton(panel.transform, font, "BACK",   new Vector2( 302f, -190f), size, Cancel,    BtnFill, BtnBorder, Color.white);
            _focus = 0; UpdateHighlight();

            SuppressBehind();
        }

        // While the seed overlay is up, stop the run-setup / loadout picker underneath from consuming
        // the same input, and disable EventSystem navigation (keyboard/gamepad move+submit) so it
        // can't drive selectables behind us. Mouse pointer input is untouched, and our own screen
        // polls devices directly, so both keep working. Everything is restored in OnDestroy.
        private void SuppressBehind()
        {
            foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mb == this) continue;
                switch (mb.GetType().Name)
                {
                    case "RunSetupScreen":
                    case "LoadoutSelector":
                    case "InputSelectorScreen":
                        if (mb.enabled) { mb.enabled = false; _suppressed.Add(mb); }
                        break;
                }
            }
            _es = UnityEngine.EventSystems.EventSystem.current;
            if (_es != null) { _prevNav = _es.sendNavigationEvents; _es.sendNavigationEvents = false; }
        }

        private void RestoreBehind()
        {
            foreach (var mb in _suppressed) if (mb != null) mb.enabled = true;
            _suppressed.Clear();
            if (_es != null) { _es.sendNavigationEvents = _prevNav; _es = null; }
        }

        private void OnDestroy() => RestoreBehind();

        // D-pad/stick or arrow keys move focus; South/Enter = confirm focused; Start = START;
        // East = BACK; North = RANDOM. (Typing a custom seed is still keyboard-only.)
        private void Navigate()
        {
            int prev = _focus;
            bool left = false, right = false, confirm = false, back = false, start = false, random = false;

            var gp = Gamepad.current;
            if (gp != null)
            {
                left   |= gp.dpad.left.wasPressedThisFrame  || gp.leftStick.left.wasPressedThisFrame;
                right  |= gp.dpad.right.wasPressedThisFrame || gp.leftStick.right.wasPressedThisFrame;
                confirm |= gp.buttonSouth.wasPressedThisFrame;
                back   |= gp.buttonEast.wasPressedThisFrame;
                start  |= gp.startButton.wasPressedThisFrame;
                random |= gp.buttonNorth.wasPressedThisFrame;
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                left  |= kb.leftArrowKey.wasPressedThisFrame;
                right |= kb.rightArrowKey.wasPressedThisFrame;
            }

            if (left)  _focus = Mathf.Max(0, _focus - 1);
            if (right) _focus = Mathf.Min(_btns.Count - 1, _focus + 1);
            if (_focus != prev) UpdateHighlight();

            if (random) { Randomize(); return; }
            if (start)  { Confirm(); return; }
            if (back)   { Cancel(); return; }
            if (confirm && _focus >= 0 && _focus < _btns.Count) _btns[_focus].act();
        }

        private void UpdateHighlight()
        {
            for (int i = 0; i < _btns.Count; i++)
                if (_btns[i].outer != null) _btns[i].outer.color = (i == _focus) ? FocusBorder : _btns[i].border;
        }

        private void Update()
        {
            if (_confirmed) return;
            // Brief lock-out after opening so the button/key that picked the class doesn't immediately
            // confirm (and instantly start) the seed screen.
            if (Time.unscaledTime - _openTime >= InputDelay)
            {
                try { ReadKeyboard(); Navigate(); }
                catch (Exception e) { Plugin.Log.LogWarning($"seed input failed: {e.Message}"); }
            }

            bool blink = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0;
            if (_display != null) _display.text = (_text.Length == 0 ? "" : _text) + (blink ? "|" : "");
        }

        private void ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return;

            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl && kb.vKey.wasPressedThisFrame)
            {
                var clip = GUIUtility.systemCopyBuffer ?? "";
                bool neg = clip.TrimStart().StartsWith("-");
                var digits = new string(clip.Where(char.IsDigit).Take(MaxLen).ToArray());
                _text = (neg ? "-" : "") + digits;
                return;
            }
            if (ctrl && kb.cKey.wasPressedThisFrame) { GUIUtility.systemCopyBuffer = _text; return; }

            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { Confirm(); return; }
            if (kb.backspaceKey.wasPressedThisFrame && _text.Length > 0) { _text = _text.Substring(0, _text.Length - 1); return; }
            if (kb.minusKey.wasPressedThisFrame || kb.numpadMinusKey.wasPressedThisFrame)
            { _text = _text.StartsWith("-") ? _text.Substring(1) : "-" + _text; return; }

            AppendDigit(kb.digit0Key, kb.numpad0Key, '0');
            AppendDigit(kb.digit1Key, kb.numpad1Key, '1');
            AppendDigit(kb.digit2Key, kb.numpad2Key, '2');
            AppendDigit(kb.digit3Key, kb.numpad3Key, '3');
            AppendDigit(kb.digit4Key, kb.numpad4Key, '4');
            AppendDigit(kb.digit5Key, kb.numpad5Key, '5');
            AppendDigit(kb.digit6Key, kb.numpad6Key, '6');
            AppendDigit(kb.digit7Key, kb.numpad7Key, '7');
            AppendDigit(kb.digit8Key, kb.numpad8Key, '8');
            AppendDigit(kb.digit9Key, kb.numpad9Key, '9');
        }

        private void AppendDigit(ButtonControl row, ButtonControl pad, char c)
        {
            int digits = _text.TrimStart('-').Length;
            if (digits >= MaxLen) return;
            if (row.wasPressedThisFrame || pad.wasPressedThisFrame) _text += c;
        }

        private void Randomize() => _text = new System.Random(Environment.TickCount).Next().ToString();

        private void Confirm()
        {
            if (_confirmed) return;
            _confirmed = true;
            if (!int.TryParse(_text, out int seed) || seed == 0) seed = _args.seed;   // 0 would re-randomize; keep original
            _args.seed = seed;
            var args = _args;
            Plugin.Log.LogInfo($"Starting run with seed {seed}.");
            Destroy(_canvas);
            GameScene.GoToGameScene(args);
        }

        private void Cancel()
        {
            // return to the loadout selector (still active behind us) so the class can be re-picked
            try { LoadoutSelectedF.Set(_setup, false); } catch { }
            _confirmed = true;
            Destroy(_canvas);
        }

        // ---------- tiny uGUI builders ----------
        private static Image NewImage(Transform parent, Color color)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private static TMP_Text NewText(Transform parent, TMP_FontAsset font, string text, float size,
            TextAlignmentOptions align, Vector2 pos = default, Vector2 dimSize = default, Color color = default)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text; t.fontSize = size; t.alignment = align;
            t.color = color == default ? Color.white : color;
            t.enableWordWrapping = false; t.richText = true;
            var rt = t.rectTransform;
            if (dimSize == default) { Stretch(rt); }
            else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.anchoredPosition = pos; rt.sizeDelta = dimSize; }
            return t;
        }

        private void MakeButton(Transform parent, TMP_FontAsset font, string label, Vector2 pos, Vector2 size,
            Action onClick, Color fill, Color border, Color textColor)
        {
            var outer = Framed(parent, pos, size, fill, border, 2f);
            var btn = outer.gameObject.AddComponent<Button>();
            btn.targetGraphic = outer;
            btn.onClick.AddListener(() => { try { onClick(); } catch (Exception e) { Plugin.Log.LogWarning($"button '{label}': {e.Message}"); } });
            NewText(outer.transform, font, label, 32, TextAlignmentOptions.Center, Vector2.zero, size, textColor);
            _btns.Add((outer, border, onClick));
        }

        // A bordered rect: an outer image (border color) with an inset inner fill. Returns the OUTER
        // image so callers can parent content on top of the fill (and use it as the click target).
        private static Image Framed(Transform parent, Vector2 pos, Vector2 size, Color fill, Color border, float bw)
        {
            var outer = NewImage(parent, border);
            Place(outer.rectTransform, pos, size);
            var inner = NewImage(outer.transform, fill);
            var rt = inner.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(bw, bw); rt.offsetMax = new Vector2(-bw, -bw);
            return outer;
        }

        // Let a single-line label shrink to fit its rect width so it never overflows the panel.
        private static void AutoFit(TMP_Text t, float min, float max)
        {
            if (t == null) return;
            t.enableWordWrapping = false;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = true;
            t.fontSizeMin = min;
            t.fontSizeMax = max;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        private static void Center(RectTransform rt, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero; rt.sizeDelta = size;
        }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }

    /// <summary>Small cached-reflection helper for a private field.</summary>
    internal sealed class FieldInfoCache
    {
        private readonly System.Reflection.FieldInfo _f;
        internal FieldInfoCache(Type t, string name) => _f = AccessTools.Field(t, name);
        internal void Set(object obj, object value) => _f?.SetValue(obj, value);
    }
}
