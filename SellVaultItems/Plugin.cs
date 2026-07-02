using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.Mono;            // BepInEx 6 (Mono). For BepInEx 5, remove this line.
using UnityEngine;
using UnityEngine.InputSystem;

namespace SellVaultItems
{
    /// <summary>
    /// Lets you SELL spare modules that are sitting in the Vault, for the module's base shop BUY
    /// price (the normal purchase cost from the shop's pricing config — not a discounted resale).
    ///
    /// Selling is only possible from the Vault: hover a module in the vault tab of the ship-build
    /// screen and press the gamepad NORTH face button (Y / Triangle) — or the configured keyboard
    /// key. The module is removed from the vault and its buy price is credited to the run currency
    /// (the resource the module is priced in), which is what the HUD money counter shows.
    ///
    /// No Harmony patches — the mod only READS game state (the vault widget's hovered/selected
    /// module) each frame and, on the button press, calls the game's own Vault.Remove + credits a
    /// resource tank. Everything is guarded so a signature mismatch or missing hover fails safe.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.osanchez.punk.sellvaultitems";
        public const string Name = "SellVaultItems";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log;

        // Persisted on/off (also toggled in the Mods menu). Gates all behaviour.
        private ConfigEntry<bool> _enabled;
        // Keyboard key used when playing on keyboard/mouse (the gamepad button is always NORTH).
        private ConfigEntry<string> _sellKey;
        private Key _sellKeyParsed = Key.G;

        internal bool Enabled => _enabled != null && _enabled.Value;

        // Per-frame snapshot, computed in Update() and rendered by OnGUI().
        private bool _sellable;
        private int _price;
        private string _promptButton = "Y";

        // ShipMenuTab.Ship is protected; read it via reflection to credit the viewing player's unit.
        private static readonly PropertyInfo ShipProp =
            typeof(ShipMenuTab).GetProperty("Ship", BindingFlags.NonPublic | BindingFlags.Instance);

        private GUIStyle _style;

        private void Awake()
        {
            Log = Logger;

            var cfg = new BepInEx.Configuration.ConfigFile(Path.Combine(ModFolder.Dir, "config.cfg"), saveOnInit: true);
            _enabled = cfg.Bind("General", "Enabled", true,
                "Master on/off (persisted). Also toggleable in the in-game Mods menu.");
            _sellKey = cfg.Bind("Input", "SellKey", "G",
                "Keyboard key to sell the hovered vault module (UnityEngine.InputSystem Key name). " +
                "The gamepad sell button is always the NORTH face button (Y / Triangle).");

            if (!Enum.TryParse(_sellKey.Value, true, out _sellKeyParsed))
            {
                Log.LogWarning($"[SellVaultItems] Could not parse SellKey '{_sellKey.Value}', defaulting to G.");
                _sellKeyParsed = Key.G;
            }

            ModMenuBridge.AddToggle("Sell Vault Items", () => Enabled, v => { if (_enabled != null) _enabled.Value = v; });

            Log.LogInfo($"[SellVaultItems] {Name} v{Version} loaded. {(Enabled ? "ON" : "OFF")}" +
                        (ModMenuBridge.Available ? " | toggle in Mods menu." : " | no Mods menu (edit config.cfg).") +
                        $" Sell key (kbm): {_sellKeyParsed}. Gamepad: NORTH (Y/Triangle).");
        }

        // Harmony-free mod: nothing to unpatch. Drop the Mods-menu row so a live reload doesn't
        // leave a duplicate toggle behind.
        private void OnDestroy()
        {
            try { ModMenuBridge.RemoveAll(); } catch { }
        }

        private void Update()
        {
            _sellable = false;
            if (!Enabled) return;

            try
            {
                if (!TryGetSellable(out var module, out var resource, out var price)) return;

                _sellable = true;
                _price = price;
                _promptButton = ActiveButtonLabel();

                if (SellPressed())
                    Sell(module, resource, price);
            }
            catch (Exception e)
            {
                _sellable = false;
                Log.LogWarning($"[SellVaultItems] Update failed: {e.Message}");
            }
        }

        // True + outputs when a module is currently hovered/selected in an OPEN vault and can be
        // priced. Returns false (fail-safe) for the shop tab, the installed grid, mid-move, or if
        // the module has no resolvable buy price.
        private static bool TryGetSellable(out Module module, out Resource resource, out int price)
        {
            module = null; resource = null; price = 0;

            VaultGridWidget vault;
            try { vault = ServiceLocator.Get<VaultGridWidget>(); }
            catch { return false; }
            if (vault == null || !vault.IsOpened) return false;

            // SelectedModule is the hovered (mouse) / selected (gamepad) vault entry. It is cleared to
            // null while a module is being moved/dragged, so this naturally excludes mid-move.
            module = vault.SelectedModule;
            if (module == null) return false;

            Vault vaultData;
            try { vaultData = ServiceLocator.Get<Vault>(); }
            catch { return false; }
            if (vaultData == null || !vaultData.Contains(module)) { module = null; return false; }

            if (!TryGetBuyPrice(module, out resource, out price)) { module = null; return false; }
            return true;
        }

