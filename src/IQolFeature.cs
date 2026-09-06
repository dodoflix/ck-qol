using System.Collections.Generic;
using CkQol.Config;

namespace CkQol
{
    /// One self-contained tweak. Extend QolFeatureBase and register it in
    /// CkQolMod.BuildFeatures; the toggle, persistence, menu page and crash isolation
    /// are wired up.
    public interface IQolFeature
    {
        /// Config section, menu label and log prefix. Renaming orphans saved
        /// settings.
        string Name { get; }

        /// Shown on hover on the root page.
        string Description { get; }

        /// Whether this starts enabled on a fresh install.
        bool EnabledByDefault { get; }

        /// Called once at construction, before Bind.
        IEnumerable<ModSetting> GetSettings();

        /// At load if enabled, or when switched on.
        void Init();

        /// Also fired on enable if a world is already loaded.
        void OnWorldCreated();

        /// Client world went away.
        void OnWorldDestroyed();

        /// Per-frame while enabled. Keep it cheap.
        void Update();

        /// On disable or unload. Undo anything global.
        void Shutdown();
    }
}
