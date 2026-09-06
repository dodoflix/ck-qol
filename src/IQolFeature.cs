using System.Collections.Generic;
using CkQol.Config;

namespace CkQol
{
    /// One self-contained quality-of-life tweak. Add a class extending
    /// QolFeatureBase and register it in CkQolMod.BuildFeatures; the on/off toggle,
    /// config persistence, menu tab and crash isolation are wired up for you.
    public interface IQolFeature
    {
        /// Config section name, menu tab label, log prefix. Keep it short and stable -
        /// renaming it orphans the user's existing settings.
        string Name { get; }

        /// Shown at the top of the feature's tab.
        string Description { get; }

        /// Whether this starts enabled on a fresh install.
        bool EnabledByDefault { get; }

        /// Settings to expose in the menu. Return an empty list for none.
        /// Called once at construction, before Bind.
        IEnumerable<ModSetting> GetSettings();

        /// Called when the feature starts - at load if enabled, or the moment the
        /// user switches it on in the menu.
        void Init();

        /// Client world appeared. Also fired on enable if a world is already loaded.
        void OnWorldCreated();

        /// Client world went away.
        void OnWorldDestroyed();

        /// Per-frame while enabled. Keep it cheap.
        void Update();

        /// Called on disable or mod unload. Undo anything global you touched.
        void Shutdown();
    }
}
