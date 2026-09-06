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

        /// Rows are read from the menu's children, not from menuOptions.
        ///
        /// RadicalMenu only fills menuOptions in Awake, and MenuManager instantiates
        /// the option menus inactive - so that list stays empty until the player
        /// first opens the menu, and searching it finds nothing at startup.
        private static RadicalMenuOption[] RowsOf(RadicalMenu menu)
        {
            if (menu == null) return new RadicalMenuOption[0];
            return menu.GetComponentsInChildren<RadicalMenuOption>(true);
        }

        /// An on/off row to clone. Chosen by the option's own isOnOffToggle flag
        /// rather than by class name, so renamed game options do not break it.
        public static RadicalMenuOption FindToggleDonor(RadicalMenu menu)
        {
            foreach (var option in RowsOf(menu))
            {
                if (option != null && option.isOnOffToggle && option.valueText != null) return option;
            }
            return null;
        }

        public static RadicalOptionsMenuOption_Slider FindSliderDonor(RadicalMenu menu)
        {
            foreach (var option in RowsOf(menu))
            {
                var slider = option as RadicalOptionsMenuOption_Slider;
                if (slider != null) return slider;
            }
            return null;
        }

        /// Any plain row, used as the shape for submenu and back entries.
        ///
        /// Prefers a row with no value column. Rows that have one (Language, showing
        /// "English") offset their label to the left to make room for it, so cloning
        /// one leaves our entry visibly misaligned against the other submenu rows.
        public static RadicalMenuOption FindPlainDonor(RadicalMenu menu)
        {
            RadicalMenuOption fallback = null;
            foreach (var option in RowsOf(menu))
            {
                if (option == null || option.isOnOffToggle) continue;
                if (option is RadicalOptionsMenuOption_Slider) continue;
                if (option.labelText == null) continue;

                if (option.valueText == null) return option;
                if (fallback == null) fallback = option;
            }
            return fallback;
        }

        /// Re-scans children into menuOptions.
        ///
        /// Deliberately does NOT call UpdatePosition. RadicalMenu.Activate lays the
        /// menu out itself every time it is shown; running a second pass afterwards
        /// collapsed all of the stock rows onto one line. All that is needed is for
        /// our rows to be in the list before the game's own pass runs - Awake only
        /// collects them once, and may already have run before we parented ours.
        public static void Refresh(RadicalMenu menu)
        {
            if (menu == null) return;
            menu.GetComponentsInChildren(true, menu.menuOptions);
            foreach (var option in menu.menuOptions)
            {
                if (option != null) option.SetParentMenu(menu);
            }

            // When a menu has autoPositioningOverride populated, the layout pass uses
            // that curated list and ignores menuOptions entirely - so a row missing
            // from it renders but never gets a slot, and sits wherever the donor was.
            var order = menu.autoPositioningOverride;
            if (order != null && order.Count > 0)
            {
                foreach (var option in menu.menuOptions)
                {
                    if (option != null && !order.Contains(option)) order.Add(option);
                }
            }
        }

        /// PugText.Render treats its argument as a localization key when the text
        /// object has localize set - the donor rows do, which is why our labels came
        /// out as "missing: Enabled". Our strings are already literal.
        /// Places a row one slot below the lowest existing row.
        ///
        /// Necessary because the Settings menu has autoPositioning off: every stock
        /// row sits at its authored prefab position and UpdatePosition does nothing,
        /// so a clone keeps the donor's position and lands on top of it. Spacing is
        /// measured from the existing rows rather than assumed.
        public static void PlaceBelowLast(RadicalMenu menu, RadicalMenuOption ours)
        {
            if (menu == null || ours == null) return;

            // Positions in child order. The direction the list runs is derived from
            // them rather than assumed - this is world-space UI and the sign of "down"
            // is not something to guess at.
            var ys = new List<float>();
            float x = ours.transform.localPosition.x;
            float z = ours.transform.localPosition.z;

            foreach (var option in RowsOf(menu))
            {
                if (option == null || option == ours) continue;
                var local = option.transform.localPosition;
                ys.Add(local.y);
                x = local.x;
                z = local.z;
            }

            if (ys.Count == 0) return;

            float first = ys[0];
            float last = ys[ys.Count - 1];

            var sorted = new List<float>(ys);
            sorted.Sort();

            float pitch = 0f;
            for (int i = 1; i < sorted.Count; i++)
            {
                float gap = sorted[i] - sorted[i - 1];
                if (gap > 0.0001f && (pitch <= 0f || gap < pitch)) pitch = gap;
            }
            if (pitch <= 0f) pitch = Mathf.Abs(menu.menuEntryVirtualHeight);
            if (pitch <= 0f) pitch = 1f;

            // Continue past whichever end the list ends on.
            float y = last <= first ? sorted[0] - pitch : sorted[sorted.Count - 1] + pitch;

            var placed = new Vector3(x, y, z);
            ours.transform.localPosition = placed;

            Debug.Log($"[CkQol] placed row at {placed} pitch={pitch} firstY={first} lastY={last} " +
                      $"minY={sorted[0]} maxY={sorted[sorted.Count - 1]} rows={ys.Count} " +
                      $"world={ours.transform.position} active={ours.gameObject.activeInHierarchy}");
        }

        /// Stacks rows down a menu we built ourselves, reusing the slot positions the
        /// template's own rows occupied so spacing matches the game exactly.
        public static void PlaceInSlots(RadicalMenu menu, List<Vector3> slots)
        {
            if (menu == null || slots == null || slots.Count == 0) return;

            float pitch = slots.Count > 1 ? slots[0].y - slots[1].y : 1f;
            if (pitch <= 0f) pitch = 1f;

            int index = 0;
            foreach (var option in RowsOf(menu))
            {
                if (option == null) continue;
                Vector3 slot = index < slots.Count
                    ? slots[index]
                    : new Vector3(slots[0].x, slots[slots.Count - 1].y - pitch * (index - slots.Count + 1),
                                  slots[0].z);
                option.transform.localPosition = slot;
                index++;
            }
        }

        /// The positions a menu's rows occupy, top to bottom.
        public static List<Vector3> CaptureSlots(RadicalMenu menu)
        {
            var slots = new List<Vector3>();
            foreach (var option in RowsOf(menu))
            {
                if (option != null) slots.Add(option.transform.localPosition);
            }
            slots.Sort((a, b) => b.y.CompareTo(a.y));
            return slots;
        }

        public static void SetLiteral(PugText target, string text)
        {
            if (target == null) return;
            target.localize = false;
            target.Render(text ?? string.Empty);
        }

        public static void SetLabel(RadicalMenuOption option, string text)
        {
            if (option != null) SetLiteral(option.labelText, text);
        }

        public static void SetValue(RadicalMenuOption option, string text)
        {
            if (option != null) SetLiteral(option.valueText, text);
        }

        /// Clears a donor's leftover value text. A cloned row keeps whatever the
        /// original had rendered - cloning the Language row left "english" sitting in
        /// the value column of our submenu entry.
        public static void ClearValue(RadicalMenuOption option)
        {
            if (option != null) SetLiteral(option.valueText, string.Empty);
        }
    }
}
