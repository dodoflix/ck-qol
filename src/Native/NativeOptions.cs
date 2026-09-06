using System;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Adds the mod's settings to Core Keeper's own Options menu.
    ///
    /// One row is appended to Options ("Core Keeper QoL") which opens a list of
    /// features; each feature opens a page of its settings. Every row is a clone of
    /// one of the game's real rows, so navigation, sounds, fonts and controller
    /// support come from the game rather than being imitated.
    public static class NativeOptions
    {
        private static bool _installed;
        private static int _attempts;

        /// Menus can appear a frame or two before their rows do, so a single attempt
        /// at the first opportunity is unreliable. Capped so a genuinely missing
        /// donor does not retry forever.
        private const int MaxAttempts = 600;

        public static bool Installed => _installed || _attempts >= MaxAttempts;

        /// The game's menus are built by MenuManager during startup, long after mods
        /// load, so installation has to wait for them.
        public static bool MenusReady =>
            Manager.menu != null &&
            Manager.menu.optionsMenu != null &&
            Manager.menu.uiOptionsMenu != null;

        public static void Install(CkQolMod mod)
        {
            if (_installed) return;
            _attempts++;

            try
            {
                var optionsMenu = Manager.menu.optionsMenu;

                // Donors are searched across several stock pages because no single
                // page is guaranteed to contain every row shape.
                var plainDonor = FirstPlain(optionsMenu, Manager.menu.uiOptionsMenu,
                                            Manager.menu.gameplayOptionsMenu);
                var toggleDonor = FirstToggle(Manager.menu.uiOptionsMenu,
                                              Manager.menu.gameplayOptionsMenu,
                                              Manager.menu.videoOptionsMenu);
                var sliderDonor = FirstSlider(Manager.menu.audioOptionsMenu,
                                              Manager.menu.uiOptionsMenu,
                                              Manager.menu.videoOptionsMenu,
                                              Manager.menu.gameplayOptionsMenu,
                                              Manager.menu.graphicsOptionsMenu,
                                              Manager.menu.performanceOptionsMenu,
                                              Manager.menu.optionsMenu);

                if (plainDonor != null || _attempts == MaxAttempts)
                {
                    Debug.Log($"[CkQol] donors after {_attempts} attempt(s): " +
                              $"plain={(plainDonor != null)} toggle={(toggleDonor != null)} " +
                              $"slider={(sliderDonor != null)}");
                }

                if (plainDonor == null)
                {
                    if (_attempts >= MaxAttempts)
                    {
                        Debug.LogError("[CkQol] no plain row to clone, cannot add the options entry");
                    }
                    return; // rows may not exist yet, try again next frame
                }

                var rootMenu = BuildMenu(Manager.menu.uiOptionsMenu, "CkQolRootMenu");
                if (rootMenu == null) return;

                foreach (var handle in mod.Features)
                {
                    var page = BuildFeaturePage(handle, plainDonor, toggleDonor, sliderDonor);
                    if (page == null) continue;

                    var row = GameMenu.CloneAndSwap<QolSubmenuOption>(plainDonor, rootMenu.transform, "Submenu");
                    if (row == null) continue;
                    row.Label = handle.Name;
                    row.Target = page;
                }

                AddBack(plainDonor, rootMenu);
                GameMenu.Refresh(rootMenu);

                var entry = GameMenu.CloneAndSwap<QolSubmenuOption>(plainDonor, optionsMenu.transform, "Entry");
                if (entry == null)
                {
                    Debug.LogError("[CkQol] could not add the entry to the Options menu");
                    return;
                }
                entry.Label = "Core Keeper QoL";
                entry.Target = rootMenu;
                GameMenu.Refresh(optionsMenu);

                _installed = true;
                Debug.Log($"[CkQol] added to the game's Options menu after {_attempts} attempt(s)");
            }
            catch (Exception e)
            {
                _installed = true; // a throw will not fix itself on the next frame
                Debug.LogError("[CkQol] failed to install into the Options menu");
                Debug.LogException(e);
            }
        }

        /// A fresh page cloned from a stock options page, emptied of its rows, so it
        /// keeps the page's background, layout metrics and title wiring.
        private static RadicalMenu BuildMenu(RadicalMenu template, string name)
        {
            if (template == null) return null;

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

        private static RadicalMenu BuildFeaturePage(FeatureHandle handle,
                                                    RadicalMenuOption plainDonor,
                                                    RadicalMenuOption toggleDonor,
                                                    RadicalOptionsMenuOption_Slider sliderDonor)
        {
            var page = BuildMenu(Manager.menu.uiOptionsMenu, "CkQolPage_" + handle.Name);
            if (page == null) return null;

            if (handle.CanBeDisabled && toggleDonor != null)
            {
                var row = GameMenu.CloneAndSwap<QolToggleOption>(toggleDonor, page.transform, "Toggle");
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

            AddBack(plainDonor, page);
            GameMenu.Refresh(page);
            return page;
        }

        private static void AddSettingRow(RadicalMenu page, ModSetting setting,
                                          RadicalMenuOption toggleDonor,
                                          RadicalOptionsMenuOption_Slider sliderDonor)
        {
            if (setting is BoolSetting b)
            {
                if (toggleDonor == null) return;
                var row = GameMenu.CloneAndSwap<QolToggleOption>(toggleDonor, page.transform, "Toggle");
                if (row != null) { row.Label = b.Label; row.Setting = b; }
                return;
            }

            if (sliderDonor == null) return;

            var slider = GameMenu.CloneSlider(sliderDonor, page.transform);
            if (slider == null) return;

            var binding = slider.gameObject.AddComponent<QolSliderBinding>();
            binding.Slider = slider;

            if (setting is IntSetting i) { binding.Label = i.Label; binding.Int = i; }
            else if (setting is FloatSetting f) { binding.Label = f.Label; binding.Float = f; }
            else if (setting is ChoiceSetting c) { binding.Label = c.Label; binding.Choice = c; }
            else
            {
                // Text and key settings have no native row; they stay editable in the
                // config file rather than being shown as something they are not.
                UnityEngine.Object.DestroyImmediate(slider.gameObject);
                Debug.Log($"[CkQol] '{setting.Label}' has no native row, edit it in the config file");
            }
        }

        private static void AddBack(RadicalMenuOption donor, RadicalMenu menu)
        {
            var back = GameMenu.CloneAndSwap<QolBackOption>(donor, menu.transform, "Back");
            if (back != null) back.Label = "Back";
        }

        private static RadicalMenuOption FirstPlain(params RadicalMenu[] menus)
        {
            foreach (var menu in menus)
            {
                var found = GameMenu.FindPlainDonor(menu);
                if (found != null) return found;
            }
            return null;
        }

        private static RadicalMenuOption FirstToggle(params RadicalMenu[] menus)
        {
            foreach (var menu in menus)
            {
                var found = GameMenu.FindToggleDonor(menu);
                if (found != null) return found;
            }
            return null;
        }

        private static RadicalOptionsMenuOption_Slider FirstSlider(params RadicalMenu[] menus)
        {
            foreach (var menu in menus)
            {
                var found = GameMenu.FindSliderDonor(menu);
                if (found != null) return found;
            }
            return null;
        }
    }
}
