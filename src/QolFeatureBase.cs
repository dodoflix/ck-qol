using UnityEngine;

namespace CkQol
{
    /// Default no-op implementations so a feature only overrides what it needs.
    public abstract class QolFeatureBase : IQolFeature
    {
        public abstract string Name { get; }
        public abstract string Description { get; }
        public virtual bool EnabledByDefault => true;

        public virtual void Init() { }
        public virtual void OnWorldCreated() { }
        public virtual void OnWorldDestroyed() { }
        public virtual void Update() { }
        public virtual void Shutdown() { }

        protected void Log(string message)
        {
            Debug.Log($"[CkQol/{Name}] {message}");
        }

        protected void LogError(string message)
        {
            Debug.LogError($"[CkQol/{Name}] {message}");
        }
    }
}
