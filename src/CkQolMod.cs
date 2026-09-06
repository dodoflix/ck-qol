using System;
using System.Collections.Generic;
using CkQol.Config;
using CkQol.Native;
using PugMod;
using UnityEngine;

namespace CkQol
{
    /// Entry point: builds the features, binds their config, installs the pages into
    /// the game's Settings menu and forwards the PugMod lifecycle.
    public class CkQolMod : IMod
    {
        public const string ModName = "CkQol";

        private readonly List<FeatureHandle> _features = new List<FeatureHandle>();

        public IReadOnlyList<FeatureHandle> Features => _features;

        /// Register features here. Order is menu order.
        private static IEnumerable<IQolFeature> BuildFeatures()
        {
            yield return new Features.AutoFishing();
        }

        public void EarlyInit() { }

        public void Init()
        {
            foreach (var feature in BuildFeatures())
            {
                _features.Add(new FeatureHandle(feature));
            }

            foreach (var handle in _features)
            {
                try
                {
                    handle.Bind(ModName);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{ModName}] config bind failed for {handle.Name}");
                    Debug.LogException(e);
                }
                handle.Apply();
            }

            API.Client.OnWorldCreated += OnWorldCreated;
            API.Client.OnWorldDestroyed += OnWorldDestroyed;

            Debug.Log($"[{ModName}] loaded with {_features.Count} feature(s), " +
                      "settings live under Settings in the game menu");
        }

        public void Shutdown()
        {
            API.Client.OnWorldCreated -= OnWorldCreated;
            API.Client.OnWorldDestroyed -= OnWorldDestroyed;
            foreach (var handle in _features) handle.Shutdown();
        }

        public void ModObjectLoaded(UnityEngine.Object obj) { }

        public void Update()
        {
            // The options menus are built during startup, long after mods load.
            if (!NativeOptions.Installed && NativeOptions.MenusReady)
            {
                NativeOptions.Install(this);
            }

            for (int i = 0; i < _features.Count; i++)
            {
                _features[i].Update();
            }
        }

        private void OnWorldCreated()
        {
            foreach (var handle in _features) handle.WorldCreated();
        }

        private void OnWorldDestroyed()
        {
            foreach (var handle in _features) handle.WorldDestroyed();
        }
    }
}
