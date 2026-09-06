using System;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Adds the mod's settings to the game's Settings menu: one entry opening a list
    /// of features, each opening a page. Every row is a clone of a real one, so
    /// navigation, sound and fonts come from the game.
    public static class NativeOptions
    {
        private static bool _installed;
        private static int _attempts;

        /// Menus can appear a frame or two before their rows. Capped so a missing
        /// donor does not retry forever.
        private const int MaxAttempts = 600;

        public static bool Installed => _installed || _attempts >= MaxAttempts;

        /// MenuManager builds these during startup, long after mods load.
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

                // No single page has every row shape.
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
                        Debug.LogError("[CkQol] no plain row to clone, cannot add the settings entry");
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
                    row.Tooltip = handle.Description;
                }

                AddBack(plainDonor, rootMenu);
                GameMenu.Refresh(rootMenu);
                LayoutOwnMenu(rootMenu);

                var entry = GameMenu.CloneAndSwap<QolSubmenuOption>(plainDonor, null, "Entry");
                if (entry == null)
                {
                    Debug.LogError("[CkQol] could not add the entry to the Settings menu");
                    return;
                }
                entry.Label = "QoL settings";
                entry.Target = rootMenu;
                GameMenu.Refresh(optionsMenu);
                entry.Owner = optionsMenu;

                _installed = true;
                Debug.Log($"[CkQol] added to the game's Settings menu after {_attempts} attempt(s)");
            }
            catch (Exception e)
            {
                _installed = true; // a throw will not fix itself on the next frame
                Debug.LogError("[CkQol] failed to install into the Settings menu");
                Debug.LogException(e);
            }
        }

        /// Slot positions captured from each template before its rows are removed;
        /// these menus do not auto-position.
        private static readonly System.Collections.Generic.Dictionary<RadicalMenu,
            System.Collections.Generic.List<Vector3>> _slots =
            new System.Collections.Generic.Dictionary<RadicalMenu,
                System.Collections.Generic.List<Vector3>>();

        /// Container the rows live under, or the menu root.
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

            // Sibling of the template so it shares the template's scene and parent.
            var clone = UnityEngine.Object.Instantiate(template.gameObject,
                                                       template.transform.parent);
            clone.name = name;
            clone.SetActive(false);

            var menu = clone.GetComponent<RadicalMenu>();

            // Where the template's rows sat, so ours match the game's spacing.
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

            // Feature pages only; the root owns no settings.
            var reset = GameMenu.CloneAndSwap<QolResetOption>(plainDonor, RowParent(page), "Reset");
            if (reset != null) reset.Feature = handle;

            AddBack(plainDonor, page);
            GameMenu.Refresh(page);
            LayoutOwnMenu(page);
            return page;
        }

        private static void AddSettingRow(RadicalMenu page, ModSetting setting,
                                          RadicalMenuOption toggleDonor,
                                          RadicalMenuOption barDonor)
        {
            // The on/off donor is the only stock row with a value column.
            if (toggleDonor == null) return;

            if (setting is BoolSetting b)
            {
                var row = GameMenu.CloneAndSwap<QolToggleOption>(toggleDonor, RowParent(page), "Toggle");
                if (row != null) { row.Label = b.Label; row.Setting = b; }
                return;
            }

            if (setting is StringSetting s)
            {
                var row = GameMenu.CloneAndSwap<QolTextOption>(toggleDonor, RowParent(page), "Text");
                if (row != null) { row.Label = s.Label; row.Setting = s; }
                return;
            }

            if (setting is KeySetting k)
            {
                var row = GameMenu.CloneAndSwap<QolKeybindOption>(toggleDonor, RowParent(page), "Keybind");
                if (row != null) { row.Label = k.Label; row.Setting = k; }
                return;
            }

            // Only clone the volume row when a bar is drawn: it has the diamond
            // glyphs but renders numbers bold.
            bool drawsBar = QolNumberOption.SegmentsFor(setting) > 0;
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
