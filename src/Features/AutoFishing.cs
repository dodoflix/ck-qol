using System.Collections.Generic;
using CkQol.Config;

namespace CkQol.Features
{
    /// Hands-free fishing: casts and hooks for you, and keeps baited spots from
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
            "Casts and hooks every fish for you, and stops baited fishing spots from " +
            "running out. Cast once and leave it. Pauses while a menu or inventory is " +
            "open, and never touches octopus boss fishing.";

        private readonly BoolSetting _autoReel =
            new BoolSetting("AutoReel", "Auto reel", true,
                            "Fish automatically. Turn off if another fishing mod is " +
                            "doing the same job.");

        private readonly FloatSetting _castingTime =
            new FloatSetting("CastingTimeSeconds", "Casting time", 2f, 0f, 2f,
                             "How long the throw is charged for. The game sets cast " +
                             "distance from this, so it is how far the line lands. " +
                             "Anything past the game's own cast timer throws at " +
                             "maximum range.");

        private readonly BoolSetting _learnCasting =
            new BoolSetting("LearnCasting", "Learn casting", false,
                            "Use your own casting instead of the setting above: charge " +
                            "a throw by hand and every automatic one copies it. Not " +
                            "saved - Casting time is used again after a restart.");

        private readonly FloatSetting _reelHold =
            new FloatSetting("ReelHoldSeconds", "Reel hold", 0f, 0f, 2f,
                             "How long the button is held to hook a fish. Zero is a " +
                             "single press, which is all the game needs. Raise it only " +
                             "if bites are being missed on a high-latency server.");

        private readonly BoolSetting _infiniteShoal =
            new BoolSetting("InfiniteShoal", "Infinite fish shoal", true,
                            "Baited spots never deplete. The host decides this one, so " +
                            "on a server it has no effect unless the host has the mod.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _autoReel;
            yield return _castingTime;
            yield return _learnCasting;
            yield return _reelHold;
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
            Log($"started (reel={_autoReel.Value}, cast={_castingTime.Value:0.00}s, " +
                $"learn={_learnCasting.Value}, hold={_reelHold.Value:0.00}s, " +
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
            AutoFishingState.CastingTimeSeconds = _castingTime.Value;
            AutoFishingState.LearnEnabled = _learnCasting.Value;
            AutoFishingState.ReelHoldSeconds = _reelHold.Value;
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
        internal static volatile float CastingTimeSeconds = 2f;

        /// How long the hook press is held. Zero is one frame, which is all the game
        /// needs: hooking takes a press, not a duration. Longer is a risk rather than
        /// a safety margin - a press still held once the line is back out with no bite
        /// pulls it straight up empty (Fishing.cs:272).
        internal static volatile float ReelHoldSeconds;

        /// How long the player charged their last throw of their own, or -1 before
        /// there has been one. Not persisted; Casting time is used again after a
        /// restart.
        private static volatile float _lastCastHold = -1f;

        private static volatile bool _shoalCheckPending;

        /// Reported when a button hold the mod did not perform itself ends.
        internal static void ReportHold(float seconds) =>
            _lastCastHold = UnityEngine.Mathf.Clamp(seconds, 0f, 2f);

        /// How long to charge a throw for: the player's own last throw while Learn
        /// casting is on, otherwise the configured value.
        internal static float EffectiveCastingTime
        {
            get
            {
                float last = _lastCastHold;
                return LearnEnabled && last >= 0f ? last : CastingTimeSeconds;
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
