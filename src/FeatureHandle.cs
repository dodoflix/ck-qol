using System;
using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol
{
    /// Wraps a feature with its config-backed Enabled toggle and settings.
    ///
    /// Owns the running/stopped state so the menu's toggle takes effect immediately:
    /// flipping it calls Init or Shutdown rather than waiting for a game restart.
    public class FeatureHandle
    {
        public IQolFeature Feature { get; }
        public BoolSetting Enabled { get; }
        public IReadOnlyList<ModSetting> Settings { get; }

        /// A pseudo-feature (the General tab) has no on/off switch of its own.
        public bool CanBeDisabled { get; }

        public string Name => Feature.Name;
        public string Description => Feature.Description;

        /// True while the feature's Init has run and Shutdown has not.
        public bool Running { get; private set; }

        /// Set once a feature has faulted, so we stop calling into it.
        public bool Faulted { get; private set; }

        private bool _worldAlive;

        public FeatureHandle(IQolFeature feature, bool canBeDisabled = true)
        {
            Feature = feature;
            CanBeDisabled = canBeDisabled;
            Enabled = new BoolSetting("Enabled", "Enabled", feature.EnabledByDefault,
                                      feature.Description);

            var settings = new List<ModSetting> { };
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

        /// Restores every setting on this feature, including the Enabled toggle.
        public void ResetToDefaults()
        {
            Enabled.ResetToDefault();
            foreach (var setting in Settings)
            {
                setting.ResetToDefault();
            }
        }

        /// Starts or stops the feature to match the Enabled toggle.
        public void Apply()
        {
            if (Faulted) return;

            bool want = !CanBeDisabled || Enabled.Value;
            if (want == Running) return;

            if (want)
            {
                if (!Guard("Init", Feature.Init)) return;
                Running = true;
                // A feature switched on mid-session still needs the world callback it
                // missed, otherwise it sits idle until the next world load.
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

        /// One misbehaving feature should not take the mod - or the game - down, so
        /// a throw disables that feature and leaves the rest running.
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
