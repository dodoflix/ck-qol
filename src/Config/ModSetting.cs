using System;
using PugMod;
using UnityEngine;

namespace CkQol.Config
{
    /// One configurable value, backed by PugMod's config so it persists.
    ///
    /// Deliberately UI-agnostic: the menu inspects the concrete type and builds the
    /// right widget.
    ///
    /// The live value is held here, not read back from PugMod. ModConfigEntry's
    /// getter re-reads and re-parses the JSON file on every access, so a feature
    /// polling a setting each frame would be reading from disk sixty times a second.
    /// The entry is touched once at Bind and then only on write, which also keeps the
    /// value readable from simulation code.
    public abstract class ModSetting
    {
        public string Key { get; protected set; }
        public string Label { get; protected set; }
        public string Tooltip { get; protected set; }

        /// Raised after the value changes, from the menu or from code.
        public event Action<ModSetting> Changed;

        /// Registers with PugMod config under [section]. Called once at load.
        public abstract void Bind(string mod, string section);

        /// Restores the value the setting was constructed with. Raises Changed, so
        /// menu rows redraw themselves.
        public abstract void ResetToDefault();

        protected void RaiseChanged() => Changed?.Invoke(this);

        /// Registers with PugMod, working around it dropping the metadata.
        ///
        /// ModAPIConfig.Register creates a brand-new file through Set(), which writes
        /// only mod/section/key/value - the description and defaultValue it was given
        /// are kept in memory and never reach disk. Every later run then reads that
        /// incomplete file back, so the fields stay empty forever. Writing once while
        /// the in-memory record is still the complete one persists them.
        ///
        /// Only for a file that does not exist yet: on an existing one the record
        /// already came from disk, so this would rewrite it with the default and
        /// throw away whatever the player had set.
        protected static IConfigEntry<T> Register<T>(string mod, string section,
                                                     string description, string key,
                                                     T defaultValue)
        {
            bool isNew = !API.Config.TryGet<T>(mod, section, key, out _);
            var entry = API.Config.Register(mod, section, description, key, defaultValue);
            if (isNew) entry.Value = defaultValue;
            return entry;
        }
    }

    public class BoolSetting : ModSetting
    {
        private readonly bool _default;
        private IConfigEntry<bool> _entry;

        // volatile: read from ECS simulation code, written from the menu.
        private volatile bool _value;

        public BoolSetting(string key, string label, bool defaultValue, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            _default = defaultValue; _value = defaultValue;
        }

