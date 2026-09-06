using System;
using System.Collections.Generic;
using PugMod;
using UnityEngine;

namespace CkQol
{
    /// Entry point. Owns the feature list, their config toggles, and forwards the
    /// PugMod lifecycle to whichever features are enabled.
    ///
    /// A feature that throws is disabled rather than allowed to take the mod (or the
    /// game) down with it - one broken tweak should not cost you the other ten.
    public class CkQolMod : IMod
    {
        public const string ModName = "CkQol";

        private readonly List<IQolFeature> _active = new List<IQolFeature>();

        /// Register features here. Order is load order.
        private static IEnumerable<IQolFeature> BuildFeatures()
        {
            yield return new Features.LoadProbe();
        }

        public void EarlyInit()
        {
        }

        public void Init()
        {
            foreach (var feature in BuildFeatures())
            {
                bool enabled;
                try
                {
                    enabled = API.Config
                        .Register(ModName, "Features", feature.Description,
                                  feature.Name, feature.EnabledByDefault)
                        .Value;
                }
                catch (Exception e)
                {
                    LogError($"config registration failed for {feature.Name}, using default", e);
                    enabled = feature.EnabledByDefault;
                }

                if (!enabled)
                {
                    Log($"{feature.Name}: disabled by config");
                    continue;
                }

                if (Guard(feature, "Init", feature.Init))
                {
                    _active.Add(feature);
                }
            }

            API.Client.OnWorldCreated += OnWorldCreated;
            API.Client.OnWorldDestroyed += OnWorldDestroyed;

            Log(_active.Count == 0
                ? "loaded with no active features"
                : $"loaded with {_active.Count} feature(s): {string.Join(", ", _active.ConvertAll(f => f.Name))}");
        }

        public void Shutdown()
        {
            API.Client.OnWorldCreated -= OnWorldCreated;
            API.Client.OnWorldDestroyed -= OnWorldDestroyed;

            foreach (var feature in _active)
            {
                Guard(feature, "Shutdown", feature.Shutdown);
            }
            _active.Clear();
        }

        public void ModObjectLoaded(UnityEngine.Object obj)
        {
        }

        public void Update()
        {
            // Reverse so a feature that faults can drop itself mid-iteration.
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var feature = _active[i];
                if (!Guard(feature, "Update", feature.Update))
                {
                    _active.RemoveAt(i);
                }
            }
        }

        private void OnWorldCreated()
        {
            foreach (var feature in _active)
            {
                Guard(feature, "OnWorldCreated", feature.OnWorldCreated);
            }
        }

        private void OnWorldDestroyed()
        {
            foreach (var feature in _active)
            {
                Guard(feature, "OnWorldDestroyed", feature.OnWorldDestroyed);
            }
        }

        /// Runs a feature callback, reporting and reporting-once on failure.
        /// Returns false if the feature threw and should be dropped.
        private static bool Guard(IQolFeature feature, string stage, Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception e)
            {
                LogError($"{feature.Name}.{stage} threw, disabling feature", e);
                return false;
            }
        }

        private static void Log(string message)
        {
            Debug.Log($"[{ModName}] {message}");
        }

        private static void LogError(string message, Exception e = null)
        {
            Debug.LogError($"[{ModName}] {message}");
            if (e != null)
            {
                Debug.LogException(e);
            }
        }
    }
}
