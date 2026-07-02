using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PunkModsMenu
{
    /// <summary>
    /// Public registration API — a lightweight framework other mods hook into. Mods call these
    /// (via reflection, so there's no hard dependency: if this plugin isn't installed the calls
    /// simply don't happen and the mod degrades gracefully). Entries are registered at plugin
    /// Awake; the MODS tab is built from them when the Settings screen first opens.
    ///
    /// Each entry remembers which mod registered it (derived from the call stack), so the tab can
    /// group rows under a per-mod section header. The tab is a scrollable list that grows to fit
    /// however many rows are registered.
    /// </summary>
    public static class ModMenu
    {
        public enum Kind { Toggle, ConfirmButton, Action, List }

        public sealed class Entry
        {
            public Kind kind;
            public Assembly owner;        // the mod assembly that registered this row (for teardown on hot-reload)
            public string label;
            public string section;        // grouping key: the mod's (prettified) name
            public string sectionLabel;   // display: "NAME By Author (vVersion)"
            public string sectionDescription;   // mod blurb shown as a tooltip
            // toggle
            public Func<bool> get;
            public Action<bool> set;
            // button / action
            public string neutralLabel;   // the "do nothing" option (e.g. "KEEP")
            public string actionLabel;    // the action option (e.g. "CLEAR PROGRESS")
            public Action action;
            public bool confirm;
            public string confirmText;
            // list (flippable selector)
            public Func<List<string>> options;
            public Func<int> getIndex;
            public Action<int> setIndex;
        }

        internal static readonly List<Entry> Entries = new List<Entry>();

        /// <summary>A row with an OFF/ON selector that applies immediately.</summary>
        public static void AddToggle(string label, Func<bool> get, Action<bool> set)
        { var a = CallerAssembly(); var s = a != null ? InfoFor(a) : null; Entries.Add(new Entry { kind = Kind.Toggle, owner = a, label = label, get = get, set = set, section = s?.Key, sectionLabel = s?.Label, sectionDescription = s?.Description }); }

        /// <summary>A row with a neutral option and an action option; the action fires (optionally
        /// with a confirm prompt) when you exit the Settings screen with the action option selected.</summary>
        public static void AddButton(string label, string neutralLabel, string actionLabel, Action action, bool confirm, string confirmText)
        { var a = CallerAssembly(); var s = a != null ? InfoFor(a) : null; Entries.Add(new Entry { kind = Kind.ConfirmButton, owner = a, label = label, neutralLabel = neutralLabel, actionLabel = actionLabel, action = action, confirm = confirm, confirmText = confirmText, section = s?.Key, sectionLabel = s?.Label, sectionDescription = s?.Description }); }

        /// <summary>A row whose action fires IMMEDIATELY when its action option is selected (then
        /// refreshes list rows). Use for create/delete that should update the UI live.</summary>
        public static void AddAction(string label, string actionLabel, Action action)
        { var a = CallerAssembly(); var s = a != null ? InfoFor(a) : null; Entries.Add(new Entry { kind = Kind.Action, owner = a, label = label, neutralLabel = "—", actionLabel = actionLabel, action = action, section = s?.Key, sectionLabel = s?.Label, sectionDescription = s?.Description }); }

        /// <summary>A flippable selector row (◄ value ►). <paramref name="options"/> is read live each
        /// time it flips/refreshes, so dynamic lists (e.g. profiles) stay current.</summary>
        public static void AddList(string label, Func<List<string>> options, Func<int> getIndex, Action<int> setIndex)
        { var a = CallerAssembly(); var s = a != null ? InfoFor(a) : null; Entries.Add(new Entry { kind = Kind.List, owner = a, label = label, options = options, getIndex = getIndex, setIndex = setIndex, section = s?.Key, sectionLabel = s?.Label, sectionDescription = s?.Description }); }

        /// <summary>Teardown hook for hot-reload: a mod calls this from its plugin's OnDestroy (via its
        /// reflection bridge) to drop the rows it registered, so a live reload doesn't stack duplicates.
        /// Uses the same call-stack derivation as registration to identify the caller's assembly.</summary>
        public static void RemoveByCaller()
        {
            var a = CallerAssembly();
            if (a == null) return;
            Entries.RemoveAll(e => e.owner == a);
        }

        // ---- section (source mod) derivation ----

        private sealed class SectionInfo { public string Key; public string Label; public string Description; }
        private static readonly Dictionary<Assembly, SectionInfo> _sections = new Dictionary<Assembly, SectionInfo>();

        // Walk the call stack to the first frame outside this framework (and outside system/Unity/
        // BepInEx/Harmony) — that's the mod that registered the row. The mod calls us through its own
        // reflection bridge, so its assembly is on the stack even though the call is reflective.
        private static Assembly CallerAssembly()
        {
            try
            {
                var self = typeof(ModMenu).Assembly;
                var st = new System.Diagnostics.StackTrace(1, false);
                for (int i = 0; i < st.FrameCount; i++)
                {
                    var asm = st.GetFrame(i)?.GetMethod()?.DeclaringType?.Assembly;
                    if (asm == null || asm == self) continue;
                    var n = asm.GetName().Name;
                    if (n.StartsWith("System") || n.StartsWith("mscorlib") || n.StartsWith("netstandard")
                        || n.StartsWith("Mono") || n == "0Harmony" || n.StartsWith("Unity") || n.StartsWith("BepInEx"))
                        continue;
                    return asm;
                }
            }
            catch { }
            return null;
        }

        // Resolve a mod's label metadata. Prefers a `mod.yaml` next to the mod's DLL (so the labels
        // are editable post-build), then falls back to [BepInPlugin] (name/version) and the
        // AssemblyCompany attribute (author).
        private static SectionInfo InfoFor(Assembly asm)
        {
            if (_sections.TryGetValue(asm, out var cached)) return cached;

            string name = null, ver = null, author = null, desc = null;
            ReadYaml(asm, ref name, ref author, ref ver, ref desc);   // mod-folder file wins

            if (name == null || ver == null)
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        var a = (BepInPlugin)Attribute.GetCustomAttribute(t, typeof(BepInPlugin));
                        if (a != null) { name = name ?? a.Name; ver = ver ?? a.Version?.ToString(); break; }
                    }
                }
                catch { }
            }
            if (author == null)
            {
                try { author = (Attribute.GetCustomAttribute(asm, typeof(AssemblyCompanyAttribute)) as AssemblyCompanyAttribute)?.Company; }
                catch { }
                if (!string.IsNullOrEmpty(author) && author == asm.GetName().Name) author = null;   // unset default
            }

            string key = Prettify(name ?? asm.GetName().Name);
            var info = new SectionInfo { Key = key, Label = BuildLabel(key, author, ver), Description = desc };
            _sections[asm] = info;
            return info;
        }

        // Minimal key:value reader for `mod.yaml` (or mod.yml) in the mod's own folder.
        private static void ReadYaml(Assembly asm, ref string name, ref string author, ref string ver, ref string desc)
        {
            try
            {
                var loc = asm.Location;
                if (string.IsNullOrEmpty(loc)) return;
                var dir = System.IO.Path.GetDirectoryName(loc);
                string path = System.IO.Path.Combine(dir, "mod.yaml");
                if (!System.IO.File.Exists(path)) path = System.IO.Path.Combine(dir, "mod.yml");
                if (!System.IO.File.Exists(path)) return;

                foreach (var raw in System.IO.File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int c = line.IndexOf(':');
                    if (c <= 0) continue;
                    string k = line.Substring(0, c).Trim().ToLowerInvariant();
                    string v = line.Substring(c + 1).Trim().Trim('"', '\'');
                    if (v.Length == 0) continue;
                    if (k == "name") name = v;
                    else if (k == "author" || k == "by") author = v;
                    else if (k == "version" || k == "v") ver = v;
                    else if (k == "description" || k == "desc") desc = v;
                }
            }
            catch { }
        }

        private static string BuildLabel(string name, string author, string ver)
        {
            string s = name.ToUpper();
            if (!string.IsNullOrEmpty(author)) s += $"  By {author}";
            if (!string.IsNullOrEmpty(ver)) s += $" (v{ver})";
            return s;
        }

        private static string Prettify(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Trim();
            if (s.StartsWith("PUNK ", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5).Trim();
            int p = s.IndexOf('(');
            if (p > 0) s = s.Substring(0, p);
            return s.Trim();
        }
    }

    /// <summary>Custom flipper component dropped onto a cloned 2-button row: ◄ / ► cycle a dynamic
    /// option list, with the current "label    value" shown in the row's name text.</summary>
    public class ModListRow : OptionsMenuitemBase
    {
        internal Func<List<string>> options;
        internal Func<int> getIndex;
        internal Action<int> setIndex;
        internal string rowLabel;
        internal Transform itemName;
        internal Action onChanged;     // refresh sibling rows (e.g. assigning a profile frees it elsewhere)

        public override void HandleLeft() => Step(-1);
        public override void HandleRight() => Step(1);

        private void Step(int dir)
        {
            try
            {
                var opts = options?.Invoke();
                if (opts == null || opts.Count == 0) return;
                int cur = Mathf.Clamp(getIndex?.Invoke() ?? 0, 0, opts.Count - 1);
                int next = ((cur + dir) % opts.Count + opts.Count) % opts.Count;
                setIndex?.Invoke(next);
                Refresh();
                onChanged?.Invoke();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"list '{rowLabel}' step failed: {e.Message}"); }
        }

        internal void Refresh()
        {
            try
            {
                var opts = options?.Invoke();
                string val = (opts != null && opts.Count > 0)
                    ? opts[Mathf.Clamp(getIndex?.Invoke() ?? 0, 0, opts.Count - 1)] : "—";
                OptionsScreenInjector.SetText(itemName, $"{rowLabel}     {val}");
            }
            catch { }
        }
    }

    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.modsmenu";
        public const string Name = "PUNK Mods Menu (framework)";
        public const string Version = "2.1.0";

        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} v{Version} loaded.");
        }

        // Hot-reload teardown: drop this framework's Harmony patches (tab injection / nav / close guard).
        // We deliberately do NOT clear ModMenu.Entries: it's a SHARED registry that other, still-loaded
        // mods have registered rows into, and each mod removes its own rows from its own OnDestroy
        // (ModMenu.RemoveByCaller). Wiping it here would erase rows for mods that aren't reloading.
        private void OnDestroy()
        {
            try { _harmony?.UnpatchSelf(); } catch (Exception e) { Log?.LogWarning($"unpatch failed: {e.Message}"); }
        }
    }

    internal sealed class ItemBinding
    {
        public OptionsMenuitemBase item;   // OptionsMenuItemButtons for toggle/button/action; ModListRow for list
        public ModMenu.Entry entry;
        public ModListRow list;            // non-null only for list rows
    }

    /// <summary>Concrete tab hosting the registered rows in a scroll view.</summary>
    public class ModsOptionsTab : OptionsTab
    {
        internal readonly List<ItemBinding> bindings = new List<ItemBinding>();

        // Scroll plumbing (set during injection). Auto-scroll keeps the selected row in view.
        internal ScrollRect scroll;
        internal RectTransform viewport;
        internal RectTransform content;
        internal float[] navTops;          // y-offset (from content top, downward) of each nav item
        internal float rowHeight = 72f;
        internal TMPro.TMP_Text tooltip;   // shows the selected row's mod description

        internal static bool SuppressApply = true;
        private static readonly FieldInfo SelectionF = AccessTools.Field(typeof(OptionsMenuItemButtons), "selection");

        private void OnDisable() => SuppressApply = true;

        internal void RefreshLists()
        {
            foreach (var b in bindings) b.list?.Refresh();
        }

        internal void ScrollToIndex(int i)
        {
            try
            {
                if (scroll == null || content == null || viewport == null || navTops == null) return;
                if (i < 0 || i >= navTops.Length) return;
                float viewH = viewport.rect.height;
                float totalH = content.rect.height;
                if (viewH <= 1f || totalH <= viewH) { content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f); return; }

                float top = navTops[i], bottom = top + rowHeight;
                float s = content.anchoredPosition.y;
                if (top < s) s = top;
                else if (bottom > s + viewH) s = bottom - viewH;
                s = Mathf.Clamp(s, 0f, totalH - viewH);
                content.anchoredPosition = new Vector2(content.anchoredPosition.x, s);
            }
            catch { }
        }

        internal void UpdateTooltip(int i)
        {
            if (tooltip == null) return;
            string d = (i >= 0 && i < bindings.Count) ? bindings[i].entry?.sectionDescription : null;
            tooltip.text = string.IsNullOrEmpty(d)
                ? ""
                : $"<size=80%><b><color=#8a8a92>DESCRIPTION</color></b></size>\n<color=#b8b8c0>{d}</color>";
        }

        protected override void OnOpened()
        {
            SuppressApply = true;
            try
            {
                foreach (var b in bindings)
                {
                    if (b.entry.kind == ModMenu.Kind.List) { b.list?.Refresh(); continue; }
                    var omib = b.item as OptionsMenuItemButtons;
                    if (omib == null) continue;
                    int target = (b.entry.kind == ModMenu.Kind.Toggle && b.entry.get != null && b.entry.get()) ? 1 : 0;
                    try { SelectionF.SetValue(omib, -1); omib.SetSelection(target); } catch { }
                }
                if (content != null) content.anchoredPosition = new Vector2(content.anchoredPosition.x, 0f);
            }
            finally { SuppressApply = false; }
        }

        protected override void OnClosed() { }
    }

    [HarmonyPatch(typeof(OptionsScreen), "Awake")]
    internal static class OptionsScreenInjector
    {
        private static readonly HashSet<int> _done = new HashSet<int>();
        private static readonly FieldInfo ButtonsF = AccessTools.Field(typeof(OptionsMenuItemButtons), "buttons");
        private static readonly FieldInfo AnimatorF = AccessTools.Field(typeof(OptionsMenuitemBase), "animator");
        private static readonly FieldInfo HintsF = AccessTools.Field(typeof(OptionsMenuitemBase), "gamepadHints");

        private const float SepHeight = 30f;

        private static void Postfix(OptionsScreen __instance)
        {
            try
            {
                if (ModMenu.Entries.Count == 0) return;   // nothing registered -> no tab

                int id = __instance.GetInstanceID();
                if (_done.Contains(id)) return;

                var tabsF = AccessTools.Field(typeof(OptionsScreen), "tabs");
                var imgsF = AccessTools.Field(typeof(OptionsScreen), "tabButtonImages");
                var tabs = tabsF.GetValue(__instance) as OptionsTab[];
                var imgs = imgsF.GetValue(__instance) as Image[];
                if (tabs == null || imgs == null || tabs.Length == 0 || imgs.Length == 0) return;
                if (tabs.Length >= 4) { _done.Add(id); return; }
                int newIndex = tabs.Length;

                // ---- tab content: clone tab[0], strip to a single 2-button item TEMPLATE ----
                var srcTab = tabs[0];
                var tabGO = UnityEngine.Object.Instantiate(srcTab.gameObject, srcTab.transform.parent);
                tabGO.name = "MODS";
                foreach (var ot in tabGO.GetComponents<OptionsTab>())
                    UnityEngine.Object.DestroyImmediate(ot);

                var existing = Children(tabGO.transform);
                OptionsMenuItemButtons template = null;
                foreach (var c in existing)
                {
                    var omib = c.GetComponent<OptionsMenuItemButtons>();
                    if (omib != null && c.gameObject.activeSelf && ButtonCount(omib) == 2) { template = omib; break; }
                }
                if (template == null) { UnityEngine.Object.Destroy(tabGO); Plugin.Log.LogWarning("No 2-button item template; MODS tab skipped."); return; }

                var modsTab = tabGO.AddComponent<ModsOptionsTab>();
                modsTab.rowHeight = RowHeight(template);

                // ---- build the scroll view; rows go into `content` ----
                Transform content = BuildScroll(tabGO, modsTab, out float spacing, out float padTop, out float padBottom);

                // ---- rows grouped by mod, sections sorted alphabetically (unknown/empty last) ----
                var navTops = new List<float>();
                float cursorY = padTop;

                var groups = ModMenu.Entries
                    .GroupBy(e => e.section ?? "")
                    .OrderBy(g => string.IsNullOrEmpty(g.Key) ? 1 : 0)
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var g in groups)
                {
                    if (!string.IsNullOrEmpty(g.Key))
                    {
                        MakeSeparator(content, template, g.First().sectionLabel ?? g.Key);
                        cursorY += SepHeight + spacing;
                    }

                    foreach (var e in g)
                    {
                        var go = UnityEngine.Object.Instantiate(template.gameObject, content);
                        var omib = go.GetComponent<OptionsMenuItemButtons>();
                        var binding = new ItemBinding { entry = e };
                        if (e.kind == ModMenu.Kind.List)
                        {
                            var row = MakeListRow(modsTab, go, omib, e);
                            binding.item = row;
                            binding.list = row;
                        }
                        else
                        {
                            ConfigureButtonsRow(modsTab, omib, e);
                            binding.item = omib;
                        }
                        modsTab.bindings.Add(binding);
                        navTops.Add(cursorY);
                        cursorY += modsTab.rowHeight + spacing;
                    }
                }
                modsTab.navTops = navTops.ToArray();
                modsTab.tooltip = MakeTooltip(tabGO, template);   // bottom band; updated on selection

                foreach (var c in existing) UnityEngine.Object.DestroyImmediate(c.gameObject);   // drop the original (gameplay) rows

                AccessTools.Field(typeof(OptionsTab), "items").SetValue(modsTab, modsTab.bindings.Select(b => (OptionsMenuitemBase)b.item).ToArray());
                tabGO.SetActive(false);

                // ---- tab button ----
                var srcBtn = imgs[imgs.Length - 1].transform.parent;
                var btnGO = UnityEngine.Object.Instantiate(srcBtn.gameObject, srcBtn.parent);
                btnGO.name = "ModsButton";
                btnGO.transform.SetSiblingIndex(srcBtn.GetSiblingIndex() + 1);

                // The tab buttons are positioned by fixed anchoredPositions (no layout group), so the
                // clone lands exactly on top of the last (AUDIO) button. Shift it right by the spacing
                // between the two existing buttons so it sits after AUDIO instead of overlapping it.
                var newBtnRT = btnGO.GetComponent<RectTransform>();
                var lastBtnRT = srcBtn as RectTransform;
                if (newBtnRT != null && lastBtnRT != null && imgs.Length >= 2)
                {
                    var prevBtnRT = imgs[imgs.Length - 2].transform.parent as RectTransform;
                    if (prevBtnRT != null)
                    {
                        Vector2 step = lastBtnRT.anchoredPosition - prevBtnRT.anchoredPosition;
                        newBtnRT.anchoredPosition = lastBtnRT.anchoredPosition + step;
                    }
                }

                SetText(FindByName(btnGO.transform, "Text (TMP)"), "MODS");
                var modsImg = btnGO.transform.Find("ButtonBody").GetComponent<Image>();
                var punk = btnGO.GetComponent<PunkButton>();
                if (punk != null)
                {
                    var oc = punk.OnClick;
                    for (int i = oc.GetPersistentEventCount() - 1; i >= 0; i--) oc.SetPersistentListenerState(i, UnityEventCallState.Off);
                    oc.AddListener(() => __instance.ShowTab(newIndex));
                }

                var newTabs = new OptionsTab[tabs.Length + 1]; tabs.CopyTo(newTabs, 0); newTabs[tabs.Length] = modsTab;
                var newImgs = new Image[imgs.Length + 1]; imgs.CopyTo(newImgs, 0); newImgs[imgs.Length] = modsImg;
                tabsF.SetValue(__instance, newTabs);
                imgsF.SetValue(__instance, newImgs);

                _done.Add(id);
                Plugin.Log.LogInfo($"Injected MODS tab with {ModMenu.Entries.Count} row(s).");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"MODS tab injection failed (vanilla settings unaffected): {e}"); }
        }

        // Wrap the tab content in a ScrollRect + masked viewport + auto-sizing content holder so the
        // list scrolls and grows to fit any number of rows. Returns the content transform to fill.
        private static Transform BuildScroll(GameObject tabGO, ModsOptionsTab tab, out float spacing, out float padTop, out float padBottom)
        {
            spacing = 12f; padTop = 16f; padBottom = 16f;
            var srcVlg = tabGO.GetComponent<VerticalLayoutGroup>();

            var viewportGO = new GameObject("ModsViewport", typeof(RectTransform));
            var vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.SetParent(tabGO.transform, false);
            vpRT.anchorMin = Vector2.zero; vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = new Vector2(0f, 112f);   // leave a band at the bottom for the description tooltip
            vpRT.offsetMax = Vector2.zero;
            viewportGO.AddComponent<RectMask2D>();
            // Invisible graphic so clicks in the empty parts of the list are consumed here and don't
            // fall through to whatever screen is behind the Settings panel.
            var vpBlocker = viewportGO.AddComponent<Image>();
            vpBlocker.color = new Color(0f, 0f, 0f, 0f);
            vpBlocker.raycastTarget = true;

            var contentGO = new GameObject("ModsContent", typeof(RectTransform));
            var cRT = contentGO.GetComponent<RectTransform>();
            cRT.SetParent(vpRT, false);
            cRT.anchorMin = new Vector2(0f, 1f); cRT.anchorMax = new Vector2(1f, 1f); cRT.pivot = new Vector2(0.5f, 1f);
            cRT.anchoredPosition = Vector2.zero; cRT.sizeDelta = Vector2.zero;

            var vlg = contentGO.AddComponent<VerticalLayoutGroup>();
            if (srcVlg != null)
            {
                spacing = srcVlg.spacing;
                padTop = srcVlg.padding.top; padBottom = srcVlg.padding.bottom;
                vlg.padding = new RectOffset(srcVlg.padding.left, srcVlg.padding.right, srcVlg.padding.top, srcVlg.padding.bottom);
                vlg.spacing = srcVlg.spacing;
                vlg.childAlignment = srcVlg.childAlignment;
                vlg.childControlHeight = srcVlg.childControlHeight;
                vlg.childControlWidth = srcVlg.childControlWidth;
                vlg.childForceExpandHeight = srcVlg.childForceExpandHeight;
                vlg.childForceExpandWidth = srcVlg.childForceExpandWidth;
                UnityEngine.Object.DestroyImmediate(srcVlg);   // don't let the old group fight the scroll content
            }
            else
            {
                vlg.spacing = spacing;
                vlg.padding = new RectOffset(0, 0, (int)padTop, (int)padBottom);
                vlg.childAlignment = TextAnchor.UpperCenter;
                vlg.childControlHeight = true; vlg.childControlWidth = true;
                vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;
            }

            // Extra breathing room up top so the first section header isn't tucked under the tab bar.
            padTop += 44f;
            vlg.padding = new RectOffset(vlg.padding.left, vlg.padding.right, (int)padTop, vlg.padding.bottom);

            var fitter = contentGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var sr = viewportGO.AddComponent<ScrollRect>();
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 40f;
            sr.viewport = vpRT; sr.content = cRT;

            tab.scroll = sr; tab.viewport = vpRT; tab.content = cRT;
            return contentGO.transform;
        }

        private static float RowHeight(OptionsMenuItemButtons template)
        {
            try
            {
                var le = template.GetComponent<LayoutElement>();
                if (le != null && le.preferredHeight > 1f) return le.preferredHeight;
                var rt = template.GetComponent<RectTransform>();
                if (rt != null && rt.rect.height > 1f) return rt.rect.height;
            }
            catch { }
            return 72f;
        }

        // A non-interactive header row. Cloned from the FULL row template (so it lines up with the
        // real rows), with its buttons stripped and its label set to a dim title + dash rule. Not
        // added to the nav items, so selection skips it.
        private static void MakeSeparator(Transform content, OptionsMenuItemButtons template, string title)
        {
            try
            {
                var go = UnityEngine.Object.Instantiate(template.gameObject, content);
                go.name = "ModsSeparator";

                var omib = go.GetComponent<OptionsMenuItemButtons>();
                if (omib != null)
                {
                    var btns = ButtonsF.GetValue(omib) as Array;
                    if (btns != null)
                        for (int i = 0; i < btns.Length; i++)
                            (btns.GetValue(i) as Component)?.gameObject.SetActive(false);
                    UnityEngine.Object.DestroyImmediate(omib);   // not interactive
                }

                var itemName = go.transform.Find("Visual/ItemName");
                SetText(itemName, $"<size=80%><color=#9a9aa2>{title}</color>   <color=#50505a>──────</color></size>");
                SetAlign(itemName, false);

                var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
                le.minHeight = SepHeight; le.preferredHeight = SepHeight; le.flexibleHeight = 0f;
                var rt = go.GetComponent<RectTransform>();
                if (rt != null) rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, SepHeight);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"separator '{title}' failed: {e.Message}"); }
        }

        // A wrapping description line pinned to the bottom of the tab, updated as the selection moves.
        private static TMPro.TMP_Text MakeTooltip(GameObject tabGO, OptionsMenuItemButtons template)
        {
            try
            {
                var font = FindByName(template.transform, "ItemName")?.GetComponent<TMPro.TMP_Text>()?.font;
                var go = new GameObject("ModTooltip", typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                rt.SetParent(tabGO.transform, false);
                rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(48f, 4f); rt.offsetMax = new Vector2(-48f, 78f);   // sits lower, clear of the last row
                var t = go.AddComponent<TMPro.TextMeshProUGUI>();
                if (font != null) t.font = font;
                t.fontSize = 18; t.richText = true; t.enableWordWrapping = true;
                t.alignment = TMPro.TextAlignmentOptions.Top;
                t.color = new Color(0.66f, 0.66f, 0.72f, 1f);
                t.text = "";
                return t;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"tooltip create failed: {e.Message}"); return null; }
        }

        private static void ConfigureButtonsRow(ModsOptionsTab tab, OptionsMenuItemButtons item, ModMenu.Entry e)
        {
            SetText(item.transform.Find("Visual/ItemName"), e.label);
            var btns = ButtonsF.GetValue(item) as Array;
            if (btns == null || btns.Length < 2) return;
            var b0 = (btns.GetValue(0) as Component).transform;
            var b1 = (btns.GetValue(1) as Component).transform;

            switch (e.kind)
            {
                case ModMenu.Kind.Toggle:
                    SetButtonLabel(b0, "OFF");
                    SetButtonLabel(b1, "ON");
                    item.SelectionChanged += idx =>
                    {
                        if (ModsOptionsTab.SuppressApply) return;
                        try { e.set?.Invoke(idx == 1); } catch (Exception ex) { Plugin.Log.LogWarning($"toggle '{e.label}' failed: {ex.Message}"); }
                    };
                    break;

                case ModMenu.Kind.Action:
                    // Single visible button: hide the neutral option, keep the 2-button selection model
                    // intact (clicking the action button is index 1, which the handler fires on).
                    b0.gameObject.SetActive(false);
                    SetButtonLabel(b1, e.actionLabel);
                    try
                    {
                        var ev = b1.GetComponent<PunkButton>()?.OnClick;
                        if (ev != null)
                            for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                                Plugin.Log.LogInfo($"[diag] '{e.label}' b1 listener {i}: {ev.GetPersistentTarget(i)?.GetType().Name}.{ev.GetPersistentMethodName(i)}");
                    }
                    catch { }
                    item.SelectionChanged += idx =>
                    {
                        Plugin.Log.LogInfo($"[diag] '{e.label}' selectionChanged idx={idx} suppress={ModsOptionsTab.SuppressApply}");
                        if (ModsOptionsTab.SuppressApply || idx != 1) return;
                        try { e.action?.Invoke(); } catch (Exception ex) { Plugin.Log.LogWarning($"action '{e.label}' failed: {ex.Message}"); }
                        try { item.SetSelection(0); } catch { }
                        tab.RefreshLists();
                    };
                    break;

                default: // ConfirmButton — fires from the close guard when option 1 is selected on exit
                    SetButtonLabel(b0, string.IsNullOrEmpty(e.neutralLabel) ? "—" : e.neutralLabel);
                    SetButtonLabel(b1, e.actionLabel);
                    break;
            }
        }

        // Turn a cloned 2-button row into a flipper: ◄ / ► buttons + a custom ModListRow component.
        private static ModListRow MakeListRow(ModsOptionsTab tab, GameObject go, OptionsMenuItemButtons omib, ModMenu.Entry e)
        {
            var itemName = go.transform.Find("Visual/ItemName");
            var btns = ButtonsF.GetValue(omib) as Array;
            PunkButton pb0 = null, pb1 = null;
            if (btns != null && btns.Length >= 2)
            {
                var b0 = (btns.GetValue(0) as Component).transform;
                var b1 = (btns.GetValue(1) as Component).transform;
                SetButtonLabel(b0, "<");
                SetButtonLabel(b1, ">");
                pb0 = b0.GetComponent<PunkButton>();
                pb1 = b1.GetComponent<PunkButton>();
            }

            // Preserve the base-class wiring (animator/hints) the highlight methods depend on.
            var animator = AnimatorF.GetValue(omib);
            var hints = HintsF.GetValue(omib);
            UnityEngine.Object.DestroyImmediate(omib);

            var row = go.AddComponent<ModListRow>();
            AnimatorF.SetValue(row, animator);
            HintsF.SetValue(row, hints);
            row.options = e.options; row.getIndex = e.getIndex; row.setIndex = e.setIndex;
            row.rowLabel = e.label; row.itemName = itemName; row.onChanged = tab.RefreshLists;

            if (pb0 != null) { ClearPersistent(pb0.OnClick); pb0.OnClick.AddListener(() => row.HandleLeft()); }
            if (pb1 != null) { ClearPersistent(pb1.OnClick); pb1.OnClick.AddListener(() => row.HandleRight()); }
            row.Refresh();
            return row;
        }

        // Set a button's label text AND force it centered — short labels like "<" / ">" were sitting
        // flush-left otherwise, which read as a misaligned scroll arrow.
        private static void SetButtonLabel(Transform button, string text)
        {
            var t = FindByName(button, "Text (TMP)");
            SetText(t, text);
            SetAlign(t, true);
        }

        private static void ClearPersistent(UnityEvent ev)
        {
            for (int i = ev.GetPersistentEventCount() - 1; i >= 0; i--) ev.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        internal static List<Transform> Children(Transform t)
        {
            var list = new List<Transform>();
            for (int i = 0; i < t.childCount; i++) list.Add(t.GetChild(i));
            return list;
        }

        internal static int ButtonCount(OptionsMenuItemButtons omib)
            => (AccessTools.Field(typeof(OptionsMenuItemButtons), "buttons").GetValue(omib) as Array)?.Length ?? 0;

        internal static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindByName(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        internal static void SetText(Transform t, string text)
        {
            if (t == null) return;
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null) continue;
                var p = c.GetType().GetProperty("text", typeof(string));
                if (p != null && p.CanWrite) { p.SetValue(c, text); return; }
            }
        }

        // Center or left-align a TMP text component (typed — reflection on the enum proved unreliable).
        // Also disables auto-sizing: a single-char label like "<" was auto-growing to fill the button,
        // which made it oversized and top-heavy compared to ">".
        internal static void SetAlign(Transform t, bool center)
        {
            if (t == null) return;
            var tmp = t.GetComponent<TMPro.TMP_Text>();
            if (tmp == null) return;
            tmp.enableAutoSizing = false;
            tmp.alignment = center ? TMPro.TextAlignmentOptions.Center : TMPro.TextAlignmentOptions.Left;
        }
    }

    // Keep the selected row visible when navigating with keyboard/controller.
    [HarmonyPatch(typeof(OptionsTab), "SetSelected")]
    internal static class SelectionScrollPatch
    {
        private static void Postfix(OptionsTab __instance, int index)
        {
            if (__instance is ModsOptionsTab m) { m.ScrollToIndex(index); m.UpdateTooltip(index); }
        }
    }

    // The Settings screen only navigates from its bound verticalNavigationAction (keyboard arrows +
    // left stick). The gamepad D-pad isn't bound to it, so D-pad up/down does nothing. Drive the MODS
    // tab from the D-pad directly, guarded so we don't double-step when the stick already navigated.
    [HarmonyPatch(typeof(OptionsScreen), "Update")]
    internal static class ModsDpadNav
    {
        private static readonly FieldInfo CurrentTabF = AccessTools.Field(typeof(OptionsScreen), "currentTab");
        private static readonly FieldInfo TabsF = AccessTools.Field(typeof(OptionsScreen), "tabs");
        private static readonly FieldInfo VertActF = AccessTools.Field(typeof(OptionsScreen), "verticalNavigationAction");

        private static void Postfix(OptionsScreen __instance)
        {
            try
            {
                var gp = Gamepad.current;
                if (gp == null) return;

                var tabs = TabsF.GetValue(__instance) as OptionsTab[];
                if (tabs == null) return;
                int current = (int)CurrentTabF.GetValue(__instance);
                if (current < 0 || current >= tabs.Length || !(tabs[current] is ModsOptionsTab)) return;

                // If the game's own vertical-nav action fired this frame (e.g. the stick is bound to it),
                // it already navigated — don't add a second step.
                var aref = VertActF.GetValue(__instance) as InputActionReference;
                if (aref?.action != null && aref.action.WasPerformedThisFrame()) return;

                if (gp.dpad.down.wasPressedThisFrame) tabs[current].HandleDown();
                else if (gp.dpad.up.wasPressedThisFrame) tabs[current].HandleUp();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"mods dpad nav failed: {e.Message}"); }
        }
    }

    // The base tab wraps selection (last -> first / first -> last), which on a long scrolling list
    // reads as "it jumps back to the top". Clamp the MODS tab at both ends instead; other tabs keep
    // their vanilla wrap.
    [HarmonyPatch]
    internal static class ModsClampNav
    {
        private static readonly FieldInfo SelF = AccessTools.Field(typeof(OptionsTab), "selection");
        private static readonly FieldInfo ItemsF = AccessTools.Field(typeof(OptionsTab), "items");
        private static readonly MethodInfo SetSelM = AccessTools.Method(typeof(OptionsTab), "SetSelected");

        [HarmonyPatch(typeof(OptionsTab), "HandleDown")]
        [HarmonyPrefix]
        private static bool Down(OptionsTab __instance) => Step(__instance, +1);

        [HarmonyPatch(typeof(OptionsTab), "HandleUp")]
        [HarmonyPrefix]
        private static bool Up(OptionsTab __instance) => Step(__instance, -1);

        private static bool Step(OptionsTab tab, int dir)
        {
            if (!(tab is ModsOptionsTab)) return true;   // vanilla tabs keep wrapping
            try
            {
                var items = ItemsF.GetValue(tab) as OptionsMenuitemBase[];
                if (items == null || items.Length == 0) return false;
                int sel = (int)SelF.GetValue(tab);
                int next = Mathf.Clamp(sel + dir, 0, items.Length - 1);
                if (next != sel) SetSelM.Invoke(tab, new object[] { next });
            }
            catch { return true; }
            return false;
        }
    }

    // Action rows commit on EXIT (close the Settings screen with the action option selected),
    // with an optional confirm prompt — so you only trigger them deliberately.
    [HarmonyPatch(typeof(OptionsScreen), "Close")]
    internal static class OptionsScreenCloseGuard
    {
        private static readonly FieldInfo CurrentTabF = AccessTools.Field(typeof(OptionsScreen), "currentTab");
        private static readonly FieldInfo TabsF = AccessTools.Field(typeof(OptionsScreen), "tabs");
        private static readonly FieldInfo SelectionF = AccessTools.Field(typeof(OptionsMenuItemButtons), "selection");
        private static bool _prompting;

        private static bool Prefix(OptionsScreen __instance)
        {
            if (_prompting) return false;
            try
            {
                int current = (int)CurrentTabF.GetValue(__instance);
                var tabs = TabsF.GetValue(__instance) as OptionsTab[];
                if (tabs == null || current < 0 || current >= tabs.Length) return true;
                var mods = tabs[current] as ModsOptionsTab;
                if (mods == null) return true;   // only when exiting from the MODS tab

                ItemBinding armed = null;
                foreach (var b in mods.bindings)
                {
                    if (b.entry.kind != ModMenu.Kind.ConfirmButton) continue;
                    if (b.item is OptionsMenuItemButtons omib && (int)SelectionF.GetValue(omib) == 1) { armed = b; break; }
                }
                if (armed == null) return true;

                Action finish = () =>
                {
                    _prompting = false;
                    try { (armed.item as OptionsMenuItemButtons)?.SetSelection(0); } catch { }
                    __instance.Close();
                };

                var prompt = armed.entry.confirm ? ModsActions.FindPrompt(__instance) : null;
                if (prompt != null)
                {
                    _prompting = true;
                    ModsActions.SetPromptTitle(prompt, armed.entry.confirmText ?? "Are you sure?");
                    prompt.Open(
                        positiveButtonCallback: () => { Run(armed); prompt.Close(); finish(); },
                        negativeButtonCallback: () => { prompt.Close(); finish(); });
                    return false;
                }

                Run(armed);
                try { (armed.item as OptionsMenuItemButtons)?.SetSelection(0); } catch { }
                return true;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"close-guard failed: {e.Message}"); return true; }
        }

        private static void Run(ItemBinding b)
        {
            try { b.entry.action?.Invoke(); }
            catch (Exception e) { Plugin.Log.LogWarning($"action '{b.entry.label}' failed: {e.Message}"); }
        }
    }

    // Lets the controller shoulder buttons (LB/RB) page through ALL Settings tabs, including the
    // injected MODS tab. Guarded so it won't double-step if the game already handles the bumpers.
    [HarmonyPatch(typeof(OptionsScreen), "Update")]
    internal static class BumperTabNav
    {
        private static readonly FieldInfo CurrentTabF = AccessTools.Field(typeof(OptionsScreen), "currentTab");
        private static readonly FieldInfo TabsF = AccessTools.Field(typeof(OptionsScreen), "tabs");
        private static readonly FieldInfo NextActF = AccessTools.Field(typeof(OptionsScreen), "nextTabAction");
        private static readonly FieldInfo PrevActF = AccessTools.Field(typeof(OptionsScreen), "previousTabAction");

        private static void Postfix(OptionsScreen __instance)
        {
            try
            {
                var gp = Gamepad.current;
                if (gp == null) return;
                bool rb = gp.rightShoulder.wasPressedThisFrame;
                bool lb = gp.leftShoulder.wasPressedThisFrame;
                if (!rb && !lb) return;

                // If the game already paged tabs this frame via its own actions, don't double up.
                if (Fired(NextActF, __instance) || Fired(PrevActF, __instance)) return;

                var tabs = TabsF.GetValue(__instance) as OptionsTab[];
                if (tabs == null || tabs.Length == 0) return;
                int current = (int)CurrentTabF.GetValue(__instance);
                int next = rb ? (current + 1) % tabs.Length : (current - 1 + tabs.Length) % tabs.Length;
                __instance.ShowTab(next);
            }
            catch (Exception e) { Plugin.Log.LogWarning($"bumper tab nav failed: {e.Message}"); }
        }

        private static bool Fired(FieldInfo f, OptionsScreen inst)
        {
            var aref = f.GetValue(inst) as InputActionReference;
            return aref?.action != null && aref.action.WasPerformedThisFrame();
        }
    }

    internal static class ModsActions
    {
        internal static Prompt FindPrompt(OptionsScreen screen)
        {
            var prompts = Resources.FindObjectsOfTypeAll<Prompt>().Where(p => p != null && p.gameObject.scene.IsValid()).ToList();
            return prompts.FirstOrDefault(p => p.gameObject.scene == screen.gameObject.scene) ?? prompts.FirstOrDefault();
        }

        internal static void SetPromptTitle(Prompt prompt, string text)
        {
            var titleComp = AccessTools.Field(typeof(Prompt), "title").GetValue(prompt) as Component;
            if (titleComp != null) OptionsScreenInjector.SetText(titleComp.transform, text);
        }
    }
}
