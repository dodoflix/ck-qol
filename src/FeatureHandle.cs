using System;
using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol
{
    /// Wraps a feature with its Enabled toggle and settings. Owns the running state,
    /// so flipping the toggle calls Init or Shutdown at once.
    public class FeatureHandle
    {
        public IQolFeature Feature { get; }
        public BoolSetting Enabled { get; }
        public IReadOnlyList<ModSetting> Settings { get; }

        /// False for a feature with no on/off switch of its own.
        public bool CanBeDisabled { get; }

        public string Name => Feature.Name;
        public string Description => Feature.Description;

        /// Init has run and Shutdown has not.
        public bool Running { get; private set; }

        /// Set once it throws, after which we stop calling into it.
        public bool Faulted { get; private set; }

        private bool _worldAlive;

        public FeatureHandle(IQolFeature feature, bool canBeDisabled = true)
        {
            Feature = feature;
            CanBeDisabled = canBeDisabled;
            Enabled = new BoolSetting("Enabled", "Enabled", feature.EnabledByDefault,
                                      feature.Description);

            var settings = new List<ModSetting>();
            foreach (var setting in feature.GetSettings())
            {
                settings.Add(setting);
            }
            Settings = settings;
        }

        public void Bind(string mod)
        {
            Enabled.Bind(mod, Name);
            foreach (var setting in Settings)
            {
                setting.Bind(mod, Name);
            }
            Enabled.Changed += _ => Apply();
        }

        /// Restores every setting, including Enabled.
        public void ResetToDefaults()
        {
            Enabled.ResetToDefault();
            foreach (var setting in Settings)
            {
                setting.ResetToDefault();
            }
        }

        /// Starts or stops to match the Enabled toggle.
        public void Apply()
        {
            if (Faulted) return;

            bool want = !CanBeDisabled || Enabled.Value;
            if (want == Running) return;

            if (want)
            {
                if (!Guard("Init", Feature.Init)) return;
                Running = true;
                // Switched on mid-session, it still needs the callback it missed.
                if (_worldAlive) Guard("OnWorldCreated", Feature.OnWorldCreated);
            }
            else
            {
                Guard("Shutdown", Feature.Shutdown);
                Running = false;
            }
        }

        public void WorldCreated()
        {
            _worldAlive = true;
            if (Running) Guard("OnWorldCreated", Feature.OnWorldCreated);
        }

        public void WorldDestroyed()
        {
            _worldAlive = false;
            if (Running) Guard("OnWorldDestroyed", Feature.OnWorldDestroyed);
        }

        public void Update()
        {
            if (Running) Guard("Update", Feature.Update);
        }

        public void Shutdown()
        {
            if (!Running) return;
            Guard("Shutdown", Feature.Shutdown);
            Running = false;
        }

        /// A throw disables that feature and leaves the rest running.
        private bool Guard(string stage, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                Faulted = true;
                Running = false;
                Debug.LogError($"[CkQol] {Name}.{stage} threw, feature disabled");
                Debug.LogException(e);
                return false;
            }
        }
    }
}
