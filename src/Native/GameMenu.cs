using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CkQol.Native
{
    /// Helpers for reusing Core Keeper's own menu objects.
    ///
    /// The game's menus are SpriteRenderer-based world-space UI driven by
    /// RadicalMenu, not uGUI. Rather than imitating that, we clone the game's real
    /// option rows so the result is the game's widgets, fonts, sounds and
    /// controller navigation by construction.
    ///
    /// Deliberately reflection-free: PugMod's security verifier rejects any mod
    /// referencing System.Reflection, so fields are copied by name in code. That is
    /// safe here because RadicalMenuOption and UIelement declare no private
    /// [SerializeField] members - everything the inspector sets on them is public,
    /// so nothing is lost when the script is swapped.
    public static class GameMenu
    {
        private static Transform _staging;

        /// An inactive parent to instantiate into.
        ///
        /// Instantiating an active GameObject runs its Awake and Start immediately,
        /// and the game's option scripts read and write game preferences there.
        /// Cloning into an inactive holder defers that until the script is swapped.
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

        /// Everything the inspector sets on a menu option. All public, so it can be
        /// carried across a script swap without reflection.
        private struct OptionFields
        {
            public List<UIelement> Top, Bottom, Left, Right, Children;
            public bool SelectFirstEnabled, InvokeSelectionEvents;
            public UnityEvent OnSelectedEvent, OnDeselectedEvent;

            public float ExtraVerticalSpacing;
            public bool ActiveInSPStage, ActiveInTitle, ActiveInDebugOnly, ForceDeactive;
            public bool VisibleButNotSelectable, HandleNavigationInternally;
            public bool CanBeActivated, IsOnOffToggle;
            public PlatformFlags ActiveInPlatforms;
            public StorefrontFlags ActiveInStoreFronts;
            public PugText LabelText, ValueText;
            public List<SelectionListener> SelectionListeners;
            public string SelectionListenerSourceTag;

            public static OptionFields From(RadicalMenuOption o) => new OptionFields
            {
                Top = o.topUIElements,
                Bottom = o.bottomUIElements,
                Left = o.leftUIElements,
                Right = o.rightUIElements,
                Children = o.childElements,
                SelectFirstEnabled = o.selectFirstEnabledElementInList,
                InvokeSelectionEvents = o.invokeSelectionEvents,
                OnSelectedEvent = o.onElementSelectedEvent,
                OnDeselectedEvent = o.onElementDeselectedEvent,
                ExtraVerticalSpacing = o.extraVerticalSpacing,
                ActiveInSPStage = o.activeInSPStage,
                ActiveInTitle = o.activeInTitle,
                ActiveInDebugOnly = o.activeInDebugOnly,
                ForceDeactive = o.forceDeactive,
                VisibleButNotSelectable = o.visibleButNotSelectableWhenInactive,
                HandleNavigationInternally = o.handleNavigationInternally,
                CanBeActivated = o.canBeActivated,
                IsOnOffToggle = o.isOnOffToggle,
                ActiveInPlatforms = o.activeInPlatforms,
                ActiveInStoreFronts = o.activeInStoreFronts,
                LabelText = o.labelText,
                ValueText = o.valueText,
                SelectionListeners = o.selectionListeners,
                SelectionListenerSourceTag = o.selectionListenerSourceTag,
            };

            public void ApplyTo(RadicalMenuOption o)
            {
                o.topUIElements = Top;
                o.bottomUIElements = Bottom;
                o.leftUIElements = Left;
                o.rightUIElements = Right;
                o.childElements = Children;
                o.selectFirstEnabledElementInList = SelectFirstEnabled;
                o.invokeSelectionEvents = InvokeSelectionEvents;
                o.onElementSelectedEvent = OnSelectedEvent;
                o.onElementDeselectedEvent = OnDeselectedEvent;
                o.extraVerticalSpacing = ExtraVerticalSpacing;
                o.activeInSPStage = ActiveInSPStage;
                o.activeInTitle = ActiveInTitle;
                o.activeInDebugOnly = ActiveInDebugOnly;
                o.forceDeactive = ForceDeactive;
                o.visibleButNotSelectableWhenInactive = VisibleButNotSelectable;
                o.handleNavigationInternally = HandleNavigationInternally;
                o.canBeActivated = CanBeActivated;
                o.isOnOffToggle = IsOnOffToggle;
                o.activeInPlatforms = ActiveInPlatforms;
                o.activeInStoreFronts = ActiveInStoreFronts;
                o.labelText = LabelText;
                o.valueText = ValueText;
                o.selectionListeners = SelectionListeners;
                o.selectionListenerSourceTag = SelectionListenerSourceTag;
            }
        }

        /// Clones a row and replaces its script with ours, keeping the inspector
        /// wiring. Returns null rather than throwing so one bad row cannot cost the
        /// whole menu.
        /// debugName is passed in rather than read from typeof(T).Name: that call
        /// compiles to MemberInfo.get_Name, which the security verifier rejects.
        public static T CloneAndSwap<T>(RadicalMenuOption donor, Transform parent, string debugName)
            where T : RadicalMenuOption
        {
            if (donor == null) return null;

            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject, Staging);
                clone.name = "CkQol_" + debugName;

                var original = clone.GetComponent<RadicalMenuOption>();
                var fields = OptionFields.From(original);

                // DestroyImmediate: the replacement must exist before the object is
                // activated; Destroy would not run until the end of the frame.
                UnityEngine.Object.DestroyImmediate(original);

                var replacement = clone.AddComponent<T>();
                fields.ApplyTo(replacement);

                clone.transform.SetParent(parent, false);
                return replacement;
            }
            catch (Exception e)
            {
                Debug.LogError($"[CkQol] failed to clone a row as {debugName}");
                Debug.LogException(e);
                return null;
            }
        }

        /// Clones a row and keeps its script. Used for sliders, which hold private
        /// [SerializeField] visual references that a script swap would discard.
        public static RadicalOptionsMenuOption_Slider CloneSlider(
            RadicalOptionsMenuOption_Slider donor, Transform parent)
        {
            if (donor == null) return null;

            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject, Staging);
                clone.name = "CkQol_Slider";
                clone.transform.SetParent(parent, false);
                return clone.GetComponent<RadicalOptionsMenuOption_Slider>();
            }
            catch (Exception e)
            {
                Debug.LogError("[CkQol] failed to clone a slider row");
                Debug.LogException(e);
                return null;
            }
        }

        /// An on/off row to clone. Chosen by the option's own isOnOffToggle flag
        /// rather than by class name, so renamed game options do not break it.
        public static RadicalMenuOption FindToggleDonor(RadicalMenu menu)
        {
            if (menu == null || menu.menuOptions == null) return null;
            foreach (var option in menu.menuOptions)
            {
                if (option != null && option.isOnOffToggle && option.valueText != null) return option;
            }
            return null;
        }

        public static RadicalOptionsMenuOption_Slider FindSliderDonor(RadicalMenu menu)
        {
            if (menu == null || menu.menuOptions == null) return null;
            foreach (var option in menu.menuOptions)
            {
                var slider = option as RadicalOptionsMenuOption_Slider;
                if (slider != null) return slider;
            }
            return null;
        }

        /// Any plain row, used as the shape for submenu and back entries.
        public static RadicalMenuOption FindPlainDonor(RadicalMenu menu)
        {
            if (menu == null || menu.menuOptions == null) return null;
            foreach (var option in menu.menuOptions)
            {
                if (option != null && !option.isOnOffToggle &&
                    !(option is RadicalOptionsMenuOption_Slider) && option.labelText != null)
                {
                    return option;
                }
            }
            return null;
        }

        /// Re-scans children into menuOptions and re-lays them out. RadicalMenu only
        /// collects options in Awake, so anything added later is invisible until this.
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
            if (option != null && option.labelText != null) option.labelText.Render(text);
        }

        public static void SetValue(RadicalMenuOption option, string text)
        {
            if (option != null && option.valueText != null) option.valueText.Render(text);
        }
    }
}
