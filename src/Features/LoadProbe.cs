using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol.Features
{
    /// Placeholder proving the mod loads and the menu round-trips every widget type.
    /// Delete it once there are real features.
    public class LoadProbe : QolFeatureBase
    {
        public override string Name => "LoadProbe";

        public override string Description =>
            "Diagnostic placeholder. Logs lifecycle events and exercises each " +
            "setting widget so the menu can be checked end to end. Safe to disable.";

        private readonly BoolSetting _verbose =
            new BoolSetting("Verbose", "Verbose logging", false,
                            "Also log every frame tick. Noisy - leave off unless debugging.");

        private readonly IntSetting _count =
            new IntSetting("SampleCount", "Sample int", 5, 0, 20,
                           "Demonstrates the integer slider.");

        private readonly FloatSetting _scale =
            new FloatSetting("SampleScale", "Sample float", 1f, 0.1f, 4f,
                             "Demonstrates the float slider.");

        private readonly ChoiceSetting _mode =
            new ChoiceSetting("SampleMode", "Sample choice",
                              new[] { "Off", "Low", "High" }, "Low",
                              "Demonstrates the choice buttons.");

        private readonly StringSetting _label =
            new StringSetting("SampleLabel", "Sample text", "hello", 24,
                              "Demonstrates the text field.");

        private readonly KeySetting _hotkey =
            new KeySetting("SampleHotkey", "Sample hotkey", KeyCode.F9,
                           "Demonstrates rebinding. Logs a line when pressed.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _verbose;
            yield return _count;
            yield return _scale;
            yield return _mode;
            yield return _label;
            yield return _hotkey;
        }

        public override void Init()
        {
            Log($"started (count={_count.Value}, scale={_scale.Value:0.00}, mode={_mode.Value})");
        }

        public override void OnWorldCreated() => Log("client world created");

        public override void OnWorldDestroyed() => Log("client world destroyed");

        public override void Shutdown() => Log("stopped");

        public override void Update()
        {
            if (_verbose.Value) Log("tick");
            if (_hotkey.WasPressed) Log($"hotkey {_hotkey.Name} pressed (text={_label.Value})");
        }
    }
}
