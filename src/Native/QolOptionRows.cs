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
    /// Not built on RadicalOptionsMenuOption_Slider: nothing in the stock menus uses
    /// that class, so there is no instance to clone. The game's own numeric rows
    /// (volume, vibration) are plain RadicalPauseMenuOption subclasses that step a
    /// value and write it to valueText, which is what this does.
    public class QolNumberOption : RadicalPauseMenuOption
    {
        public IntSetting Int;
        public FloatSetting Float;
        public ChoiceSetting Choice;
        public string Label;

        private void Start() => Refresh();

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
                // Twenty steps across the range keeps a fine setting usable.
                float step = (Float.Max - Float.Min) / 20f;
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

            Refresh();
        }

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);

            string value = Int != null ? Int.Value.ToString()
                : Float != null ? Float.Value.ToString("0.00")
                : Choice != null ? Choice.Value
                : string.Empty;
            GameMenu.SetValue(this, value);
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

        private void OnEnable()
        {
            if (Menu != null) GameMenu.Refresh(Menu);
        }
    }
}
