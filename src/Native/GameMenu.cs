using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace CkQol.Native
{
    /// Helpers for reusing the game's own menu objects.
    ///
    /// Menus are SpriteRenderer world-space UI driven by RadicalMenu, not uGUI, so
    /// rows are cloned rather than imitated.
    ///
    /// Reflection-free: PugMod's verifier rejects System.Reflection, so fields are
    /// copied by name. Safe because RadicalMenuOption and UIelement declare no
    /// private [SerializeField] members.
    public static class GameMenu
    {
        private static Transform _staging;

        /// Inactive parent to clone into: an active one runs the donor's Awake before
        /// its script is swapped, which for option rows writes game preferences.
        internal static Transform Staging
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

        /// Everything the inspector sets on a row, carried across the script swap.
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

        /// Clones a row and swaps in our script, keeping the inspector wiring.
        ///
        /// debugName rather than typeof(T).Name: that compiles to
        /// MemberInfo.get_Name, which the verifier rejects. parent defaults to the
        /// donor's - rows live under an offset container, not the menu root.
        public static T CloneAndSwap<T>(RadicalMenuOption donor, Transform parent, string debugName)
            where T : RadicalMenuOption
        {
            if (donor == null) return null;
            if (parent == null) parent = donor.transform.parent;

            try
            {
                var clone = UnityEngine.Object.Instantiate(donor.gameObject, Staging);
                clone.name = "CkQol_" + debugName;

                // A ranged donor's per-diamond ButtonUIElements are prefab-wired to
                // the script we are about to destroy. Left alone they win the click
                // raycast and then do nothing; QolStepStrip rebuilds them.
                foreach (var stale in clone.GetComponentsInChildren<ButtonUIElement>(true))
                {
                    if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
                }

                var original = clone.GetComponent<RadicalMenuOption>();
                var fields = OptionFields.From(original);

                // DestroyImmediate: the replacement must exist before activation.
                UnityEngine.Object.DestroyImmediate(original);

                var replacement = clone.AddComponent<T>();
                fields.ApplyTo(replacement);

                // Inherited spacing pushes the row out of view; inherited visibility
                // flags make it depend on which stock row was cloned.
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

        /// From children, not menuOptions: that list is filled in Awake, and the
        /// option menus are instantiated inactive, so it is empty at startup.
        private static RadicalMenuOption[] RowsOf(RadicalMenu menu)
        {
            if (menu == null) return new RadicalMenuOption[0];
            return menu.GetComponentsInChildren<RadicalMenuOption>(true);
        }

        /// By isOnOffToggle, not class name, so a renamed game option still matches.
        public static RadicalMenuOption FindToggleDonor(RadicalMenu menu)
        {
            foreach (var option in RowsOf(menu))
            {
                if (option != null && option.isOnOffToggle && option.valueText != null) return option;
            }
            return null;
        }

        /// The only stock row whose valueText has the diamond glyphs; others render
        /// them as question marks.
        public static RadicalMenuOption FindBarDonor(RadicalMenu menu)
        {
            foreach (var option in RowsOf(menu))
            {
                if (option is RadicalOptionsMenuOption_Volume) return option;
            }
            return null;
        }

        /// Shape for submenu and back entries. Prefers a row with no value column:
        /// rows that have one offset their label to make room.
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
        /// Does not call UpdatePosition: Activate lays the menu out itself, and a
        /// second pass collapsed the stock rows onto one line.
        public static void Refresh(RadicalMenu menu)
        {
            if (menu == null) return;
            menu.GetComponentsInChildren(true, menu.menuOptions);
            foreach (var option in menu.menuOptions)
            {
                if (option != null) option.SetParentMenu(menu);
            }

            // With autoPositioningOverride populated the layout ignores menuOptions,
            // so a row missing from it renders but never gets a slot.
            var order = menu.autoPositioningOverride;
            if (order != null && order.Count > 0)
            {
                foreach (var option in menu.menuOptions)
                {
                    if (option != null && !order.Contains(option)) order.Add(option);
                }
            }
        }

        private static readonly Dictionary<RadicalMenu, float> _pitchCache =
            new Dictionary<RadicalMenu, float>();

        /// Lets the game lay the menu out, after supplying the two inputs it needs.
        ///
        /// Menus that never auto-position ship menuEntryVirtualHeight at zero, which
        /// stacks every row on one Y. UpdatePosition starts at menuEntryStartPositionY
        /// plus half the total and walks down, so the start that reproduces the
        /// original column is (centre - pitch/2).
        ///
        /// Must run with the menu open: row visibility reads INACTIVE at startup.
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

            // Measure once: re-measuring a laid-out column feeds its own output back
            // in and the list compresses further on every visit.
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

        /// Stacks rows into the slots the template's own rows occupied.
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
            // force: Render early-outs on an unchanged string, skipping the glyphs a
            // cloned row still needs. localize false or a literal renders as "missing:".

            target.Render(text ?? string.Empty, rewindEffectAnims: true, force: true);
        }

        /// Hover description, or null. UIMouse.UpdateHoverText already renders these
        /// in menus; stock rows just never return one.
        public static List<TextAndFormatFields> Hover(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            // dontLocalize: untranslated entries without it are dropped.
            return new List<TextAndFormatFields>
            {
                new TextAndFormatFields { text = text, dontLocalize = true }
            };
        }

        public static void SetLabel(RadicalMenuOption option, string text)
        {
            if (option != null) SetLiteral(option.labelText, text);
        }

        public static void SetValue(RadicalMenuOption option, string text)
        {
            if (option != null) SetLiteral(option.valueText, text);
        }

        /// Clears the donor's leftover value text, which a clone keeps.
        public static void ClearValue(RadicalMenuOption option)
        {
            if (option != null) SetLiteral(option.valueText, string.Empty);
        }
    }
}
