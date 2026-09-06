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
        /// parent defaults to the donor's own parent. Rows live under a container
        /// inside the menu, not directly under the menu root, and that container
        /// carries an offset - parenting to the root gave rows the correct local
        /// position from the layout but the wrong world position, putting them well
        /// below the visible list.
        public static T CloneAndSwap<T>(RadicalMenuOption donor, Transform parent, string debugName)
            where T : RadicalMenuOption
        {
            if (donor == null) return null;
            if (parent == null) parent = donor.transform.parent;

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

                // Our rows are always available. Inheriting these from the donor made
                // visibility depend on which stock row happened to be cloned: once a
                // row is in menuOptions, Activate hides it whenever
                // GetActiveStateInCurrentScene is not ACTIVE.
                // Donor rows use extraVerticalSpacing to open a gap before a group.
                // UpdatePosition subtracts it before placing the row, so inheriting it
                // pushed our row far below the list - the layout was placing it
                // correctly and then shifting it out of view.
                replacement.extraVerticalSpacing = 0f;

                replacement.activeInSPStage = true;
                replacement.activeInTitle = true;
                replacement.activeInDebugOnly = false;
                replacement.forceDeactive = false;
                replacement.canBeActivated = true;

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

        /// A volume row, the only stock row whose valueText renders the diamond bar
        /// glyphs. The on/off rows only ever draw letters, so a bar cloned from one
        /// comes out as question marks.
        public static RadicalMenuOption FindBarDonor(RadicalMenu menu)
        {
            foreach (var option in RowsOf(menu))
            {
                if (option is RadicalOptionsMenuOption_Volume) return option;
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
        /// Lets the game lay the menu out, after giving it the two inputs it needs.
        ///
        /// UpdatePosition does not check autoPositioning - it always positions - but
        /// menus that never auto-position ship with menuEntryVirtualHeight at zero,
        /// so every row lands on the same Y. That is why calling it earlier collapsed
        /// the Settings list onto one line.
        ///
        /// Both inputs are derived from the rows, so no correction is applied
        /// afterwards. UpdatePosition starts at menuEntryStartPositionY + half the
        /// total height and walks down, which puts the column centre exactly half a
        /// row below that value - so the start position that reproduces the original
        /// column is (centre - pitch/2).
        ///
        /// Must run once the menu is open - row visibility cannot be read at startup,
        /// when Manager.sceneHandler is null and everything reports INACTIVE.
        private static readonly Dictionary<RadicalMenu, float> _pitchCache =
            new Dictionary<RadicalMenu, float>();

        public static void LayoutWithGame(RadicalMenu menu)
        {
            if (menu == null) return;

            var ys = new List<float>();
            foreach (var option in RowsOf(menu))
            {
                if (option != null && option.gameObject.activeSelf)
                {
                    ys.Add(option.transform.localPosition.y);
                }
            }
            if (ys.Count < 2) return;

            ys.Sort();
            float top = ys[ys.Count - 1];
            float bottom = ys[0];

            // Smallest positive gap is the row pitch; rows sharing a Y are ignored.
            float pitch = 0f;
            for (int i = 1; i < ys.Count; i++)
            {
                float gap = ys[i] - ys[i - 1];
                if (gap > 0.0001f && (pitch <= 0f || gap < pitch)) pitch = gap;
            }
            if (pitch <= 0f) pitch = (top - bottom) / (ys.Count - 1);
            if (pitch <= 0f) return;

            // Measure once. This runs every time the menu opens, and re-measuring an
            // already-laid-out column feeds its own output back in - the list visibly
            // compressed a little further on each visit.
            if (_pitchCache.TryGetValue(menu, out float cached)) pitch = cached;
            else _pitchCache[menu] = pitch;

            // UpdatePosition lays out GetAllCurrentlyActiveMenuOptions, which reads
            // autoPositioningOverride when that is populated and menuOptions
            // otherwise. Both were verified at install, but menuOptions is rebuilt in
            // Awake when the menu first activates - so make sure our rows are in
            // whichever list is authoritative, immediately before the layout runs.
            Refresh(menu);

            menu.menuEntryVirtualHeight = pitch;
            menu.menuEntryStartPositionY = (top + bottom) * 0.5f - pitch * 0.5f;

            menu.UpdatePosition();
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

        /// The menu's heading: the only PugText that is not part of a row.
        public static PugText FindHeading(RadicalMenu menu)
        {
            if (menu == null) return null;
            foreach (var text in menu.GetComponentsInChildren<PugText>(true))
            {
                if (text != null && text.GetComponentInParent<RadicalMenuOption>() == null)
                {
                    return text;
                }
            }
            return null;
        }

        public static void SetLiteral(PugText target, string text)
        {
            if (target == null) return;
            target.localize = false;
            // force: Render(string) early-outs via HasCorrectGlyphs when the string is
            // unchanged, which skips building the glyphs a cloned row still needs.
            target.Render(text ?? string.Empty, rewindEffectAnims: true, force: true);
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
