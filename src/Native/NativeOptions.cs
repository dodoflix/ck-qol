using System;
using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Adds the mod's settings to Core Keeper's own Options menu.
    ///
    /// One row is appended to Options ("Core Keeper QoL"), which opens a menu
    /// listing the features; each feature opens a page of its settings. Every row
    /// is a clone of one of the game's real option rows, so navigation, sounds,
    /// fonts and controller support come from the game rather than being imitated.
    public static class NativeOptions
    {
        private static bool _installed;
        private static RadicalMenu _rootMenu;

        public static bool Installed => _installed;

        /// True once the game's menus exist. They are built by MenuManager during
        /// startup, well after mods load, so installation has to wait for this.
        public static bool MenusReady =>
            Manager.menu != null &&
            Manager.menu.optionsMenu != null &&
            Manager.menu.uiOptionsMenu != null;

        public static void Install(CkQolMod mod)
        {
            if (_installed) return;

            try
            {
                var optionsMenu = Manager.menu.optionsMenu;
                var donorMenu = Manager.menu.uiOptionsMenu;

                // Donor rows: a submenu-opening row from Options, plus a toggle and a
                // slider from the UI options page. Picked by type-name shape rather
                // than a specific game option class, which would break on any update
                // that renames or reorders those options.
                var submenuDonor = GameMenu.FindDonor(optionsMenu, "Options", "Pause");
                var toggleDonor = GameMenu.FindDonor(donorMenu, "Toggle");
                var sliderDonor = GameMenu.FindDonor(donorMenu, "Slider");

                if (submenuDonor == null)
                {
                    Debug.LogError("[CkQol] no donor row in the Options menu, cannot add the tab");
                    _installed = true; // do not retry every frame
                    return;
                }
                Debug.Log($"[CkQol] donors: submenu='{submenuDonor.GetType().Name}' " +
                          $"toggle='{toggleDonor?.GetType().Name ?? "none"}' " +
                          $"slider='{sliderDonor?.GetType().Name ?? "none"}'");

                _rootMenu = BuildMenu(donorMenu, "CkQolRootMenu");

                // Feature list: one submenu row per feature.
                foreach (var handle in mod.Features)
                {
                    var page = BuildFeaturePage(donorMenu, handle, submenuDonor, toggleDonor, sliderDonor);
                    if (page == null) continue;

                    var row = GameMenu.CloneOption<QolSubmenuOption>(submenuDonor, _rootMenu.transform);
                    if (row == null) continue;
                    row.Label = handle.Name;
                    row.Target = page;
                }
                AddBack(submenuDonor, _rootMenu);
                GameMenu.Refresh(_rootMenu);

                // Finally the entry in the game's own Options menu.
                var entry = GameMenu.CloneOption<QolSubmenuOption>(submenuDonor, optionsMenu.transform);
                if (entry != null)
                {
                    entry.Label = "Core Keeper QoL";
                    entry.Target = _rootMenu;
                    GameMenu.Refresh(optionsMenu);
                }

                _installed = true;
                Debug.Log("[CkQol] added to the game's Options menu");
            }
            catch (Exception e)
            {
                _installed = true;
                Debug.LogError("[CkQol] failed to install into the Options menu");
                Debug.LogException(e);
            }
        }

        /// A fresh menu cloned from an existing options page, emptied of its rows.
        /// Cloning keeps the page's background, layout metrics and title wiring.
        private static RadicalMenu BuildMenu(RadicalMenu template, string name)
        {
            var clone = UnityEngine.Object.Instantiate(template.gameObject);
            clone.name = name;
            clone.SetActive(false);
            UnityEngine.Object.DontDestroyOnLoad(clone);

            var menu = clone.GetComponent<RadicalMenu>();
            foreach (var option in clone.GetComponentsInChildren<RadicalMenuOption>(true))
            {
                UnityEngine.Object.DestroyImmediate(option.gameObject);
            }
            menu.menuOptions.Clear();
            return menu;
        }

        private static RadicalMenu BuildFeaturePage(RadicalMenu template, FeatureHandle handle,
                                                    RadicalMenuOption submenuDonor,
                                                    RadicalMenuOption toggleDonor,
                                                    RadicalMenuOption sliderDonor)
        {
            var page = BuildMenu(template, "CkQolPage_" + handle.Name);

            if (handle.CanBeDisabled && toggleDonor != null)
            {
                var row = GameMenu.CloneOption<QolToggleOption>(toggleDonor, page.transform);
                if (row != null)
                {
                    row.Label = "Enabled";
                    row.Setting = handle.Enabled;
                }
            }

            foreach (var setting in handle.Settings)
            {
                AddSettingRow(page, setting, toggleDonor, sliderDonor);
            }

            AddBack(submenuDonor, page);
            GameMenu.Refresh(page);
            return page;
        }

        private static void AddSettingRow(RadicalMenu page, ModSetting setting,
                                          RadicalMenuOption toggleDonor,
                                          RadicalMenuOption sliderDonor)
        {
            switch (setting)
            {
                case BoolSetting b when toggleDonor != null:
                {
                    var row = GameMenu.CloneOption<QolToggleOption>(toggleDonor, page.transform);
                    if (row != null) { row.Label = b.Label; row.Setting = b; }
                    break;
                }
                case IntSetting i when sliderDonor != null:
                {
                    var row = GameMenu.CloneOption<QolSliderOption>(sliderDonor, page.transform);
                    if (row != null) { row.Label = i.Label; row.IntSetting = i; }
                    break;
                }
                case FloatSetting f when sliderDonor != null:
                {
                    var row = GameMenu.CloneOption<QolSliderOption>(sliderDonor, page.transform);
                    if (row != null) { row.Label = f.Label; row.FloatSetting = f; }
                    break;
                }
                case ChoiceSetting c when sliderDonor != null:
                {
                    var row = GameMenu.CloneOption<QolChoiceOption>(sliderDonor, page.transform);
                    if (row != null) { row.Label = c.Label; row.Setting = c; }
                    break;
                }
                default:
                    // Text and key settings have no native row yet; they stay editable
                    // in the config file rather than being shown as something they
                    // are not.
                    Debug.Log($"[CkQol] '{setting.Label}' ({setting.GetType().Name}) has no native row, " +
                              "edit it in the config file");
                    break;
            }
        }

        private static void AddBack(RadicalMenuOption donor, RadicalMenu menu)
        {
            var back = GameMenu.CloneOption<QolBackOption>(donor, menu.transform);
            if (back != null) back.Label = "Back";
        }
    }
}
