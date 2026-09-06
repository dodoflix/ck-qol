using System;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Opens a submenu. Used for the mod's entry in the game's Options menu and for
    /// each feature's entry in the mod's own menu.
    public class QolSubmenuOption : RadicalMenuOption
    {
        public RadicalMenu Target;
        public string Label;

        public override void OnActivated()
        {
            base.OnActivated();
            if (Target != null) Manager.menu.PushMenu(Target);
        }

        protected override void Awake()
        {
            base.Awake();
            GameMenu.SetLabel(this, Label);
        }
    }

    /// Leaves the current menu. RadicalMenu handles back/escape itself, but a
    /// visible row matters for controller and mouse users who never press escape.
    public class QolBackOption : RadicalMenuOption
    {
        public string Label = "Back";

        public override void OnActivated()
        {
            base.OnActivated();
            Manager.menu.PopMenu();
        }

        protected override void Awake()
        {
            base.Awake();
            GameMenu.SetLabel(this, Label);
        }
    }

    /// On/off row bound to a BoolSetting.
    ///
    /// Derives from the game's own toggle so the tick sprites and selection marker
    /// keep working; only the backing store changes.
    public class QolToggleOption : RadicalMenuOption_Toggle
    {
        public BoolSetting Setting;
        public string Label;

        public override bool IsOn() => Setting != null && Setting.Value;

        public override void OnActivated()
        {
            if (Setting != null) Setting.Value = !Setting.Value;
            base.OnActivated();
            Refresh();
        }

        protected override void Awake()
        {
            base.Awake();
            Refresh();
        }

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);
            isOn = IsOn();
        }
    }

    /// Numeric row bound to an Int or Float setting, using the game's own slider.
    public class QolSliderOption : RadicalOptionsMenuOption_Slider
    {
        public IntSetting IntSetting;
        public FloatSetting FloatSetting;
        public string Label;

        protected override void Awake()
        {
            base.Awake();
            GameMenu.SetLabel(this, Label);

            if (IntSetting != null)
            {
                SetValueRange(IntSetting.Min, IntSetting.Max);
                SetValue(IntSetting.Value);
            }
            else if (FloatSetting != null)
            {
                SetValueRange(FloatSetting.Min, FloatSetting.Max);
                SetValue(FloatSetting.Value);
            }

            ValueChanged += OnSliderChanged;
        }

        private void OnDestroy()
        {
            ValueChanged -= OnSliderChanged;
        }

        private void OnSliderChanged(float value, int step)
        {
            if (IntSetting != null) IntSetting.Value = Mathf.RoundToInt(value);
            else if (FloatSetting != null) FloatSetting.Value = value;
        }
    }

    /// Pick-one row. Rendered as a slider over the option indices because the game
    /// has no dedicated multi-choice row, and a slider already supports left/right
    /// skim on a controller.
    public class QolChoiceOption : RadicalOptionsMenuOption_Slider
    {
        public ChoiceSetting Setting;
        public string Label;

        protected override void Awake()
        {
            base.Awake();
            GameMenu.SetLabel(this, Label);

            if (Setting != null)
            {
                SetValueRange(0f, Mathf.Max(0f, Setting.Options.Length - 1));
                SetValue(Setting.Index);
                GameMenu.SetValue(this, Setting.Value);
            }

            ValueChanged += OnSliderChanged;
        }

        private void OnDestroy()
        {
            ValueChanged -= OnSliderChanged;
        }

        private void OnSliderChanged(float value, int step)
        {
            if (Setting == null) return;
            int index = Mathf.Clamp(Mathf.RoundToInt(value), 0, Setting.Options.Length - 1);
            Setting.Value = Setting.Options[index];
            GameMenu.SetValue(this, Setting.Value);
        }
    }
}
