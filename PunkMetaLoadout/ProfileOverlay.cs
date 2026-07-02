using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace PunkMetaLoadout
{
    /// <summary>
    /// Blocking overlay shown when a player readies up on the join screen: pick an existing profile,
    /// create a new one, or rename one. The list scrolls (windowed) when there are many profiles.
    /// Naming uses an on-screen keyboard (navigable by controller, clickable by mouse) plus the
    /// physical keyboard. On select the profile is assigned to that player's slot and the overlay
    /// closes.
    ///
    /// This now lives in PunkMetaLoadout (which owns the profile concept/data), so it calls
    /// <see cref="ProfileApi"/> / <see cref="ProfileStore"/> directly instead of via reflection.
    /// PunkFourPlayer (if installed) observes <see cref="ChangeCounter"/> to refresh its header tags;
    /// this overlay never calls into PunkFourPlayer.
    /// </summary>
    public class ProfileOverlay : MonoBehaviour
    {
        internal static bool IsOpen;

        // Bumped every time the picker closes (i.e. a slot assignment may have changed). PunkFourPlayer
        // polls this to know when to refresh its per-column profile tags — no callback into FourPlayer.
        internal static int ChangeCounter;

        private const string NoProfile = "No Profile";
        private const string CreateNew = "Create New";
        private const int    MaxVisible = 9;
        private const float  RowH = 46f;

        private static readonly Color PanelFill   = new Color(0.10f, 0.10f, 0.12f, 0.98f);
        private static readonly Color PanelBorder = new Color(0.28f, 0.28f, 0.34f, 1f);
        private static readonly Color KeyFill     = new Color(0.16f, 0.16f, 0.19f, 1f);
        private static readonly Color KeyBorder   = new Color(0.30f, 0.30f, 0.36f, 1f);
        private static readonly Color Sel         = new Color(0.92f, 0.66f, 0.27f, 1f);
        private static readonly Color Idle        = new Color(0f, 0f, 0f, 0f);
        private static readonly Color Amber       = new Color(0.92f, 0.66f, 0.27f, 1f);
        private static readonly Color Muted       = new Color(0.62f, 0.62f, 0.66f, 1f);

        private GameObject _canvas;
        private InputSelectorScreen _screen;   // may be null in the single-player case (no join screen)
        private int _player;
        private InputDevice _device;
        private bool _isSim;
        private TMP_FontAsset _font;
        private Action _onClose;               // invoked once after the overlay closes (single-player start)

        private enum Mode { List, Name }
        private Mode _mode = Mode.List;

        // list state
        private List<string> _options = new List<string>();
        private int _sel, _scrollTop;
        private RectTransform _listRoot;
        private readonly List<Image> _rowBg = new List<Image>();
        private readonly List<TMP_Text> _rowTx = new List<TMP_Text>();
        private TMP_Text _scrollUp, _scrollDown;

        // name state
        private string _nameBuf = "";
        private string _renameTarget;
        private GameObject _nameRoot;
        private TMP_Text _nameField;
        private int _cursor;
        private sealed class KbKey { public Image bg; public char ch; public string action; public Vector2 c; }
        private readonly List<KbKey> _keys = new List<KbKey>();

        private TMP_Text _title, _hint;

        // screen may be null (single-player has no join screen). onClose runs once after the overlay
        // closes — the single-player start uses it to proceed into the run only after a pick is made.
        internal static void Open(InputSelectorScreen screen, int player, InputDevice device, Action onClose = null)
        {
            if (IsOpen) return;
            IsOpen = true;
            var go = new GameObject("ProfileOverlayCanvas");
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 31000;
            go.AddComponent<GraphicRaycaster>();
            var o = go.AddComponent<ProfileOverlay>();
            o._canvas = go; o._screen = screen; o._player = player; o._device = device; o._onClose = onClose;
            o._isSim = JoinProfileTrigger.IsSim(device);
            o.Build();
        }

        private void Build()
        {
            _font = UnityEngine.Object.FindObjectOfType<TMP_Text>()?.font;

            // Fully opaque backdrop so the player-select screen underneath can't show through
            // (both the dim margins around the panel and any gap behind the on-screen keyboard).
            var bg = NewImage(_canvas.transform, new Color(0.04f, 0.04f, 0.05f, 1f));
            Stretch(bg.rectTransform);

            var panel = Framed(_canvas.transform, Vector2.zero, new Vector2(760f, 660f), PanelFill, PanelBorder, 3f);

            _title = NewText(panel.transform, "", 38, TextAlignmentOptions.Center,
                new Vector2(0f, 280f), new Vector2(700f, 52f), Color.white);

            // ---- list ----
            _listRoot = NewImage(panel.transform, Idle).rectTransform;
            Place(_listRoot, new Vector2(0f, -8f), new Vector2(680f, MaxVisible * RowH + 8f));
            float top = (MaxVisible - 1) * 0.5f * RowH;
            for (int i = 0; i < MaxVisible; i++)
            {
                var row = NewImage(_listRoot, Idle);
                Place(row.rectTransform, new Vector2(0f, top - i * RowH), new Vector2(640f, RowH - 6f));
                _rowBg.Add(row);
                _rowTx.Add(NewText(row.transform, "", 28, TextAlignmentOptions.Center, Vector2.zero, new Vector2(620f, RowH - 6f), Color.white));
            }
            _scrollUp   = NewText(panel.transform, "▲", 20, TextAlignmentOptions.Center, new Vector2(0f, 248f), new Vector2(40f, 24f), Muted);
            _scrollDown = NewText(panel.transform, "▼", 20, TextAlignmentOptions.Center, new Vector2(0f, -250f), new Vector2(40f, 24f), Muted);

            // ---- name + on-screen keyboard ----
            _nameRoot = new GameObject("NameRoot", typeof(RectTransform));
            _nameRoot.GetComponent<RectTransform>().SetParent(panel.transform, false);
            Stretch(_nameRoot.GetComponent<RectTransform>());
            var field = Framed(_nameRoot.transform, new Vector2(0f, 250f), new Vector2(680f, 76f), new Color(0.04f, 0.04f, 0.05f, 1f), Amber, 2f);
            _nameField = NewText(field.transform, "", 44, TextAlignmentOptions.Center, Vector2.zero, new Vector2(640f, 64f), Amber);
            BuildKeyboard(_nameRoot.transform);

            _hint = NewText(panel.transform, "", 19, TextAlignmentOptions.Center,
                new Vector2(0f, -300f), new Vector2(720f, 28f), Muted);

            RebuildOptions(selectCurrent: false);   // always open hovered on "No Profile" (index 0)
            ShowMode();
        }

        // ---------- list ----------
        private void RebuildOptions(bool selectCurrent)
        {
            _options = new List<string> { NoProfile, CreateNew };
            _options.AddRange(ProfileApi.List());

            if (selectCurrent)
            {
                var cur = ProfileApi.GetSlot(_player);
                _sel = string.IsNullOrEmpty(cur) ? 0 : Mathf.Max(0, _options.IndexOf(cur));
            }
            _sel = Mathf.Clamp(_sel, 0, _options.Count - 1);
            ClampScroll();
            RefreshList();
        }

        private void ClampScroll()
        {
            if (_sel < _scrollTop) _scrollTop = _sel;
            else if (_sel >= _scrollTop + MaxVisible) _scrollTop = _sel - MaxVisible + 1;
            _scrollTop = Mathf.Clamp(_scrollTop, 0, Mathf.Max(0, _options.Count - MaxVisible));
        }

        private void RefreshList()
        {
            for (int i = 0; i < MaxVisible; i++)
            {
                int idx = _scrollTop + i;
                bool used = idx < _options.Count;
                _rowBg[i].gameObject.SetActive(used);
                if (!used) continue;
                bool on = idx == _sel;
                _rowBg[i].color = on ? Sel : Idle;
                _rowTx[i].color = on ? Color.black : Color.white;
                _rowTx[i].text = _options[idx] == CreateNew ? "+  CREATE NEW" : _options[idx].ToUpper();
            }
            if (_scrollUp != null) _scrollUp.gameObject.SetActive(_scrollTop > 0);
            if (_scrollDown != null) _scrollDown.gameObject.SetActive(_scrollTop + MaxVisible < _options.Count);
        }

        private void ShowMode()
        {
            _nameRoot.SetActive(_mode == Mode.Name);
            _listRoot.gameObject.SetActive(_mode == Mode.List);
            _scrollUp.transform.parent.gameObject.SetActive(true);
            _scrollUp.gameObject.SetActive(_mode == Mode.List && _scrollTop > 0);
            _scrollDown.gameObject.SetActive(_mode == Mode.List && _scrollTop + MaxVisible < _options.Count);

            _title.text = _mode == Mode.List ? $"PLAYER {_player + 1}  —  PROFILE" : $"PLAYER {_player + 1}  —  NAME";
            _hint.text = _mode == Mode.List
                ? "<size=92%>↕ select    Ⓐ confirm    Ⓑ back    Ⓨ rename</size>"
                : "<size=92%>move + Ⓐ to type    DONE / Enter confirm    Ⓑ back    (keyboard also types)</size>";
        }

        private void Update()
        {
            try { if (_mode == Mode.List) NavList(); else NavName(); }
            catch (Exception e) { Plugin.Log.LogWarning($"profile overlay: {e.Message}"); }
        }

        private void NavList()
        {
            var gp = _device as Gamepad;
            var kb = _device as Keyboard;
            bool up    = P(gp?.dpad.up)   || P(gp?.leftStick.up)   || P(kb?.upArrowKey);
            bool down  = P(gp?.dpad.down)  || P(gp?.leftStick.down) || P(kb?.downArrowKey);
            bool ok    = P(gp?.buttonSouth) || P(kb?.enterKey) || P(kb?.numpadEnterKey);
            bool back  = P(gp?.buttonEast)  || P(kb?.escapeKey);
            bool rename = P(gp?.buttonNorth) || P(kb?.f2Key);

            if (up)   { _sel = (_sel - 1 + _options.Count) % _options.Count; ClampScroll(); RefreshList(); }
            if (down) { _sel = (_sel + 1) % _options.Count; ClampScroll(); RefreshList(); }

            if (rename)
            {
                string opt = _options[_sel];
                if (opt != NoProfile && opt != CreateNew) { _renameTarget = opt; _nameBuf = opt; EnterName(); }
                return;
            }
            if (back) { Close(); return; }
            if (ok)
            {
                string opt = _options[_sel];
                if (opt == NoProfile) { ProfileApi.SetSlot(_player, null); Close(); }
                else if (opt == CreateNew) { _renameTarget = null; _nameBuf = ""; EnterName(); }
                else { ProfileApi.SetSlot(_player, opt); Close(); }
            }
        }

        // ---------- name entry + on-screen keyboard ----------
        private void EnterName() { _mode = Mode.Name; _cursor = 0; HighlightKeys(); ShowMode(); }

        private void NavName()
        {
            var gp = _device as Gamepad;
            // Physical keyboard typing for a real keyboard player or a sim (driven by the keyboard).
            if (_device is Keyboard || _isSim)
            {
                var phys = Keyboard.current;
                if (phys != null)
                {
                    if (phys.backspaceKey.wasPressedThisFrame && _nameBuf.Length > 0) _nameBuf = _nameBuf.Substring(0, _nameBuf.Length - 1);
                    else if (_nameBuf.Length < 24)
                    {
                        for (Key k = Key.A; k <= Key.Z; k++) if (phys[k].wasPressedThisFrame) _nameBuf += (char)('A' + (k - Key.A));
                        for (Key k = Key.Digit0; k <= Key.Digit9; k++) if (phys[k].wasPressedThisFrame) _nameBuf += (char)('0' + (k - Key.Digit0));
                        if (phys.spaceKey.wasPressedThisFrame && _nameBuf.Length > 0) _nameBuf += ' ';
                    }
                    if (phys.enterKey.wasPressedThisFrame || phys.numpadEnterKey.wasPressedThisFrame) { ConfirmName(); return; }
                }
            }

            // On-screen keyboard navigation (real gamepads use South to press; sims type physically).
            var kb = _device as Keyboard;
            int dx = (P(gp?.dpad.right) || P(gp?.leftStick.right) || P(kb?.rightArrowKey) ? 1 : 0)
                   - (P(gp?.dpad.left)  || P(gp?.leftStick.left)  || P(kb?.leftArrowKey)  ? 1 : 0);
            int dy = (P(gp?.dpad.up)    || P(gp?.leftStick.up)    || P(kb?.upArrowKey)    ? 1 : 0)
                   - (P(gp?.dpad.down)  || P(gp?.leftStick.down)  || P(kb?.downArrowKey)  ? 1 : 0);
            if (dx != 0 || dy != 0) MoveCursor(dx, dy);

            if (!_isSim && gp != null && P(gp.buttonSouth)) PressKey(_cursor);
            if (P(gp?.buttonEast) || P(kb?.escapeKey)) { _mode = Mode.List; _renameTarget = null; RefreshList(); ShowMode(); return; }

            bool blink = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0;
            _nameField.text = _nameBuf + (blink ? "|" : "");
        }

        private void PressKey(int idx)
        {
            if (idx < 0 || idx >= _keys.Count) return;
            var k = _keys[idx];
            if (k.action == "SPACE") { if (_nameBuf.Length > 0 && _nameBuf.Length < 24) _nameBuf += ' '; }
            else if (k.action == "DEL") { if (_nameBuf.Length > 0) _nameBuf = _nameBuf.Substring(0, _nameBuf.Length - 1); }
            else if (k.action == "DONE") { ConfirmName(); }
            else if (_nameBuf.Length < 24) _nameBuf += k.ch;
        }

        private void ConfirmName()
        {
            if (_renameTarget != null)
            {
                ProfileApi.Rename(_renameTarget, _nameBuf);
                _renameTarget = null; _mode = Mode.List;
                RebuildOptions(selectCurrent: false); ShowMode();
            }
            else
            {
                var created = ProfileApi.Create(_nameBuf);   // empty -> auto "Profile N"
                ProfileApi.SetSlot(_player, created);
                Close();
            }
        }

        private void MoveCursor(int dx, int dy)
        {
            Vector2 cp = _keys[_cursor].c;
            int best = -1; float bestScore = 1e9f;
            for (int i = 0; i < _keys.Count; i++)
            {
                if (i == _cursor) continue;
                Vector2 d = _keys[i].c - cp;
                float along = d.x * dx + d.y * dy;
                if (along <= 1f) continue;
                float perp = Mathf.Abs(d.x * dy - d.y * dx);
                float score = along + perp * 3f;
                if (score < bestScore) { bestScore = score; best = i; }
            }
            if (best >= 0) { _cursor = best; HighlightKeys(); }
        }

        private void HighlightKeys()
        {
            for (int i = 0; i < _keys.Count; i++)
                if (_keys[i].bg != null) _keys[i].bg.color = (i == _cursor) ? Sel : KeyFill;
        }

        private void BuildKeyboard(Transform parent)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            int cols = 6;
            float kw = 80f, kh = 50f, gx = 8f, gy = 8f;
            float startX = -((cols * kw + (cols - 1) * gx) - kw) * 0.5f;
            float topY = 130f;

            for (int i = 0; i < chars.Length; i++)
            {
                int r = i / cols, c = i % cols;
                var pos = new Vector2(startX + c * (kw + gx), topY - r * (kh + gy));
                AddKey(parent, pos, new Vector2(kw, kh), chars[i].ToString(), chars[i], "");
            }
            float ay = topY - 6 * (kh + gy) - 6f;
            AddKey(parent, new Vector2(-150f, ay), new Vector2(190f, kh), "SPACE", ' ', "SPACE");
            AddKey(parent, new Vector2(40f,   ay), new Vector2(110f, kh), "DEL",   ' ', "DEL");
            AddKey(parent, new Vector2(200f,  ay), new Vector2(150f, kh), "DONE",  ' ', "DONE");
        }

        private void AddKey(Transform parent, Vector2 pos, Vector2 size, string label, char ch, string action)
        {
            var outer = Framed(parent, pos, size, KeyFill, KeyBorder, 2f);
            int idx = _keys.Count;
            var btn = outer.gameObject.AddComponent<Button>();
            btn.targetGraphic = outer;
            btn.onClick.AddListener(() => { try { PressKey(idx); } catch { } });
            NewText(outer.transform, label, label.Length > 1 ? 24 : 30, TextAlignmentOptions.Center, Vector2.zero, size, Color.white);
            _keys.Add(new KbKey { bg = outer, ch = ch, action = action, c = pos });
        }

        private void Close()
        {
            IsOpen = false;
            ChangeCounter++;   // signal observers (e.g. PunkFourPlayer's header) that a slot may have changed
            var cb = _onClose; _onClose = null;
            Destroy(_canvas);
            // Single-player: resume the run start now that a profile (or "No Profile") is chosen. Guarded
            // so a callback failure can't leave the overlay stuck — worst case the run just doesn't start.
            if (cb != null)
            {
                try { cb(); }
                catch (Exception e) { Plugin.Log?.LogWarning($"profile overlay close callback failed: {e.Message}"); }
            }
        }

        // Hot-reload teardown: if the picker is open when the mod unloads, tear its canvas down so it
        // doesn't linger as an orphaned overlay. Skips the ChangeCounter bump (no slot change happened).
        internal static void ForceClose()
        {
            if (!IsOpen) return;
            IsOpen = false;
            try
            {
                var o = UnityEngine.Object.FindObjectOfType<ProfileOverlay>();
                if (o != null) UnityEngine.Object.Destroy(o.gameObject);
            }
            catch { }
        }

        private static bool P(ButtonControl b) => b != null && b.wasPressedThisFrame;

        // ---- tiny uGUI builders ----
        private static Image NewImage(Transform parent, Color color)
        {
            var go = new GameObject("Img", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            return img;
        }

        private TMP_Text NewText(Transform parent, string text, float size, TextAlignmentOptions align,
            Vector2 pos, Vector2 dim, Color color)
        {
            var go = new GameObject("Txt", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) t.font = _font;
            t.text = text; t.fontSize = size; t.alignment = align; t.color = color;
            t.enableWordWrapping = false; t.richText = true;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = dim;
            return t;
        }

        private static Image Framed(Transform parent, Vector2 pos, Vector2 size, Color fill, Color border, float bw)
        {
            var outer = NewImage(parent, border);
            Place(outer.rectTransform, pos, size);
            var inner = NewImage(outer.transform, fill);
            inner.raycastTarget = false;
            var rt = inner.rectTransform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(bw, bw); rt.offsetMax = new Vector2(-bw, -bw);
            return outer;
        }

        private static void Stretch(RectTransform rt)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero; }

        private static void Place(RectTransform rt, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
        }
    }
}
