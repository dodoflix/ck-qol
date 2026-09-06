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

        /// A number short enough for a HUD row: 2148 reads as 2.1K, 2148000 as 2.1M.
        /// A panel sized for four digits has no room for a chest holding nine
        /// thousand of something.
        protected static string Compact(long value)
        {
            if (value < 1000) return value.ToString();

            string[] steps = { "K", "M", "B" };
            double left = value;

            for (int i = 0; i < steps.Length; i++)
            {
                left /= 1000d;
                if (left < 1000d || i == steps.Length - 1)
                {
                    // One decimal below ten, none above: 9.4K, then 94K.
                    return left < 10d
                        ? left.ToString("0.#") + steps[i]
                        : System.Math.Floor(left) + steps[i];
                }
            }
            return value.ToString();
        }

        protected void Log(string message) => Debug.Log($"[CkQol/{Name}] {message}");
        protected void LogError(string message) => Debug.LogError($"[CkQol/{Name}] {message}");
    }
}
