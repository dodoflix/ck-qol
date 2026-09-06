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

    /// Re-collects and re-lays out a menu the first frame after it is shown.
    ///
    /// RadicalMenu fills menuOptions in Awake only. If Awake already ran before our
    /// rows were parented, they render but are absent from the layout list, so
    /// UpdatePosition never moves them off the donor's slot and they sit on top of
    /// a stock row. Deferred by a frame rather than done in OnEnable because the
    /// menu is mid-iteration over menuOptions while activating.
    public class QolMenuRefresher : MonoBehaviour
    {
        public RadicalMenu Menu;
        private bool _pending;

        private void OnEnable() => _pending = true;

        private void Update()
        {
            if (!_pending) return;
            _pending = false;
            GameMenu.Refresh(Menu);
        }
    }
}
