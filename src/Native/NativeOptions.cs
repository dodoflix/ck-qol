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
                var barDonor = FirstBar(Manager.menu.audioOptionsMenu,
                                        Manager.menu.uiOptionsMenu,
                                        Manager.menu.gameplayOptionsMenu);

                if (plainDonor != null || _attempts == MaxAttempts)
                {
                    Debug.Log($"[CkQol] donors: plain={(plainDonor != null)} " +
                              $"toggle={(toggleDonor != null)} bar={(barDonor != null)}");
                }

                if (plainDonor == null)
                {
                    if (_attempts >= MaxAttempts)
                    {
                        Debug.LogError("[CkQol] no plain row to clone, cannot add the options entry");
                    }
                    return; // rows may not exist yet, try again next frame
                }

                var rootMenu = BuildMenu(Manager.menu.uiOptionsMenu, "CkQolRootMenu", "QoL settings");
                if (rootMenu == null) return;

                foreach (var handle in mod.Features)
                {
                    var page = BuildFeaturePage(handle, plainDonor, toggleDonor, barDonor);
                    if (page == null) continue;

                    var row = GameMenu.CloneAndSwap<QolSubmenuOption>(plainDonor, RowParent(rootMenu), "Submenu");
                    if (row == null) continue;
                    row.Label = handle.Name;
                    row.Target = page;
                }

                AddBack(plainDonor, rootMenu);
                GameMenu.Refresh(rootMenu);
                LayoutOwnMenu(rootMenu);

                var entry = GameMenu.CloneAndSwap<QolSubmenuOption>(plainDonor, null, "Entry");
                if (entry == null)
                {
                    Debug.LogError("[CkQol] could not add the entry to the Options menu");
                    return;
                }
                entry.Label = "QoL settings";
                entry.Target = rootMenu;
                GameMenu.Refresh(optionsMenu);
                entry.Owner = optionsMenu;

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
        /// Slot positions of each page we build, captured from the template before
        /// its rows are removed. Needed because these menus do not auto-position.
        private static readonly System.Collections.Generic.Dictionary<RadicalMenu,
            System.Collections.Generic.List<Vector3>> _slots =
            new System.Collections.Generic.Dictionary<RadicalMenu,
                System.Collections.Generic.List<Vector3>>();

        /// Container the menu's rows live under. Captured from the template before
        /// its rows were deleted; falls back to the menu root.
        private static readonly System.Collections.Generic.Dictionary<RadicalMenu, Transform>
            _rowParents = new System.Collections.Generic.Dictionary<RadicalMenu, Transform>();

        private static Transform RowParent(RadicalMenu menu)
        {
            if (menu == null) return null;
            return _rowParents.TryGetValue(menu, out var parent) && parent != null
                ? parent : menu.transform;
        }

        private static RadicalMenu BuildMenu(RadicalMenu template, string name, string title)
        {
            if (template == null) return null;

            // Instantiated as a sibling of the template rather than moved to
            // DontDestroyOnLoad. Menu clicks are a physics raycast
            // (UIMouse -> Manager.physics.RaycastNonAlloc on the UI layer), and
            // colliders in the DontDestroyOnLoad scene are not hit by it - the page
            // rendered and worked by keyboard, but nothing on it could be clicked.
            var clone = UnityEngine.Object.Instantiate(template.gameObject,
                                                       template.transform.parent);
            clone.name = name;
            clone.SetActive(false);

            var menu = clone.GetComponent<RadicalMenu>();

            // Record where the template's rows sat before deleting them, so our rows
            // can occupy the same slots and match the game's spacing exactly.
            _slots[menu] = GameMenu.CaptureSlots(menu);

            var firstRow = clone.GetComponentInChildren<RadicalMenuOption>(true);
            if (firstRow != null) _rowParents[menu] = firstRow.transform.parent;

            var init = clone.AddComponent<QolPageInit>();
            init.Menu = menu;
            init.Title = title;

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
                                                    RadicalMenuOption barDonor)
        {
            var page = BuildMenu(Manager.menu.uiOptionsMenu, "CkQolPage_" + handle.Name, handle.Name);
            if (page == null) return null;

            if (handle.CanBeDisabled && toggleDonor != null)
            {
                var row = GameMenu.CloneAndSwap<QolToggleOption>(toggleDonor, RowParent(page), "Toggle");
                if (row != null)
                {
                    row.Label = "Enabled";
                    row.Setting = handle.Enabled;
                }
            }

            foreach (var setting in handle.Settings)
            {
                AddSettingRow(page, setting, toggleDonor, barDonor);
            }

            AddBack(plainDonor, page);
            GameMenu.Refresh(page);
            LayoutOwnMenu(page);
            return page;
        }

        private static void AddSettingRow(RadicalMenu page, ModSetting setting,
                                          RadicalMenuOption toggleDonor,
                                          RadicalMenuOption barDonor)
        {
            // Every row shape is cloned from the on/off donor: it is the only stock
            // row that carries a value column, which is where the value is shown.
            if (toggleDonor == null) return;

            if (setting is BoolSetting b)
            {
                var row = GameMenu.CloneAndSwap<QolToggleOption>(toggleDonor, RowParent(page), "Toggle");
                if (row != null) { row.Label = b.Label; row.Setting = b; }
                return;
            }

            // Clone the volume row only when a bar will actually be drawn. Its
            // valueText is the only one with the diamond glyphs, but it is also
            // styled differently - a number rendered in it comes out bold and unlike
            // every other row.
            bool drawsBar = setting is FloatSetting ||
                            (setting is IntSetting i2 && i2.Max - i2.Min > 0 &&
                             i2.Max - i2.Min <= QolNumberOption.BarSegments);
            var donor = drawsBar && barDonor != null ? barDonor : toggleDonor;

            var number = GameMenu.CloneAndSwap<QolNumberOption>(donor, RowParent(page), "Number");
            if (number == null) return;
            number.CanDrawBar = donor == barDonor;

            if (setting is IntSetting i) { number.Label = i.Label; number.Int = i; }
            else if (setting is FloatSetting f) { number.Label = f.Label; number.Float = f; }
            else if (setting is ChoiceSetting c) { number.Label = c.Label; number.Choice = c; }
            else UnityEngine.Object.DestroyImmediate(number.gameObject);
        }

        private static void LayoutOwnMenu(RadicalMenu menu)
        {
            if (menu != null && _slots.TryGetValue(menu, out var slots))
            {
                GameMenu.PlaceInSlots(menu, slots);
            }
        }

        private static void AddBack(RadicalMenuOption donor, RadicalMenu menu)
        {
            var back = GameMenu.CloneAndSwap<QolBackOption>(donor, RowParent(menu), "Back");
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

        private static RadicalMenuOption FirstBar(params RadicalMenu[] menus)
        {
            foreach (var menu in menus)
            {
                var found = GameMenu.FindBarDonor(menu);
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

    }
}