        public bool Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                if (_entry != null) _entry.Value = value;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, _default);
            _value = _entry.Value;
        }

        public override void ResetToDefault() => Value = _default;
    }

    public class IntSetting : ModSetting
    {
        private readonly int _default;
        private IConfigEntry<int> _entry;
        private volatile int _value;

        public int Min { get; }
        public int Max { get; }

        public IntSetting(string key, string label, int defaultValue, int min, int max, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            Min = min; Max = max;
            _default = Mathf.Clamp(defaultValue, min, max);
            _value = _default;
        }

        public int Value
        {
            get => _value;
            set
            {
                int v = Mathf.Clamp(value, Min, Max);
                if (_value == v) return;
                _value = v;
                if (_entry != null) _entry.Value = v;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, _default);
            _value = Mathf.Clamp(_entry.Value, Min, Max);
        }

        public override void ResetToDefault() => Value = _default;
    }

    public class FloatSetting : ModSetting
    {
        private readonly float _default;
        private IConfigEntry<float> _entry;
        private volatile float _value;

        public float Min { get; }
        public float Max { get; }

        public FloatSetting(string key, string label, float defaultValue, float min, float max, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            Min = min; Max = max;
            _default = Mathf.Clamp(defaultValue, min, max);
            _value = _default;
        }

        public float Value
        {
            get => _value;
            set
            {
                float v = Mathf.Clamp(value, Min, Max);
                if (Mathf.Approximately(_value, v)) return;
                _value = v;
                if (_entry != null) _entry.Value = v;
                RaiseChanged();
            }
        }

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, _default);
            _value = Mathf.Clamp(_entry.Value, Min, Max);
        }

        public override void ResetToDefault() => Value = _default;
    }

    /// Free text.
    public class StringSetting : ModSetting
    {
        private readonly string _default;
        private IConfigEntry<string> _entry;
        private volatile string _value;

        public int MaxLength { get; }

        public StringSetting(string key, string label, string defaultValue,
                             int maxLength = 32, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            MaxLength = Mathf.Max(1, maxLength);
            _default = Clamp(defaultValue);
            _value = _default;
        }

        public string Value
        {
            get => _value;
            set
            {
                string v = Clamp(value);
                if (_value == v) return;
                _value = v;
                if (_entry != null) _entry.Value = v;
                RaiseChanged();
            }
        }

        private string Clamp(string text)
        {
            text = text ?? string.Empty;
            return text.Length > MaxLength ? text.Substring(0, MaxLength) : text;
        }

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, _default);
            _value = Clamp(_entry.Value);
        }

        public override void ResetToDefault() => Value = _default;
    }

    /// A rebindable key.
    ///
    /// Stored as the numeric KeyCode rather than its name: turning a name back into
    /// the enum needs Enum.Parse, and the game's own input stack is queried by
    /// KeyCode anyway.
    public class KeySetting : ModSetting
    {
        private readonly KeyCode _default;
        private IConfigEntry<int> _entry;
        private volatile int _value;

        public KeySetting(string key, string label, KeyCode defaultValue, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            _default = defaultValue; _value = (int)defaultValue;
        }

        public KeyCode Value
        {
            get => (KeyCode)_value;
            set
            {
                if (_value == (int)value) return;
                _value = (int)value;
                if (_entry != null) _entry.Value = (int)value;
                RaiseChanged();
            }
        }

        /// Name as the game writes it in its own key hints.
        public string Name =>
            Value == KeyCode.None ? "none" : Rewired.Keyboard.GetKeyName(Value);

        /// True on the frame the key goes down.
        public bool WasPressed => Value != KeyCode.None && Keyboard != null &&
                                  Keyboard.GetKeyDown(Value);

        public bool IsHeld => Value != KeyCode.None && Keyboard != null &&
                              Keyboard.GetKey(Value);

        /// Null until Rewired has started, which is after mods load.
        internal static Rewired.Keyboard Keyboard =>
            Rewired.ReInput.isReady ? Rewired.ReInput.controllers.Keyboard : null;

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, (int)_default);
            _value = _entry.Value;
        }

        public override void ResetToDefault() => Value = _default;
    }

    /// Pick one of a fixed list. Stored as the option string, not its index, so
    /// reordering the options later cannot silently change anyone's setting.
    public class ChoiceSetting : ModSetting
    {
        private readonly string _default;
        private IConfigEntry<string> _entry;
        private volatile string _value;

        public string[] Options { get; }

        public ChoiceSetting(string key, string label, string[] options, string defaultValue, string tooltip = "")
        {
            Key = key; Label = label; Tooltip = tooltip;
            Options = options; _default = defaultValue; _value = defaultValue;
        }

        public string Value
        {
            get => _value;
            set
            {
                string v = Valid(value);
                if (_value == v) return;
                _value = v;
                if (_entry != null) _entry.Value = v;
                RaiseChanged();
            }
        }

        /// Falls back to the default rather than storing something not on the list.
        private string Valid(string v) =>
            v != null && Array.IndexOf(Options, v) >= 0 ? v : _default;

        public int Index => Mathf.Max(0, Array.IndexOf(Options, Value));

        public override void Bind(string mod, string section)
        {
            _entry = Register(mod, section, Tooltip, Key, _default);
            _value = Valid(_entry.Value);
        }

        public override void ResetToDefault() => Value = _default;
    }
}
