using System.Collections.Generic;
using CkQol.Config;

namespace CkQol.Features
{
    /// Keeps the player's own mix of minions topped up. The work is in
    /// AutoSummonSystem.cs; this owns the settings and mirrors them into
    /// AutoSummonState.
    public class AutoSummon : QolFeatureBase
    {
        public override string Name => "Auto Summon";

        public override string Description =>
            "Resummons your minions as they expire or die, keeping the mix you summoned " +
            "yourself. Summon by hand once to teach it what you want. Forgotten when " +
            "the feature is switched off, and on quit.";

        private readonly BoolSetting _topUp =
            new BoolSetting("TopUp", "Keep mix topped up", true,
                            "Off waits until every minion is gone before summoning.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _topUp;
        }

        public override void Init()
        {
            base.Init();
            Log($"started (topUp={_topUp.Value})");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Log("stopped");
        }

        protected override void Apply()
        {
            AutoSummonState.Enabled = Running;
            AutoSummonState.TopUp = _topUp.Value;

            // Switching the feature off is how a player re-teaches it a loadout without
            // restarting; there is no separate row for that.
            if (!Running) AutoSummonState.Wanted.Clear();
        }
    }

    /// What the summoning system reads. Separate from the feature so the system holds
    /// no reference to it and costs one bool test while off.
    internal static class AutoSummonState
    {
        internal static volatile bool Enabled;
        internal static volatile bool TopUp = true;

        /// The minions to keep alive, in the order they were learned, trimmed from the
        /// front to the cap. Session only - never persisted.
        ///
        /// Only touched by the summoning system on the main thread, plus a Clear from
        /// the feature when it stops.
        internal static readonly List<ObjectID> Wanted = new List<ObjectID>();
    }
}
