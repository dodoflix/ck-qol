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

        private readonly FloatSetting _throwDelay =
            new FloatSetting("ThrowDelaySeconds", "Throw delay", 2f, 0f, 2f,
                             "How long the button is held before the rod is thrown. " +
                             "The game sets cast distance from how far the throw was " +
                             "charged, so this is how far the line lands. Held past " +
                             "the game's own cast timer it simply throws at maximum.");

        private readonly BoolSetting _learnPull =
            new BoolSetting("LearnPull", "Learn pull", false,
                            "Use your own throw instead of the setting above: hold the " +
                            "button as long as you like on a cast by hand and every " +
                            "automatic throw copies it. Not saved.");

        private readonly FloatSetting _pullDelay =
            new FloatSetting("PullDelaySeconds", "Pull delay", 0f, 0f, 2f,
                             "Wait this long after a fish bites before hooking it. " +
                             "Zero hooks the instant it bites. A fish stays on the " +
                             "line for three to four seconds, so a long wait loses it.");

        private readonly BoolSetting _infiniteShoal =
            new BoolSetting("InfiniteShoal", "Infinite fish shoal", true,
                            "Baited spots never deplete. The host decides this one, so " +
                            "on a server it has no effect unless the host has the mod.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _autoReel;
            yield return _throwDelay;
            yield return _learnPull;
            yield return _pullDelay;
            yield return _infiniteShoal;
        }


        /// Whether the feature is switched on. Settings can still be edited while it
        /// is off, and without this the change handler would push them straight into
        /// the systems and quietly restart them.
        private bool _running;

        private bool _subscribed;

        public override void Init()
        {
            if (!_subscribed)
            {
                _subscribed = true;
                foreach (var setting in GetSettings())
                {
                    setting.Changed += _ => Push();
                }
            }

            _running = true;
            Push();
            Log($"started (reel={_autoReel.Value}, throw={_throwDelay.Value:0.00}s, " +
                $"learn={_learnPull.Value}, pull={_pullDelay.Value:0.00}s, " +
                $"shoal={_infiniteShoal.Value})");
        }

        public override void Shutdown()
        {
            _running = false;
            Push();
            Log("stopped");
        }

        private void Push()
        {
            AutoFishingState.ReelEnabled = _running && _autoReel.Value;
            AutoFishingState.ShoalEnabled = _running && _infiniteShoal.Value;
            AutoFishingState.ThrowDelaySeconds = _throwDelay.Value;
            AutoFishingState.LearnEnabled = _learnPull.Value;
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
        internal static volatile bool LearnEnabled;
        internal static volatile float PullDelaySeconds;
        internal static volatile float ThrowDelaySeconds = 2f;

        /// How long the hook press is held.
        ///
        /// Not a setting: hooking needs a press, not a duration, and this only exists
        /// so one frame of it cannot be lost on the way to a server. Longer is worse,
        /// not better - a press still held once the line is back out with no bite
        /// pulls it straight up empty (Fishing.cs:272).
        internal const float HookHoldSeconds = 0.2f;

        /// How long the player held the button on their last throw of their own, or
        /// -1 before there has been one. Not persisted; the Throw delay setting is
        /// used again after a restart.
        private static volatile float _lastThrowHold = -1f;

        private static volatile bool _shoalCheckPending;

        /// Reported when a button hold the mod did not perform itself ends.
        internal static void ReportHold(float seconds) =>
            _lastThrowHold = UnityEngine.Mathf.Clamp(seconds, 0f, 2f);

        /// How long to charge a throw for: the player's own last throw while Learn
        /// pull is on, otherwise the configured value.
        internal static float EffectiveThrowDelay
        {
            get
            {
                float last = _lastThrowHold;
                return LearnEnabled && last >= 0f ? last : ThrowDelaySeconds;
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
