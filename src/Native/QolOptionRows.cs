using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Rows follow the shape of the game's own option scripts (see
    /// RadicalOptionsMenuOption_ScreenShake): label in labelText, current value in
    /// valueText, OnActivated changes it, skim left/right does the same.

    /// Opens a submenu.
    public class QolSubmenuOption : RadicalPauseMenuOption
    {
        public RadicalMenu Target;
        public string Label;

        /// Set when this row was appended to a menu that had no room for it, so the
        /// menu needs re-laying out once it is open.
        public RadicalMenu Owner;

        private bool _layoutPending;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            // Defer: this fires part way through Activate, before the menu has
            // finished setting each row active and before Unity has run Start, so
            // laying out here both misses rows and reads stale labels.
            _layoutPending = true;
        }

        protected override void Update()
        {
            base.Update();
            if (!_layoutPending) return;
            _layoutPending = false;

            if (Owner != null) GameMenu.LayoutWithGame(Owner);
        }

        private void Start()
        {
            GameMenu.SetLabel(this, Label);
            GameMenu.ClearValue(this);
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (Target != null) Manager.menu.PushMenu(Target);
        }
    }

    /// Leaves the current menu. Escape already does this, but a visible row matters
    /// for mouse users who never press it.
    public class QolBackOption : RadicalPauseMenuOption
    {
        public string Label = "Back";

        private void Start()
        {
            GameMenu.SetLabel(this, Label);
            GameMenu.ClearValue(this);
        }

        public override void OnActivated()
        {
            base.OnActivated();
            Manager.menu.PopMenu();
        }
    }

    /// On/off row bound to a BoolSetting.
    public class QolToggleOption : RadicalPauseMenuOption
    {
        public BoolSetting Setting;
        public string Label;

        private void Start() => Refresh();

        public override bool IsOn() => Setting != null && Setting.Value;

        public override void OnActivated()
        {
            base.OnActivated();
            if (Setting != null) Setting.Value = !Setting.Value;
            Refresh();
        }

        public override bool OnSkimLeft()
        {
            OnActivated();
            return true;
        }

        public override bool OnSkimRight() => OnSkimLeft();

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);
            GameMenu.SetValue(this, IsOn() ? "on" : "off");
        }
    }

    /// Numeric or multiple-choice row, stepped with left/right.
    ///
    /// Not built on RadicalOptionsMenuOption_Slider - that class is unused in the
    /// shipped game, so there is no instance to clone, and its step count and glyphs
    /// are private with no setter. What looks like a slider in Audio settings is
    /// text: RadicalOptionsMenuOption_Volume renders eight filled or hollow diamonds
    /// into valueText and steps the value in eighths. Ranged settings here draw the
    /// same bar the same way, with a QolStepStrip over it for per-diamond clicking.
    public class QolNumberOption : RadicalPauseMenuOption
    {
        public IntSetting Int;
        public FloatSetting Float;
        public ChoiceSetting Choice;
        public string Label;

        /// Only rows cloned from a volume row can draw the diamond bar; the on/off
        /// rows' valueText has no glyph for those characters and renders '?'.
        public bool CanDrawBar;

        /// Diamonds to draw, or zero for a plain numeric or text value.
        private int _segments;

        /// Step under the pointer, or -1. Shown instead of the stored value so the
        /// bar previews where a click would land.
        private int _preview = -1;

        /// Matches the eight steps the game's volume rows use.
        public const int BarSegments = 8;

        /// How many diamonds a setting is worth, or zero if it should stay numeric.
        /// A bar cannot show which of twenty values is selected, so wide integer
        /// ranges are left as text.
        public static int SegmentsFor(ModSetting setting)
        {
            if (setting is FloatSetting) return BarSegments;
            if (setting is IntSetting i)
            {
                int span = i.Max - i.Min;
                return span > 0 && span <= BarSegments ? span : 0;
            }
            return 0;
        }

        private void Start()
        {
            if (CanDrawBar)
            {
                if (Float != null) _segments = SegmentsFor(Float);
                else if (Int != null) _segments = SegmentsFor(Int);
            }
            Refresh();
            if (_segments > 0) QolStepStrip.Build(this, _segments);
        }

        public override void OnActivated()
        {
            base.OnActivated();
            Step(1);
        }

        public override bool OnSkimRight()
        {
            Step(1);
            return true;
        }

        public override bool OnSkimLeft()
        {
            Step(-1);
            return true;
        }

        /// Jumps to the step the pointer clicked. Step 1 is the first diamond, so as
        /// with the game's volume rows the low end of the range is reachable by
        /// stepping but not by clicking.
        public void SetStep(int step)
        {
            if (_segments <= 0) return;

            if (Float != null)
            {
                Float.Value = Float.Min + (Float.Max - Float.Min) * step / _segments;
            }
            else if (Int != null)
            {
                Int.Value = Int.Min + step;
            }

            _preview = -1;
            Refresh();
        }

        public void PreviewStep(int step)
        {
            if (_segments <= 0 || _preview == step) return;
            _preview = step;
            Refresh();
        }

        public void ClearPreview()
        {
            if (_preview < 0) return;
            _preview = -1;
            Refresh();
        }

        /// Wraps at the ends, so a row can always be changed with one direction and
        /// activating it cycles - the menu offers no drag interaction.
        private void Step(int direction)
        {
            if (Int != null)
            {
                int next = Int.Value + direction;
                if (next > Int.Max) next = Int.Min;
                else if (next < Int.Min) next = Int.Max;
                Int.Value = next;
            }
            else if (Float != null)
            {
                // One diamond per press, matching the bar drawn below.
                float step = (Float.Max - Float.Min) / BarSegments;
                float next = Float.Value + step * direction;
                if (next > Float.Max + 0.0001f) next = Float.Min;
                else if (next < Float.Min - 0.0001f) next = Float.Max;
                Float.Value = next;
            }
            else if (Choice != null && Choice.Options.Length > 0)
            {
                int count = Choice.Options.Length;
                Choice.Value = Choice.Options[(Choice.Index + direction + count) % count];
            }

            _preview = -1;
            Refresh();
        }

        /// Diamonds currently filled.
        private int Filled
        {
            get
            {
                if (Float != null)
                {
                    float span = Float.Max - Float.Min;
                    if (span <= 0f) return 0;
                    return Mathf.RoundToInt((Float.Value - Float.Min) / span * _segments);
                }
                return Int != null ? Int.Value - Int.Min : 0;
            }
        }

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);

            string value;
            if (_segments > 0)
            {
                value = Bar(_preview >= 0 ? _preview : Filled, _segments);
            }
            else if (Float != null)
            {
                value = Float.Value.ToString("0.00");
            }
            else if (Int != null)
            {
                value = Int.Value.ToString();
            }
            else
            {
                value = Choice != null ? Choice.Value : string.Empty;
            }

            GameMenu.SetValue(this, value);
        }

        private static string Bar(int filled, int total)
        {
            var bar = new System.Text.StringBuilder(total);
            for (int i = 0; i < total; i++) bar.Append(i < filled ? '\u2666' : '\u2662');
            return bar.ToString();
        }
    }

    /// Re-collects a page's rows into menuOptions as it is shown.
    ///
    /// RadicalMenu.Activate drives selection and collider enabling from menuOptions,
    /// and fills that list in Awake only. On a menu built by replacing the
    /// template's rows the list can be stale, leaving rows that render but cannot be
    /// selected or clicked. OnEnable runs before Activate's loop, so this is the
    /// right moment to correct it.
    public class QolPageInit : MonoBehaviour
    {
        public RadicalMenu Menu;
        public string Title;

        private bool _pending;

        private void OnEnable() => _pending = true;

        private void Update()
        {
            if (!_pending || Menu == null) return;
            _pending = false;

            GameMenu.Refresh(Menu);

            // Applied here rather than at build time: RadicalMenu.OnEnable re-renders
            // every descendant PugText when the menu is shown, which overwrote a
            // title set earlier and left the cloned template's heading.
            var heading = GameMenu.FindHeading(Menu);
            if (heading != null) GameMenu.SetLiteral(heading, Title);

            int selectable = 0;
            foreach (var row in Menu.menuOptions)
            {
                if (row != null && row.IsSelectionEnabled()) selectable++;
            }
            Debug.Log($"[CkQol] page '{Title}': rows={Menu.menuOptions.Count} " +
                      $"selectable={selectable} heading={(heading != null ? "found" : "missing")}");
        }
    }
}
