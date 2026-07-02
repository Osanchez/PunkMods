using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PunkDevReload
{
    /// <summary>
    /// DEV-ONLY live reloader (a small in-repo stand-in for BepInEx's ScriptEngine, so there's no
    /// external download). It watches <c>BepInEx/scripts/</c>: any plugin DLL you drop there is loaded
    /// on startup and re-loadable in-game with a hotkey (default F10), WITHOUT restarting the game.
    ///
    /// Workflow: `build-package.ps1 -HotReload &lt;Mod&gt;` builds that mod into scripts/ (and removes it
    /// from plugins/ so it isn't loaded twice). Rebuild, press the reload key, see your change.
    ///
    /// How it works: DLLs are loaded from BYTES (never file-locked, so you can rebuild while the game
    /// runs), so each reload is a fresh assembly (Mono can't truly unload the old one — memory grows
    /// slowly over a long session; restart occasionally). On reload the previously loaded plugin hosts
    /// are Destroy()ed first, firing each plugin's OnDestroy teardown (Harmony UnpatchSelf + cleanup),
    /// then the new build's BaseUnityPlugin types are instantiated (firing Awake). If a matching .pdb
    /// sits next to the DLL it's loaded too, so a debugger can step through the reloaded code.
    ///
    /// This mod is intentionally EXCLUDED from distribution (build-package.ps1) — it only lives on a
    /// developer's local install.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.devreload";
        public const string Name = "PUNK Dev Reload";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        private string _scriptsDir;
        private ConfigEntry<Key> _reloadKey;
        private readonly List<GameObject> _hosts = new List<GameObject>();   // one per loaded plugin instance

        private void Awake()
        {
            Log = Logger;
            _reloadKey = Config.Bind("General", "ReloadKey", Key.F10,
                "Key that reloads every plugin DLL in BepInEx/scripts/. (F10 avoids the other mods' hotkeys.)");
            _scriptsDir = Path.Combine(Paths.BepInExRootPath, "scripts");
            try { Directory.CreateDirectory(_scriptsDir); } catch { }

            Log.LogInfo($"{Name} v{Version} loaded. Watching '{_scriptsDir}'. Press {_reloadKey.Value} to reload.");
            ReloadAll();   // pick up anything already staged in scripts/ at launch
        }

        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            try { if (kb[_reloadKey.Value].wasPressedThisFrame) ReloadAll(); } catch { }
        }

        private void OnDestroy() => DestroyHosts();

        // Destroy the previously loaded plugin instances (fires their OnDestroy teardown), then load and
        // instantiate every plugin DLL currently in scripts/.
        private void ReloadAll()
        {
            DestroyHosts();

            string[] dlls;
            try { dlls = Directory.GetFiles(_scriptsDir, "*.dll"); }
            catch (Exception e) { Log.LogWarning($"[devreload] cannot read scripts dir: {e.Message}"); return; }

            if (dlls.Length == 0) { Log.LogInfo("[devreload] scripts/ is empty - nothing to reload."); return; }

            int loaded = 0;
            foreach (var path in dlls)
            {
                try { loaded += LoadDll(path); }
                catch (Exception e) { Log.LogWarning($"[devreload] failed to load {Path.GetFileName(path)}: {e}"); }
            }
            Log.LogInfo($"[devreload] reloaded {loaded} plugin(s) from {dlls.Length} DLL(s) in scripts/.");
        }

        // Load one DLL (from bytes, + .pdb if present) and instantiate each BaseUnityPlugin it defines
        // on its own DontDestroyOnLoad host GameObject. Returns how many plugins were instantiated.
        private int LoadDll(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var pdb = Path.ChangeExtension(path, ".pdb");
            Assembly asm = File.Exists(pdb)
                ? Assembly.Load(bytes, File.ReadAllBytes(pdb))   // symbols -> debugger can step into it
                : Assembly.Load(bytes);

            Type[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }

            var pluginTypes = types.Where(t => t != null && !t.IsAbstract && typeof(BaseUnityPlugin).IsAssignableFrom(t)).ToList();
            if (pluginTypes.Count == 0)
            {
                Log.LogWarning($"[devreload] {Path.GetFileName(path)} has no BaseUnityPlugin type - skipped.");
                return 0;
            }

            int n = 0;
            foreach (var t in pluginTypes)
            {
                try
                {
                    var host = new GameObject($"DevReload::{t.Name}");
                    UnityEngine.Object.DontDestroyOnLoad(host);
                    host.AddComponent(t);          // BaseUnityPlugin ctor + Awake run here
                    _hosts.Add(host);
                    n++;
                    Log.LogInfo($"[devreload] loaded {t.FullName}");
                }
                catch (Exception e) { Log.LogWarning($"[devreload] {t.FullName} failed in Awake: {e}"); }
            }
            return n;
        }

        private void DestroyHosts()
        {
            foreach (var go in _hosts)
                if (go != null) { try { UnityEngine.Object.Destroy(go); } catch { } }
            _hosts.Clear();
        }
    }
}
