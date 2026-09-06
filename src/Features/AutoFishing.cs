using System.Collections.Generic;
using CkQol.Config;

namespace CkQol.Features
{
    /// Hands-free fishing: reels for you on every bite, and keeps baited spots from
    /// running dry.
    ///
    /// The work happens in two ECS systems (see AutoFishingSystems.cs). This class
    /// only owns the settings and mirrors them into AutoFishingState, which is what
    /// the systems read - they never touch the feature, so nothing in simulation code
    /// has to care whether the feature is loaded, enabled or being torn down.
    public class AutoFishing : QolFeatureBase
    {
        public override string Name => "Auto Fishing";

        public override string Description =>
            "Reels in every bite for you and stops baited fishing spots from running " +
            "out. Cast once and leave it. Pauses while a menu or inventory is open, " +
            "and never touches octopus boss fishing.";

        private readonly BoolSetting _autoReel =
            new BoolSetting("AutoReel", "Auto reel", true,
                            "Reel in automatically the moment a fish bites. Turn off " +
                            "if another fishing mod is doing the same job.");

        private readonly BoolSetting _learnHold =
            new BoolSetting("LearnReelHold", "Learn reel hold", false,
                            "Hold the reel for as long as the previous reel took. Any " +
                            "reel you do by hand sets it. Not saved - Reel hold below " +
                            "is used again after a restart.");

        private readonly FloatSetting _reelHold =
            new FloatSetting("ReelHoldSeconds", "Reel hold", 0.2f, 0.1f, 1f,
                             "How long the reel button is held for each catch. Raise " +
                             "it if bites are being missed on a high-latency server. " +
                             "Ignored while Learn reel hold is on and you have reeled " +
                             "by hand at least once.");

        private readonly FloatSetting _pullDelay =
            new FloatSetting("PullDelaySeconds", "Pull delay", 0f, 0f, 2f,
                             "Wait this long after a fish bites before reeling. Zero " +
                             "reels the instant it bites. Raise it to look less like " +
                             "a machine, at the risk of losing a fish.");

        private readonly BoolSetting _infiniteShoal =
            new BoolSetting("InfiniteShoal", "Infinite fish shoal", true,
                            "Baited spots never deplete. The host decides this one, so " +
                            "on a server it has no effect unless the host has the mod.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _autoReel;
            yield return _learnHold;
            yield return _reelHold;
            yield return _pullDelay;
            yield return _infiniteShoal;
        }


        public override void Init()
        {
            foreach (var setting in GetSettings())
            {
                setting.Changed += _ => Push();
            }
            Push();
            Log($"started (reel={_autoReel.Value}, hold={_reelHold.Value:0.00}s, " +
                $"learn={_learnHold.Value}, delay={_pullDelay.Value:0.00}s, " +
                $"shoal={_infiniteShoal.Value})");
        }

        public override void Shutdown()
        {
            AutoFishingState.ReelEnabled = false;
            AutoFishingState.ShoalEnabled = false;
            Log("stopped");
        }

        private void Push()
        {
            AutoFishingState.ReelEnabled = _autoReel.Value;
            AutoFishingState.ShoalEnabled = _infiniteShoal.Value;
            AutoFishingState.ReelHoldSeconds = _reelHold.Value;
            AutoFishingState.LearnEnabled = _learnHold.Value;
            AutoFishingState.PullDelaySeconds = _pullDelay.Value;
        }
    }

    /// What the fishing systems read.
    ///
    /// Separate from the feature so the systems have no reference to it: they live in
    /// two different ECS worlds and run whether or not the feature is enabled, and a
    /// static bool test costs nothing when it is off.
    internal static class AutoFishingState
    {
        internal static volatile bool ReelEnabled;
        internal static volatile bool ShoalEnabled;
        internal static volatile float ReelHoldSeconds = 0.2f;
        internal static volatile bool LearnEnabled;
        internal static volatile float PullDelaySeconds;

        /// How long the previous reel lasted, or -1 before there has been one.
        ///
        /// Not persisted and never written back into the Reel hold setting: that row
        /// stays whatever the player chose, and this shadows it only while Learn reel
        /// hold is on.
        private static volatile float _lastHold = -1f;

        private static volatile bool _shoalCheckPending;

        /// Reported by the reeler when a reel it did not perform itself ends.
        /// Clamped so a stuck button or a paused frame cannot produce a hold that
        /// jams fishing.
        internal static void ReportHold(float seconds) =>
            _lastHold = UnityEngine.Mathf.Clamp(seconds, 0.05f, 1.5f);

        /// What the reeler holds for: the previous reel's length once there has been
        /// one, otherwise the configured value.
        internal static float EffectiveReelHold
        {
            get
            {
                float last = _lastHold;
                return LearnEnabled && last > 0f ? last : ReelHoldSeconds;
            }
        }

        /// Raised by the reeler the moment it hooks a fish.
        ///
        /// A shoal's catch counter can only move when a fish is caught, so this is the
        /// only moment worth reading shoal entities - and reading them is a main-thread
        /// sync point that blocks on the jobs writing that data, which is expensive
        /// whether or not anything changed. Signalling instead of polling means that
        /// away from the water the feature reads nothing at all.
        ///
        /// A plain static rather than a component because the two systems are in
        /// different worlds; a networked component would have to be read every tick to
        /// be noticed, which is the cost being avoided. Both worlds share a process in
        /// single-player and on a listen server - the only cases where a local frame
        /// rate is at stake. A dedicated server has no local client to raise it, which
        /// is what the backstop sweep covers.
        internal static void RaiseShoalCheck() => _shoalCheckPending = true;

        /// True once per raised signal.
        internal static bool ConsumeShoalCheck()
        {
            if (!_shoalCheckPending) return false;
            _shoalCheckPending = false;
            return true;
        }
    }
}
