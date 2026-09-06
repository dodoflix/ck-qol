using System.Collections.Generic;
using CkQol.Config;

namespace CkQol.Features
{
    /// Hands-free fishing. The work is in AutoFishingSystems.cs; this owns the
    /// settings and mirrors them into AutoFishingState, which the systems read.
    public class AutoFishing : QolFeatureBase
    {
        public override string Name => "Auto Fishing";

        public override string Description =>
            "Casts and hooks every fish for you, and stops baited fishing spots from " +
            "running out. Cast once and leave it. Pauses while a menu or inventory is " +
            "open, and never touches octopus boss fishing.";

        private readonly BoolSetting _autoReel =
            new BoolSetting("AutoReel", "Auto reel", true,
                            "Turn off if another fishing mod does the same job.");

        private readonly FloatSetting _castingTime =
            new FloatSetting("CastingTimeSeconds", "Casting time", 0f, 0f, 2f,
                             "How long the throw is charged, which is how far the " +
                             "line lands. Past the game's cast timer it throws at " +
                             "maximum range.");

        private readonly BoolSetting _learnCasting =
            new BoolSetting("LearnCasting", "Learn casting", true,
                            "Copy your own charge instead of Casting time above. " +
                            "Not saved.");

        private readonly FloatSetting _reelHold =
            new FloatSetting("ReelHoldSeconds", "Reel hold", 0.2f, 0.2f, 2f,
                             "How long the hook press is held. Too short and bites are " +
                             "missed; too long and the press outlives the catch and " +
                             "pulls the next cast up empty.");

        private readonly BoolSetting _infiniteShoal =
            new BoolSetting("InfiniteShoal", "Infinite fish shoal", true,
                            "Baited spots never deplete. Host-side: no effect on a " +
                            "server without the mod.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _autoReel;
            yield return _castingTime;
            yield return _learnCasting;
            yield return _reelHold;
            yield return _infiniteShoal;
        }

        /// Settings stay editable while the feature is off; without this the change
        /// handler would restart the systems.
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

    /// What the fishing systems read. Separate from the feature: the systems live in
    /// two ECS worlds and run whether or not it is enabled.
    internal static class AutoFishingState
    {
        internal static volatile bool ReelEnabled;
        internal static volatile bool ShoalEnabled;
        internal static volatile bool LearnEnabled;
        internal static volatile float CastingTimeSeconds;

        /// 0.2 is the value the reference mod shipped. A single frame is not enough:
        /// this runs in the prediction loop, so one frame of input does not reliably
        /// reach the server. Too long is the other failure - a press still held once
        /// the line is back out pulls it up empty (Fishing.cs:272).
        internal static volatile float ReelHoldSeconds = 0.2f;

        /// The player's last own charge, or -1. Not persisted.
        private static volatile float _lastCastHold = -1f;

        private static volatile bool _shoalCheckPending;

        internal static void ReportHold(float seconds) =>
            _lastCastHold = UnityEngine.Mathf.Clamp(seconds, 0f, 2f);

        internal static float EffectiveCastingTime
        {
            get
            {
                float last = _lastCastHold;
                return LearnEnabled && last >= 0f ? last : CastingTimeSeconds;
            }
        }

        /// Raised on a hook, the only moment a shoal's counter can move - so the
        /// shoal system reads nothing away from the water.
        ///
        /// A static, not a component: the systems are in different worlds, and a
        /// networked component would have to be read every tick to be noticed, which
        /// is the cost being avoided. Both worlds share a process except on a
        /// dedicated server, which the backstop covers.
        internal static void RaiseShoalCheck() => _shoalCheckPending = true;

        internal static bool ConsumeShoalCheck()
        {
            if (!_shoalCheckPending) return false;
            _shoalCheckPending = false;
            return true;
        }
    }
}
