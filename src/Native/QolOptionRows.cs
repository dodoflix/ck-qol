using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Rows follow the shape of the game's own option scripts (see
    /// RadicalOptionsMenuOption_ScreenShake): label in labelText, current value in
    /// valueText, OnActivated flips it, skim left/right does the same.

    /// Opens a submenu.
    public class QolSubmenuOption : RadicalPauseMenuOption
    {
        public RadicalMenu Target;
        public string Label;
        public bool LogState;

        /// Set when this row was appended to a menu that did not have room for it.
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

            if (Owner != null) GameMenu.LayoutWithGame(Owner, this);

            if (!LogState) return;
            string render = labelText == null ? "no labelText"
                : $"textLocal={labelText.transform.localPosition} " +
                  $"textWorld={labelText.transform.position} " +
                  $"dims={labelText.dimensions.size} text='{labelText.GetText()}'";

            // Compare against a stock row: if ours differs only in Y we are placed
            // correctly, and anything else points at where it is actually drawing.
            string reference = "no reference";
            if (Owner != null)
            {
                foreach (var other in Owner.menuOptions)
                {
                    if (other == null || other == this || other.labelText == null) continue;
                    if (!other.gameObject.activeSelf) continue;
                    reference = $"stock '{other.labelText.GetText()}' " +
                                $"rowWorld={other.transform.position} " +
                                $"textWorld={other.labelText.transform.position}";
                    break;
                }
            }

            Debug.Log($"[CkQol] after layout: rowWorld={transform.position} {render} | {reference}");
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

    /// Drives a cloned game slider from one of our settings.
    ///
    /// A companion component rather than a subclass: the slider holds private
    /// [SerializeField] visual references, so replacing its script would discard
    /// them and leave a slider that cannot draw itself.
    public class QolSliderBinding : MonoBehaviour
    {
        public RadicalOptionsMenuOption_Slider Slider;
        public string Label;
        public IntSetting Int;
        public FloatSetting Float;
        public ChoiceSetting Choice;

        private void Start()
        {
            if (Slider == null) return;
            GameMenu.SetLabel(Slider, Label);

            if (Int != null)
            {
                Slider.SetValueRange(Int.Min, Int.Max);
                Slider.SetValue(Int.Value);
            }
            else if (Float != null)
            {
                Slider.SetValueRange(Float.Min, Float.Max);
                Slider.SetValue(Float.Value);
            }
            else if (Choice != null)
            {
                Slider.SetValueRange(0f, Mathf.Max(0f, Choice.Options.Length - 1));
                Slider.SetValue(Choice.Index);
                GameMenu.SetValue(Slider, Choice.Value);
            }

            Slider.ValueChanged += OnChanged;
        }

        private void OnDestroy()
        {
            if (Slider != null) Slider.ValueChanged -= OnChanged;
        }

        private void OnChanged(float value, int step)
        {
            if (Int != null)
            {
                Int.Value = Mathf.RoundToInt(value);
            }
            else if (Float != null)
            {
                Float.Value = value;
            }
            else if (Choice != null && Choice.Options.Length > 0)
            {
                int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, Choice.Options.Length - 1);
                Choice.Value = Choice.Options[index];
                GameMenu.SetValue(Slider, Choice.Value);
            }
        }
    }
}
