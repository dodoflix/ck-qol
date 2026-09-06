using System;
using PugMod;
using UnityEngine;

namespace CkQol.Config
{
    /// One configurable value, backed by PugMod's config so it persists.
    ///
    /// Deliberately UI-agnostic: the menu inspects the concrete type and builds the
    /// right widget. Value writes through immediately, so features should read the
    /// property every time rather than caching it at init - that is what makes the
    /// menu edits take effect live.
    public abstract class ModSetting
    {
        public string Key { get; protected set; }
        public string Label { get; protected set; }
        public string Tooltip { get; protected set; }

        /// Raised after the value changes, from the menu or from code.
        public event Action<ModSetting> Changed;

        /// Registers with PugMod config under [section]. Called once at load.
        public abstract void Bind(string mod, string section);

        protected void RaiseChanged() => Changed?.Invoke(this);
    }

    public class BoolSetting : ModSetting
    {
        private readonly bool _default;
        private IConfigEntry<bool> _entry;
        private bool _fallback;

        public BoolSetting(string key, string label, bool defaultValue, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            _default = defaultValue; _fallback = defaultValue;
        }

        public bool Value
        {
            get => _entry != null ? _entry.Value : _fallback;
            set
            {
                if (Value == value) return;
                if (_entry != null) _entry.Value = value; else _fallback = value;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section) =>
            _entry = API.Config.Register(mod, section, Tooltip, Key, _default);
    }

    public class IntSetting : ModSetting
    {
        private readonly int _default;
        private IConfigEntry<int> _entry;
        private int _fallback;

        public int Min { get; }
        public int Max { get; }

        public IntSetting(string key, string label, int defaultValue, int min, int max, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            _default = defaultValue; _fallback = defaultValue; Min = min; Max = max;
        }

        public int Value
        {
            get => Mathf.Clamp(_entry != null ? _entry.Value : _fallback, Min, Max);
            set
            {
                int v = Mathf.Clamp(value, Min, Max);
                if (Value == v) return;
                if (_entry != null) _entry.Value = v; else _fallback = v;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section) =>
            _entry = API.Config.Register(mod, section, Tooltip, Key, _default);
    }

    public class FloatSetting : ModSetting
    {
        private readonly float _default;
        private IConfigEntry<float> _entry;
        private float _fallback;

        public float Min { get; }
        public float Max { get; }

        public FloatSetting(string key, string label, float defaultValue, float min, float max, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            _default = defaultValue; _fallback = defaultValue; Min = min; Max = max;
        }

        public float Value
        {
            get => Mathf.Clamp(_entry != null ? _entry.Value : _fallback, Min, Max);
            set
            {
                float v = Mathf.Clamp(value, Min, Max);
                if (Mathf.Approximately(Value, v)) return;
                if (_entry != null) _entry.Value = v; else _fallback = v;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section) =>
            _entry = API.Config.Register(mod, section, Tooltip, Key, _default);
    }

    /// Pick one of a fixed list. Stored as the option string, not its index, so
    /// reordering the options later cannot silently change anyone's setting.
    public class ChoiceSetting : ModSetting
    {
        private readonly string _default;
        private IConfigEntry<string> _entry;
        private string _fallback;

        public string[] Options { get; }

        public ChoiceSetting(string key, string label, string[] options, string defaultValue, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            Options = options; _default = defaultValue; _fallback = defaultValue;
        }

        public string Value
        {
            get
            {
                string v = (_entry != null ? _entry.Value : _fallback) ?? _default;
                return Array.IndexOf(Options, v) >= 0 ? v : _default;
            }
            set
            {
                if (Value == value) return;
                if (_entry != null) _entry.Value = value; else _fallback = value;
                RaiseChanged();
            }
        }

        public int Index => Mathf.Max(0, Array.IndexOf(Options, Value));

        public override void Bind(string mod, string section) =>
            _entry = API.Config.Register(mod, section, Tooltip, Key, _default);
    }
}
