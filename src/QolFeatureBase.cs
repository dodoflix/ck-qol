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

        public virtual void Init() { }
        public virtual void OnWorldCreated() { }
        public virtual void OnWorldDestroyed() { }
        public virtual void Update() { }
        public virtual void Shutdown() { }

        protected void Log(string message) => Debug.Log($"[CkQol/{Name}] {message}");
        protected void LogError(string message) => Debug.LogError($"[CkQol/{Name}] {message}");
    }
}
