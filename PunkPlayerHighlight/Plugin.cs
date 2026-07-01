using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using TMPro;
using UnityEngine;

namespace PunkPlayerHighlight
{
    /// <summary>
    /// A neon glow ring around each player's ship (in that player's color) plus an optional P1-P4
    /// label above it. Implemented as world-space objects that track each ship — so they stay
    /// centered and scale correctly with the camera zoom — and only appear while you're actually
    /// flying (hidden in the shop, pause, map, menus). Both elements toggle from the Mods menu.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.playerhighlight";
        public const string Name = "PUNK Player Highlight";
        public const string Version = "2.0.0";

        internal static ManualLogSource Log;
        internal static BepInEx.Configuration.ConfigEntry<bool> Highlight;
        internal static BepInEx.Configuration.ConfigEntry<bool> Labels;

        private const int   Tex   = 256;
        private const float Ppu   = Tex / 3.0f;   // texture spans ~3 world units (ring ~1.9 across)
        private const float LabelY        = 1.35f;
        private const float LabelFontSize = 4f;

        private static Sprite _ring, _halo;
        private static TMP_FontAsset _font;
        private static bool _fontResolved;

        private sealed class Glow
        {
            public GameObject root;
            public SpriteRenderer halo, ring;
            public TextMeshPro label;
            public Color ringColor, haloColor;
        }

        private readonly Dictionary<Ship, Glow> _glows = new Dictionary<Ship, Glow>();
        private readonly List<Ship> _stale = new List<Ship>();

        private void Awake()
        {
            Log = Logger;
            var cfg = new BepInEx.Configuration.ConfigFile(System.IO.Path.Combine(ModFolder.Dir, "config.cfg"), true);
            Highlight = cfg.Bind("General", "Highlight", true, "Neon ring around each player's ship.");
            Labels    = cfg.Bind("General", "PlayerLabels", true, "P1-P4 label above each player's ship.");

            ModMenuBridge.AddToggle("Highlight", () => Highlight.Value, v => Highlight.Value = v);
            ModMenuBridge.AddToggle("Player Labels (P1-P4)", () => Labels.Value, v => Labels.Value = v);

            Log.LogInfo($"{Name} v{Version} loaded. Highlight={Highlight.Value}, Labels={Labels.Value}.");
        }

