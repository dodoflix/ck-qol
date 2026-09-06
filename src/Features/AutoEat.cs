using System.Collections.Generic;
using CkQol.Config;

namespace CkQol.Features
{
    /// Eats automatically when hunger drops past a threshold. The work is in
    /// AutoEatSystem.cs; this owns the settings and mirrors them into AutoEatState.
    public class AutoEat : QolFeatureBase
    {
        public override string Name => "Auto Eat";

        public override string Description =>
            "Eats for you when you get hungry, without changing the item you have " +
            "selected. Pauses while a menu or inventory is open, and stays out of the " +
            "way while fishing.";

        /// Below 25 the game applies the starving penalties; at 75 and above it grants
        /// the well fed buffs (HungerAndRunningSystem.cs:178-231).
        private const string WellFed = "Well fed";
        private const string Starving = "Starving";

        private readonly ChoiceSetting _threshold =
            new ChoiceSetting("Threshold", "Threshold", new[] { WellFed, Starving }, WellFed,
                              "When to eat. Well fed keeps the buff at 75; Starving " +
                              "only eats at 25, where the penalties start.");

        private readonly BoolSetting _eatCooked =
            new BoolSetting("EatCooked", "Eat cooked food", false,
                            "Off leaves cooked dishes alone and eats only raw food.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _threshold;
            yield return _eatCooked;
        }

        /// Settings stay editable while the feature is off; without this the change
        /// handler would restart the system.
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
            Log($"started (threshold={AutoEatState.Threshold}, cooked={_eatCooked.Value})");
        }

        public override void Shutdown()
        {
            _running = false;
            Push();
            Log("stopped");
        }

        private void Push()
        {
            AutoEatState.Enabled = _running;
            AutoEatState.Threshold = _threshold.Value == Starving ? 25 : 75;
            AutoEatState.AllowCooked = _eatCooked.Value;
        }
    }

    /// What the eating system reads. Separate from the feature so the system holds no
    /// reference to it and costs one bool test while off.
    internal static class AutoEatState
    {
        internal static volatile bool Enabled;
        internal static volatile int Threshold = 75;
        internal static volatile bool AllowCooked;
    }
}
