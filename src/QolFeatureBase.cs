using System.Collections.Generic;
using CkQol.Config;
using UnityEngine;

namespace CkQol
{
    /// No-op defaults so a feature overrides only what it needs.
    public abstract class QolFeatureBase : IQolFeature
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual bool EnabledByDefault => true;

        public virtual IEnumerable<ModSetting> GetSettings() => new ModSetting[0];

        /// Whether the feature is switched on. Settings stay editable while it is off,
        /// so anything Apply writes has to account for that.
        protected bool Running { get; private set; }

        /// Mirror the settings into wherever the feature's work reads them. Called on
        /// start, on stop, and on every setting change.
        protected virtual void Apply() { }

        private bool _subscribed;

        public virtual void Init()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                foreach (var setting in GetSettings())
                {
                    setting.Changed += _ => Apply();
                }
            }

            Running = true;
            Apply();
        }

        public virtual void Shutdown()
        {
            Running = false;
            Apply();
        }

        public virtual void OnWorldCreated() { }
        public virtual void OnWorldDestroyed() { }
        public virtual void Update() { }

        protected void Log(string message) => Debug.Log($"[CkQol/{Name}] {message}");
        protected void LogError(string message) => Debug.LogError($"[CkQol/{Name}] {message}");
    }
}
