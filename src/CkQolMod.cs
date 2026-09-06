using System;
using System.Collections.Generic;
using CkQol.Config;
using CkQol.UI;
using PugMod;
using UnityEngine;

namespace CkQol
{
    /// Entry point. Builds the feature list, binds their config, spawns the menu and
    /// forwards the PugMod lifecycle.
    public class CkQolMod : IMod
    {
        public const string ModName = "CkQol";

        private readonly List<FeatureHandle> _features = new List<FeatureHandle>();
        private GeneralSettings _general;
        private QolMenu _menu;
        private bool _menuFailed;

        public IReadOnlyList<FeatureHandle> Features => _features;

        /// Whole-number canvas scale. Fractional scaling re-corrupts the pixel font.
        public int UiScaleFactor
        {
            get
            {
                if (_general == null) return 1;
                return int.TryParse(_general.UiScale.Value, out int scale)
                    ? Mathf.Clamp(scale, 1, 4) : 1;
            }
        }

        /// Register features here. Order is tab order.
        private static IEnumerable<IQolFeature> BuildFeatures()
        {
            yield return new Features.LoadProbe();
        }

        public void EarlyInit() { }

        public void Init()
        {
            _general = new GeneralSettings();
            _features.Add(new FeatureHandle(_general, canBeDisabled: false));

            foreach (var feature in BuildFeatures())
            {
                _features.Add(new FeatureHandle(feature));
            }

            foreach (var handle in _features)
            {
                try
                {
                    handle.Bind(ModName);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{ModName}] config bind failed for {handle.Name}");
                    Debug.LogException(e);
                }
                handle.Apply();
            }

            // Scale is baked into the canvas at build time, so drop the menu and let
            // it rebuild at the new scale on next open.
            _general.UiScale.Changed += _ => DestroyMenu();

            API.Client.OnWorldCreated += OnWorldCreated;
            API.Client.OnWorldDestroyed += OnWorldDestroyed;

            Debug.Log($"[{ModName}] loaded, {_features.Count - 1} feature(s), " +
                      $"press {_general.MenuKey.Value} for the menu");
        }

        public void Shutdown()
        {
            API.Client.OnWorldCreated -= OnWorldCreated;
            API.Client.OnWorldDestroyed -= OnWorldDestroyed;

            foreach (var handle in _features) handle.Shutdown();
            DestroyMenu();
        }

        private void DestroyMenu()
        {
            if (_menu == null) return;
            UnityEngine.Object.Destroy(_menu.gameObject);
            _menu = null;
        }

        public void ModObjectLoaded(UnityEngine.Object obj) { }

        public void Update()
        {
            if (Input.GetKeyDown(_general.MenuKey.Value))
            {
                ToggleMenu();
            }

            for (int i = 0; i < _features.Count; i++)
            {
                _features[i].Update();
            }
        }

        private void ToggleMenu()
        {
            if (_menuFailed) return;

            // Built lazily: at Init the game's fonts and sprites are not loaded yet,
            // so a menu constructed then would come out unstyled.
            if (_menu == null)
            {
                try
                {
                    _menu = QolMenu.Create(this);
                }
                catch (Exception e)
                {
                    _menuFailed = true;
                    Debug.LogError($"[{ModName}] menu failed to build, disabling it");
                    Debug.LogException(e);
                    return;
                }
            }
            _menu.Toggle();
        }

        private void OnWorldCreated()
        {
            foreach (var handle in _features) handle.WorldCreated();
        }

        private void OnWorldDestroyed()
        {
            foreach (var handle in _features) handle.WorldDestroyed();
        }
    }

    /// Mod-wide settings. Always present, cannot be switched off - it is the tab the
    /// menu hotkey itself lives on.
    public class GeneralSettings : QolFeatureBase
    {
        public override string Name => "General";

        public override string Description =>
            "Settings for the mod itself. Every change here and on the other tabs " +
            "applies immediately and is saved automatically.";

        public readonly KeySetting MenuKey =
            new KeySetting("MenuKey", "Menu hotkey", KeyCode.F1,
                           "Key that opens this window. Use Unity KeyCode names, eg F1, F4, Insert.");

        public readonly ChoiceSetting UiScale =
            new ChoiceSetting("UiScale", "Menu scale",
                              new[] { "1", "2", "3" }, "1",
                              "Whole-number zoom for this menu. Only whole steps are offered " +
                              "because the game's pixel font blurs at fractional scale. " +
                              "Reopen the menu to apply.");

        public readonly BoolSetting DumpAssets =
            new BoolSetting("LogUiAssets", "Log UI assets on open", false,
                            "Writes the game's available fonts and sliced sprites to the log. " +
                            "Only useful when restyling the menu for a new game version.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return MenuKey;
            yield return UiScale;
            yield return DumpAssets;
        }

        public override void Init()
        {
            if (DumpAssets.Value) GameTheme.DumpAssets();
        }
    }
}
