using System.Collections.Generic;
using CkQol.Config;
using CkQol.Native;
using UnityEngine;

namespace CkQol.Features
{
    /// Shows how much damage the player is dealing. The collecting is in
    /// DpsTrackerSystem.cs; this owns the settings and the HUD panel.
    public class DpsTracker : QolFeatureBase
    {
        public override string Name => "DPS Tracker";

        public override string Description =>
            "Shows your damage per second under the minion counter, while you are " +
            "dealing any.";

        private const string Basic = "Basic";
        private const string Detailed = "Detailed";

        private readonly ChoiceSetting _mode =
            new ChoiceSetting("Mode", "Mode", new[] { Basic, Detailed }, Basic,
                              "Basic shows one total. Detailed shows a row per source " +
                              "with its icon, and the total under them.");

        private readonly IntSetting _window =
            new IntSetting("Window", "Window", 5, 3, 10,
                           "Seconds the number averages over. Shorter reacts faster and " +
                           "swings more between hits.");

        private readonly BoolSetting _damageOverTime =
            new BoolSetting("DamageOverTime", "Damage over time", true,
                            "Count burning and acid ticks, in a row of their own. The " +
                            "game does not record who applied a condition, so in " +
                            "multiplayer this also counts other players'.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _mode;
            yield return _window;
            yield return _damageOverTime;
        }

        public override void Init()
        {
            base.Init();
            Log($"started (mode={_mode.Value}, window={_window.Value}s, " +
                $"dot={_damageOverTime.Value})");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Log("stopped");
        }

        public override void OnWorldDestroyed() => DpsMeter.Clear();

        protected override void Apply()
        {
            DpsState.Enabled = Running;
            DpsState.Detailed = _mode.Value == Detailed;
            DpsState.WindowSeconds = _window.Value;
            DpsState.CountDamageOverTime = _damageOverTime.Value;

            if (!Running) DpsMeter.Clear();
        }

        private CkQolStatPanel _panel;

        /// The HUD only exists in game, and not on the first frame of it, so the panel
        /// is added on the first update that finds it.
        public override void Update()
        {
            if (_panel != null || Manager.main == null || Manager.main.player == null) return;
            _panel = GameStatPanel.Install(BuildRows);
        }

        /// Beyond this the panel would reach the widgets below it, and a breakdown that
        /// long is not readable at a glance anyway.
        private const int MaxSourceRows = 6;

        private readonly List<StatRow> _rows = new List<StatRow>();
        private readonly Dictionary<int, int> _bySource = new Dictionary<int, int>();
        private readonly List<int> _order = new List<int>();

        /// Called every frame by the panel.
        private List<StatRow> BuildRows()
        {
            _rows.Clear();
            if (!DpsState.Enabled) return _rows;

            bool detailed = DpsState.Detailed;
            int total = DpsMeter.Total(detailed ? _bySource : null);
            if (total <= 0) return _rows;

            int window = DpsState.WindowSeconds;

            if (detailed)
            {
                _order.Clear();
                foreach (var entry in _bySource) _order.Add(entry.Key);
                _order.Sort((a, b) => _bySource[b].CompareTo(_bySource[a]));

                int rows = _order.Count < MaxSourceRows ? _order.Count : MaxSourceRows;
                for (int i = 0; i < rows; i++)
                {
                    _rows.Add(new StatRow
                    {
                        Icon = IconFor(_order[i]),
                        Text = Rate(_bySource[_order[i]], window)
                    });
                }
            }

            _rows.Add(new StatRow { Text = Rate(total, window) + " DPS" });
            return _rows;
        }

        private static string Rate(int damage, int seconds) =>
            Mathf.RoundToInt(damage / (float)seconds).ToString();

        private readonly Dictionary<int, Sprite> _icons = new Dictionary<int, Sprite>();

        /// Cached: the sprite tables do not change, and this runs every frame.
        private Sprite IconFor(int source)
        {
            if (_icons.TryGetValue(source, out var cached)) return cached;

            Sprite sprite;
            if (source == DpsMeter.BurningSource) sprite = ConditionIcon(ConditionID.Burning);
            else if (source == DpsMeter.OtherDotSource) sprite = ConditionIcon(ConditionID.AcidDamage);
            else
            {
                var info = PugDatabase.GetObjectInfo((ObjectID)source);
                sprite = info == null ? null : (info.smallIcon != null ? info.smallIcon : info.icon);
            }

            _icons[source] = sprite;
            return sprite;
        }

        /// Conditions have their own sprite table and are not in PugDatabase
        /// (ConditionsTable.cs:55).
        private static Sprite ConditionIcon(ConditionID id)
        {
            var table = Manager.ui != null ? Manager.ui.conditionsIconsTable : null;
            return table != null ? table.GetConditionInfo(id).icon : null;
        }
    }

    /// What the tracking system reads. Separate from the feature so the system holds no
    /// reference to it and costs one bool test while off.
    internal static class DpsState
    {
        internal static volatile bool Enabled;
        internal static volatile bool Detailed;
        internal static volatile int WindowSeconds = 5;
        internal static volatile bool CountDamageOverTime = true;
    }

    /// The damage dealt inside the window.
    ///
    /// Kept as raw hits rather than time buckets: a window holds a few hundred at most,
    /// and totalling those costs less than carrying a per-source breakdown in every
    /// bucket. Written from the tracking system and read from the panel, both on the
    /// main thread.
    internal static class DpsMeter
    {
        /// Source keys are ObjectID values. The damage-over-time buckets have no item
        /// behind them and take negatives, which no ObjectID uses.
        internal const int BurningSource = -1;
        internal const int OtherDotSource = -2;

        private struct Hit
        {
            public double At;
            public int Source;
            public int Amount;
        }

        private static readonly List<Hit> _hits = new List<Hit>();

        internal static void Record(int source, int amount, double at) =>
            _hits.Add(new Hit { At = at, Source = source, Amount = amount });

        internal static void Clear() => _hits.Clear();

        /// Drops what has aged out. Hits go on the end in time order, so this is one
        /// walk from the front.
        internal static void Prune(double now)
        {
            double cutoff = now - DpsState.WindowSeconds;

            int drop = 0;
            while (drop < _hits.Count && _hits[drop].At < cutoff) drop++;
            if (drop > 0) _hits.RemoveRange(0, drop);
        }

        /// Damage in the window, and the same split by source if a map is given.
        internal static int Total(Dictionary<int, int> bySource)
        {
            bySource?.Clear();

            int total = 0;
            for (int i = 0; i < _hits.Count; i++)
            {
                total += _hits[i].Amount;
                if (bySource == null) continue;

                bySource.TryGetValue(_hits[i].Source, out int sum);
                bySource[_hits[i].Source] = sum + _hits[i].Amount;
            }
            return total;
        }
    }
}
