namespace CkQol
{
    /// One self-contained quality-of-life tweak. Add a class implementing this and
    /// register it in CkQolMod.BuildFeatures; everything else is wired up for you.
    public interface IQolFeature
    {
        /// Config key and log prefix. Keep it short and stable - renaming it orphans
        /// the user's existing on/off setting.
        string Name { get; }

        /// Shown next to the toggle in the config file.
        string Description { get; }

        /// Whether this feature starts enabled on a fresh install.
        bool EnabledByDefault { get; }

        /// Called once at mod load, only if the feature is enabled.
        void Init();

        /// Called when the client world appears. May be called again if the player
        /// returns to the menu and loads another world.
        void OnWorldCreated();

        /// Called when the client world goes away.
        void OnWorldDestroyed();

        /// Per-frame. Keep it cheap - this runs every frame the game does.
        void Update();

        /// Called at mod unload. Undo anything global you touched.
        void Shutdown();
    }
}
