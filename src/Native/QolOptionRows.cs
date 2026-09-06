using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Native
{
    /// Rows follow the stock option scripts: label in labelText, value in valueText,
    /// OnActivated and skim left/right change it.

    /// Opens a submenu.
    public class QolSubmenuOption : RadicalPauseMenuOption
    {
        public RadicalMenu Target;
        public string Label;

        /// Shown on hover. On the root page this is the feature's description.
        public string Tooltip;

        /// Set when the menu needs re-laying out once open.
        public RadicalMenu Owner;

        private bool _layoutPending;

        public override void OnParentMenuActivation()
        {
            base.OnParentMenuActivation();
            // Defer: this fires mid-Activate, before rows are active and before
            // Start, so laying out here misses rows and reads stale labels.
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

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Tooltip);
    }

    /// Restores a feature page's settings, behind the game's own confirm dialog.
    /// The button labels are the game's localization keys; only the question is ours,
    /// hence localize: false.
    public class QolResetOption : RadicalPauseMenuOption
    {
        public FeatureHandle Feature;
        public string Label = "Reset to defaults";

        private void Start()
        {
            GameMenu.SetLabel(this, Label);
            GameMenu.ClearValue(this);
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (Feature == null) return;

            Manager.menu.centerPopUpText.StartNewDisplaySequence(
                "Reset " + Feature.Name + " settings to their defaults?",
                options: new List<string> { "cancelDialogue", "confirm" },
                optionsCallback: OnAnswered,
                localize: false,
                menuInputCooldown: true,
                fadeTime: 0f,
                staticTime: 1.5f,
                useUnscaledTime: true,
                textBackgroundAlpha: 1f,
                minWidth: 10f,
                backgroundAlpha: 0.95f,
                textMaxWidth: 18f,
                pauseGame: true);
        }

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Feature != null
                ? "Restores every setting on this page, including whether the feature is enabled."
                : null);

        /// Option 0 cancels, option 1 confirms.
        private void OnAnswered(PopupResponse response)
        {
            if (!response.IsConfirm || Feature == null) return;
            Feature.ResetToDefaults();
            Debug.Log($"[CkQol] reset '{Feature.Name}' settings to defaults");
        }
    }

    /// Leaves the menu. Escape does this too, but mouse users need a row.
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

        private void Start()
        {
            Refresh();
            // Redraw when the value moves without this row doing it.
            if (Setting != null) Setting.Changed += _ => Refresh();
        }

        public override bool IsOn() => Setting != null && Setting.Value;

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Setting != null ? Setting.Tooltip : null);

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
    /// Not RadicalOptionsMenuOption_Slider: nothing instantiates it, and its step
    /// count and glyphs are private with no setter. The audio "slider" is text -
    /// RadicalOptionsMenuOption_Volume renders eight diamonds into valueText.
    public class QolNumberOption : RadicalPauseMenuOption
    {
        public IntSetting Int;
        public FloatSetting Float;
        public ChoiceSetting Choice;
        public string Label;

        /// Only volume clones have the diamond glyphs; others render '?'.
        public bool CanDrawBar;

        /// Diamonds to draw, or zero for a plain value.
        private int _segments;


        /// Matches the eight steps the game's volume rows use.
        public const int BarSegments = 8;

        /// Zero keeps a setting numeric: a bar cannot show which of twenty values is
        /// selected.
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

            ModSetting bound = Bound;
            if (bound != null) bound.Changed += _ => Refresh();
        }

        /// Whichever of the three a row was built for.
        private ModSetting Bound => Float ?? (Int ?? (ModSetting)Choice);

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Bound != null ? Bound.Tooltip : null);

        /// Clicking the label drops a bar row to its minimum, as an audio row mutes.
        /// Rows without a bar cycle instead.
        public override void OnActivated()
        {
            base.OnActivated();
            if (_segments > 0) SetStep(0);
            else Step(1);
        }

        public override void OnSelected()
        {
            base.OnSelected();
            if (_segments > 0) PreviewStep(Filled);
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            ClearPreview();
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

        /// Step 1 is the first diamond, so as with volume rows the low end is
        /// reachable by stepping but not by clicking.
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

            Refresh();
            PreviewStep(step);
        }

        /// Highlights up to the pointer as PreSelectVolume does: the characters keep
        /// showing the stored value, only the colours change.
        public void PreviewStep(int step)
        {
            if (_segments <= 0 || valueText == null) return;
            var glyphs = valueText.glyphs;
            for (int i = 0; i < glyphs.Count; i++)
            {
                glyphs[i].color = i < step
                    ? PugTextEffectMenuOption.SELECTED_VALUE_COLOR
                    : PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR;
            }
        }

        public void ClearPreview()
        {
            if (_segments <= 0 || valueText == null) return;
            var glyphs = valueText.glyphs;
            for (int i = 0; i < glyphs.Count; i++)
            {
                glyphs[i].color = PugTextEffectMenuOption.UNSELECTED_TEXT_COLOR;
            }
        }

        /// Wraps, so one direction can reach every value.
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

            Refresh();
            if (_segments > 0) PreviewStep(Filled);
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
                value = Bar(Filled, _segments);
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

    /// Text field row.
    ///
    /// Implements InputManager.TextInputInterface, so typing, paste, IME and the
    /// controller keyboard all come from MenuManager.HandleTypingInput. The stock
    /// RadicalMenuOptionTextInput is not cloned: its Update override skips
    /// base.Update, so it never sizes a click collider.
    public class QolTextOption : RadicalPauseMenuOption, InputManager.TextInputInterface
    {
        public StringSetting Setting;
        public string Label;

        private string _editing = string.Empty;
        private int _caret;
        private bool _editingActive;

        public bool WasAutoActivated { get; set; }

        public int MaxCharactersForOnScreenKeyboard =>
            Setting != null ? Setting.MaxLength : 32;

        private void Start()
        {
            Refresh();
            if (Setting != null) Setting.Changed += _ => Refresh();
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (Setting == null || _editingActive) return;

            _editingActive = true;
            _editing = Setting.Value ?? string.Empty;
            _caret = _editing.Length;
            Manager.input.SetActiveInputField(this);
            Refresh();
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            // Commit rather than silently dropping what was typed.
            if (_editingActive) Deactivate(commit: true);
        }

        public string GetInputText() => _editing;

        public void SetInputText(string input)
        {
            _editing = Trim(input);
            _caret = _editing.Length;
            Refresh();
        }

        public void AppendString(string input)
        {
            if (string.IsNullOrEmpty(input)) return;

            // inputString carries backspace and CR; MenuManager handles those.
            var text = new System.Text.StringBuilder(input.Length);
            foreach (char c in input)
            {
                if (c >= ' ' && c != '\u007f') text.Append(c);
            }
            if (text.Length == 0) return;

            _caret = Mathf.Clamp(_caret, 0, _editing.Length);
            _editing = Trim(_editing.Insert(_caret, text.ToString()));
            _caret = Mathf.Min(_caret + text.Length, _editing.Length);
            WasAutoActivated = false;
            Refresh();
        }

        public void MoveCharMarker(int n)
        {
            _caret = Mathf.Clamp(_caret + n, 0, _editing.Length);
            Refresh();
        }

        public void RemoveCharAtMarker()
        {
            if (_caret >= _editing.Length) return;
            _editing = _editing.Remove(_caret, 1);
            Refresh();
        }

        public void RemoveCharBehindMarker()
        {
            if (_caret <= 0) return;
            _editing = _editing.Remove(--_caret, 1);
            Refresh();
        }

        public string GetHintString() => Label ?? string.Empty;

        public bool IsHidden() => false;

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Setting != null ? Setting.Tooltip : null);

        /// commit is false when the player pressed escape.
        public void Deactivate(bool commit)
        {
            _editingActive = false;
            Manager.input.SetActiveInputField(null);

            if (Setting != null)
            {
                if (commit) Setting.Value = _editing;
                else _editing = Setting.Value;
            }
            Refresh();
        }

        private string Trim(string text)
        {
            text = text ?? string.Empty;
            int max = MaxCharactersForOnScreenKeyboard;
            return text.Length > max ? text.Substring(0, max) : text;
        }

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);

            string shown = Setting != null ? Setting.Value : string.Empty;
            if (_editingActive) shown = _editing + "_";
            GameMenu.SetValue(this, string.IsNullOrEmpty(shown) ? "..." : shown);
        }
    }

    /// Rebindable key row. The game's control mapper maps Rewired actions, which a
    /// mod cannot add at runtime, so keys are polled through Rewired directly.
    public class QolKeybindOption : RadicalPauseMenuOption
    {
        public KeySetting Setting;
        public string Label;

        private bool _listening;

        /// The keypress that opened the row is still down on the next frame.
        private int _listenFrom;

        private void Start()
        {
            Refresh();
            if (Setting != null) Setting.Changed += _ => Refresh();
        }

        public override void OnActivated()
        {
            base.OnActivated();
            if (Setting == null || _listening) return;

            _listening = true;
            _listenFrom = Time.frameCount + 1;
            Refresh();
        }

        protected override void Update()
        {
            base.Update();
            if (!_listening || Time.frameCount < _listenFrom) return;

            var keyboard = KeySetting.Keyboard;
            if (keyboard == null) return;

            if (keyboard.GetKeyDown(KeyCode.Escape))
            {
                Stop();
                return;
            }

            var pressed = keyboard.PollForFirstKeyDown();
            if (!pressed.success) return;

            Setting.Value = pressed.keyboardKey;
            Stop();
        }

        public override void OnDeselected(bool playEffect = true)
        {
            base.OnDeselected(playEffect);
            if (_listening) Stop();
        }

        private void Stop()
        {
            _listening = false;
            Refresh();
        }

        public override List<TextAndFormatFields> GetHoverDescription() =>
            GameMenu.Hover(Setting != null ? Setting.Tooltip : null);

        private void Refresh()
        {
            GameMenu.SetLabel(this, Label);
            GameMenu.SetValue(this, _listening ? "press a key"
                                               : Setting != null ? Setting.Name : "none");
        }
    }

    /// Re-collects a page's rows into menuOptions as it is shown.
    ///
    /// Activate drives selection and colliders from that list but fills it in Awake
    /// only, so on a rebuilt menu it is stale and rows render without being
    /// selectable. OnEnable runs before Activate's loop.
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

            // Not at build time: OnEnable re-renders every descendant PugText, which
            // overwrote a title set earlier.
            var heading = GameMenu.FindHeading(Menu);
            if (heading != null) GameMenu.SetLiteral(heading, Title);
        }
    }
}