        private void Update()
        {
            if (Highlight == null) return;

            var ships = ServiceLocator.Get<ShipManager>()?.Ships;
            bool anyOn = Highlight.Value || Labels.Value;
            if (ships == null || ships.Count == 0 || !anyOn) { HideAll(); return; }

            EnsureAssets();

            // Drop glows for ships that no longer exist.
            _stale.Clear();
            foreach (var kv in _glows)
                if (kv.Key == null || !ships.Contains(kv.Key)) _stale.Add(kv.Key);
            foreach (var s in _stale) { if (_glows.TryGetValue(s, out var g) && g.root != null) Destroy(g.root); _glows.Remove(s); }

            float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 2.5f));

            for (int i = 0; i < ships.Count; i++)
            {
                var ship = ships[i];
                if (ship == null) continue;
                if (!_glows.TryGetValue(ship, out var g)) { g = Create(ship, i); _glows[ship] = g; }
                if (g == null || g.root == null) continue;

                // Only while actively flying: ship control is disabled in shop / pause / map / menus.
                bool flying = false;
                try { flying = ship.shipInput != null && ship.shipInput.ShipControlActionMap != null && ship.shipInput.ShipControlActionMap.Enabled; }
                catch { }
                bool show = flying && !ship.IsDead;

                if (g.root.activeSelf != show) g.root.SetActive(show);
                if (!show) continue;

                g.root.transform.position = ship.transform.position;
                g.root.transform.rotation = Quaternion.identity;   // keep ring/label world-aligned (don't spin with the ship)

                g.ring.enabled = Highlight.Value;
                g.halo.enabled = Highlight.Value;
                if (g.label != null) g.label.gameObject.SetActive(Labels.Value);

                if (Highlight.Value)
                {
                    var rc = g.ringColor; rc.a = pulse;          g.ring.color = rc;
                    var hc = g.haloColor; hc.a = 0.5f * pulse;   g.halo.color = hc;
                }
            }
        }

        private Glow Create(Ship ship, int i)
        {
            Color color = PlayerColor(ship, i);

            var root = new GameObject($"PunkHighlight_P{i + 1}");
            root.transform.position = ship.transform.position;

            // sorting taken from the ship so the glow sits just above the hull
            var ssr = ship.GetComponentInChildren<SpriteRenderer>();
            int layer = ssr != null ? ssr.sortingLayerID : 0;
            int order = ssr != null ? ssr.sortingOrder : 0;

            var halo = NewSprite(root.transform, _halo, layer, order + 50);
            var haloColor = new Color(color.r, color.g, color.b, 0.5f);
            halo.color = haloColor;

            var ring = NewSprite(root.transform, _ring, layer, order + 51);
            var ringColor = Color.Lerp(color, Color.white, 0.5f); ringColor.a = 1f;   // white-hot neon core
            ring.color = ringColor;

            TextMeshPro label = null;
            var font = Font();
            if (font != null)
            {
                var lgo = new GameObject("label");
                lgo.transform.SetParent(root.transform, false);
                lgo.transform.localPosition = new Vector3(0f, LabelY, 0f);
                label = lgo.AddComponent<TextMeshPro>();
                label.font = font;
                label.text = $"P{i + 1}";
                label.fontSize = LabelFontSize;
                label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.Bold;
                label.color = color;
                label.enableWordWrapping = false;
                if (label.transform is RectTransform rt) rt.sizeDelta = new Vector2(6f, 2f);
                // per-instance material so the black outline doesn't bleed into the shared font
                try { label.fontMaterial = new Material(label.font.material); label.outlineColor = Color.black; label.outlineWidth = 0.2f; } catch { }
                var mr = label.GetComponent<MeshRenderer>();
                if (mr != null) { mr.sortingLayerID = layer; mr.sortingOrder = order + 52; }
            }

            return new Glow { root = root, halo = halo, ring = ring, label = label, ringColor = ringColor, haloColor = haloColor };
        }

        private static SpriteRenderer NewSprite(Transform parent, Sprite sprite, int layerId, int order)
        {
            var go = new GameObject("glow");
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingLayerID = layerId;
            sr.sortingOrder = order;
            return sr;
        }

        // Read the ship's ACTUAL applied color (its themed sprite tint) so the highlight matches any
        // player color — including extra players added by other mods — with no dependency on them.
        private static readonly System.Reflection.FieldInfo SpriteRenderersF =
            typeof(ApplyShipTheme).GetField("spriteRenderers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static Color PlayerColor(Ship ship, int i)
        {
            try
            {
                var ast = ship.GetComponent<ApplyShipTheme>();
                if (ast != null && SpriteRenderersF?.GetValue(ast) is SpriteRenderer[] srs && srs.Length > 0 && srs[0] != null)
                {
                    var c = srs[0].color; c.a = 1f; return c;
                }
            }
            catch { }
            // fallback palette (matches the join screen) if the color can't be read yet
            switch (i)
            {
                case 0:  return new Color(1f, 0.85f, 0.3f);
                case 1:  return new Color(0.35f, 0.7f, 1f);
                case 2:  return new Color(0.49f, 0.86f, 0.49f);
                default: return new Color(0.78f, 0.55f, 0.92f);
            }
        }

        private void HideAll()
        {
            foreach (var kv in _glows)
                if (kv.Value?.root != null && kv.Value.root.activeSelf) kv.Value.root.SetActive(false);
        }

        private void OnDestroy()
        {
            foreach (var kv in _glows) if (kv.Value?.root != null) Destroy(kv.Value.root);
            _glows.Clear();
        }

        private static void EnsureAssets()
        {
            if (_ring == null) _ring = SpriteFrom(GaussRing(Tex, 0.62f, 0.04f, 1.0f));   // crisp bright core
            if (_halo == null) _halo = SpriteFrom(GaussRing(Tex, 0.62f, 0.20f, 0.6f));   // soft wide glow
        }

        private static TMP_FontAsset Font()
        {
            if (_fontResolved) return _font;
            _fontResolved = true;
            _font = TMP_Settings.defaultFontAsset;
            if (_font == null) _font = Resources.FindObjectsOfTypeAll<TMP_FontAsset>().FirstOrDefault();
            return _font;
        }

        private static Sprite SpriteFrom(Texture2D t)
            => Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), Ppu);

        // A soft gaussian ring (transparent center + edges) — tinted at runtime for the neon look.
        private static Texture2D GaussRing(int size, float radius01, float width01, float peak)
        {
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { wrapMode = TextureWrapMode.Clamp };
            float c = (size - 1) * 0.5f;
            float rPix = radius01 * (size * 0.5f);
            float wPix = Mathf.Max(0.5f, width01 * (size * 0.5f));
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = x - c, dy = y - c;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = (d - rPix) / wPix;
                    float a = Mathf.Clamp01(Mathf.Exp(-t * t) * peak);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return tex;
        }
    }
}
