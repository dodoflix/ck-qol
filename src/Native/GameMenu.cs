using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CkQol.Native
{
    /// Helpers for reusing Core Keeper's own menu objects.
    ///
    /// The game's menus are SpriteRenderer-based world-space UI driven by
    /// RadicalMenu, not uGUI. Rather than imitating that, we clone the game's real
    /// option rows and swap in our own behaviour, so the result is the game's
    /// widgets, fonts, sounds and controller navigation by construction.
    public static class GameMenu
    {
        private static Transform _staging;

        /// An inactive parent to instantiate into.
        ///
        /// Instantiating an active GameObject runs its Awake immediately, and the
        /// game's option components read game settings in Awake. Cloning into an
        /// inactive holder defers that until after the component has been swapped.
        private static Transform Staging
        {
            get
            {
                if (_staging != null) return _staging;
                var go = new GameObject("CkQolStaging");
                go.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(go);
                _staging = go.transform;
                return _staging;
            }
        }

        /// Clones an option row and replaces its script with TNew, keeping every
        /// serialized reference (label text, sprite renderers, markers) intact.
        /// Returns null rather than throwing so one bad row cannot lose the menu.
        public static TNew CloneOption<TNew>(Component source, Transform parent)
            where TNew : MonoBehaviour
        {
            if (source == null)
            {
                Debug.LogError("[CkQol] cannot clone a null option");
                return null;
            }

            try
            {
                var clone = UnityEngine.Object.Instantiate(source.gameObject, Staging);
                clone.name = "CkQol_" + typeof(TNew).Name;

                var original = clone.GetComponent(source.GetType());
                var saved = CaptureFields(original);

                // DestroyImmediate: the replacement must exist before the object is
                // activated, and Destroy would not run until end of frame.
                UnityEngine.Object.DestroyImmediate(original);

                var replacement = clone.AddComponent<TNew>();
                RestoreFields(replacement, saved);

                clone.transform.SetParent(parent, false);
                return replacement;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CkQol] failed cloning {source.GetType().Name} -> {typeof(TNew).Name}");
                Debug.LogException(e);
                return null;
            }
        }

        /// Public instance fields declared on the component's own base chain. Only
        /// fields both types share are restored, so swapping to a different subclass
        /// keeps the inherited wiring and drops what does not apply.
        private static Dictionary<string, object> CaptureFields(Component component)
        {
            var values = new Dictionary<string, object>();
            if (component == null) return values;

            for (Type t = component.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance |
                                                  BindingFlags.DeclaredOnly))
                {
                    if (!values.ContainsKey(field.Name))
                    {
                        values[field.Name] = field.GetValue(component);
                    }
                }
            }
            return values;
        }

        private static void RestoreFields(Component component, Dictionary<string, object> values)
        {
            for (Type t = component.GetType(); t != null && t != typeof(MonoBehaviour); t = t.BaseType)
            {
                foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance |
                                                  BindingFlags.DeclaredOnly))
                {
                    if (values.TryGetValue(field.Name, out object value) && value != null)
                    {
                        try { field.SetValue(component, value); }
                        catch (Exception) { /* type changed between subclasses, skip */ }
                    }
                }
            }
        }

        /// Finds the first option in a menu whose component type name contains the
        /// given fragment. Used to pick a donor row of the right shape (a toggle, a
        /// slider) without hardcoding a specific game option class, which would be
        /// far more brittle across updates.
        public static RadicalMenuOption FindDonor(RadicalMenu menu, params string[] typeFragments)
        {
            if (menu == null || menu.menuOptions == null) return null;

            foreach (var fragment in typeFragments)
            {
                foreach (var option in menu.menuOptions)
                {
                    if (option == null) continue;
                    if (option.GetType().Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return option;
                    }
                }
            }
            return null;
        }

        /// Re-scans children into menuOptions and re-lays them out. RadicalMenu only
        /// collects its options in Awake, so anything added later is invisible to it
        /// until this runs.
        public static void Refresh(RadicalMenu menu)
        {
            if (menu == null) return;
            menu.GetComponentsInChildren(true, menu.menuOptions);
            foreach (var option in menu.menuOptions)
            {
                if (option != null) option.SetParentMenu(menu);
            }
            menu.UpdatePosition();
        }

        public static void SetLabel(RadicalMenuOption option, string text)
        {
            if (option == null || option.labelText == null) return;
            option.labelText.Render(text);
        }

        public static void SetValue(RadicalMenuOption option, string text)
        {
            if (option == null || option.valueText == null) return;
            option.valueText.Render(text);
        }
    }
}
