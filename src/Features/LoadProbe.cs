namespace CkQol.Features
{
    /// Placeholder that proves the mod compiled, loaded, and is receiving lifecycle
    /// callbacks. Delete it once there are real features.
    public class LoadProbe : QolFeatureBase
    {
        public override string Name => "LoadProbe";

        public override string Description =>
            "Logs mod lifecycle events. Diagnostic only, safe to turn off.";

        public override void Init()
        {
            Log("initialised");
        }

        public override void OnWorldCreated()
        {
            Log("client world created");
        }

        public override void OnWorldDestroyed()
        {
            Log("client world destroyed");
        }
    }
}