        // The module's base BUY price = its row in the shop pricing config (ShopItemsConfig, keyed by
        // ModuleData.Id). We take the first Resource-denominated Price so currency + amount stay
        // consistent with what the player originally paid.
        private static bool TryGetBuyPrice(Module module, out Resource resource, out int price)
        {
            resource = null; price = 0;
            var data = module?.Data;
            if (data == null) return false;

            ShopItemsConfig config;
            try { config = ServiceLocator.Get<ShopItemsConfig>(); }
            catch { return false; }
            if (config == null) return false;

            ShopItemConfig row;
            try { row = config.Get(data.Id); }
            catch { return false; }
            if (row?.price == null) return false;

            foreach (var p in row.price)
            {
                if (p.currencyType == Price.CurrencyType.Resource && p.resource != null)
                {
                    resource = p.resource;
                    price = p.AmountFloored;
                    return price > 0;
                }
            }
            return false;
        }

        private void Sell(Module module, Resource resource, int price)
        {
            try
            {
                var unit = GetCreditUnit(resource);
                if (unit == null || !unit.HasTank(resource))
                {
                    Log.LogWarning($"[SellVaultItems] No '{resource?.Id}' tank to credit; sale aborted (module kept).");
                    return;
                }

                // Credit first, then remove — so a failure to credit never destroys the module.
                var tank = unit.GetTank(resource);
                tank.Value += price;

                var vaultData = ServiceLocator.Get<Vault>();
                vaultData.Remove(module);

                // Rebuild the vault UI so the sold module disappears and the stale hover clears.
                try
                {
                    var widget = ServiceLocator.Get<VaultGridWidget>();
                    widget?.Refresh();
                    widget?.RemoveSelection();
                }
                catch { }

                _sellable = false;
                Log.LogInfo($"[SellVaultItems] Sold '{SafeName(module)}' for {price} {resource.Id}.");
            }
            catch (Exception e)
            {
                Log.LogWarning($"[SellVaultItems] Sell failed: {e.Message}");
            }
        }

        // The unit whose money tank we credit. Prefer the ship whose build screen is open (correct
        // for co-op / non-shared currencies); fall back to any ship with the tank (the run money is a
        // shared resource, so any ship's tank is the same value).
        private static Unit GetCreditUnit(Resource resource)
        {
            try
            {
                foreach (var s in Resources.FindObjectsOfTypeAll<ModuleGridScreen>())
                {
                    if (s == null || !s.isActiveAndEnabled || !s.gameObject.activeInHierarchy) continue;
                    if (ShipProp?.GetValue(s) is Ship ship && ship.Unit != null && ship.Unit.HasTank(resource))
                        return ship.Unit;
                }
            }
            catch { }

            try
            {
                var ships = ServiceLocator.Get<ShipManager>()?.Ships;
                if (ships != null)
                    foreach (var s in ships)
                        if (s?.Unit != null && s.Unit.HasTank(resource))
                            return s.Unit;
            }
            catch { }
            return null;
        }

        private bool SellPressed()
        {
            var gp = Gamepad.current;
            if (gp != null && gp.buttonNorth.wasPressedThisFrame) return true;

            var kb = Keyboard.current;
            if (kb != null)
            {
                try { if (kb[_sellKeyParsed].wasPressedThisFrame) return true; }
                catch { }
            }
            return false;
        }

        // Label shown in the prompt: the north button's device-specific glyph name ("Y" / "Triangle")
        // when a gamepad is the active device, else the keyboard key name.
        private string ActiveButtonLabel()
        {
            var gp = Gamepad.current;
            var kb = Keyboard.current;

            bool useGamepad = gp != null;
            if (gp != null && kb != null)
            {
                try { useGamepad = gp.lastUpdateTime >= kb.lastUpdateTime; } catch { useGamepad = true; }
            }

            if (useGamepad && gp != null)
            {
                try { var n = gp.buttonNorth.displayName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
                return "Y";
            }

            try { var n = kb?[_sellKeyParsed].displayName; if (!string.IsNullOrEmpty(n)) return n; } catch { }
            return _sellKeyParsed.ToString();
        }

        private void OnGUI()
        {
            if (!Enabled || !_sellable) return;

            if (_style == null)
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 18,
                    richText = true,
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = Color.white }
                };

            string text = $"Press <b>{_promptButton}</b> to sell (<color=#43e04a>${_price}</color>)";

            const float w = 420f, h = 40f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - h - 90f;   // above the bottom input-hint bar, roughly by the vault
            var rect = new Rect(x, y, w, h);

            var bg = new Rect(x, y, w, h);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(bg, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(rect, text, _style);
        }

        private static string SafeName(Module m)
        {
            try { return m?.DisplayName ?? m?.Data?.Id ?? "module"; } catch { return "module"; }
        }
    }
}
