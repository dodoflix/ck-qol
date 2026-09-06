using System.Collections.Generic;
using CkQol.Config;
using CkQol.Native;
using Unity.Mathematics;
using UnityEngine;

namespace CkQol.Features
{
    /// Finds an item in the containers around you. The index and the scan are in
    /// ChestSearchIndex.cs and the panel is in Native/GameSearchPanel.cs; this owns
    /// the settings and wires the two together.
    public class ChestSearch : QolFeatureBase
    {
        public override string Name => "Chest Search";

        public override string Description =>
            "Search the chests around you from the inventory screen, instead of " +
            "opening them one at a time.";

        private readonly IntSetting _radius =
            new IntSetting("Radius", "Radius", 10, 4, 14,
                           "Tiles to search. The game only keeps containers near you " +
                           "loaded, so past about 14 there is nothing left to find.");

        private readonly IntSetting _minimum =
            new IntSetting("MinimumCharacters", "Minimum characters", 3, 2, 5,
                           "How much to type before the name suggestions appear.");

        private readonly BoolSetting _self =
            new BoolSetting("SearchSelf", "Search my inventory", true,
                            "Also report what you are already carrying.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _radius;
            yield return _minimum;
            yield return _self;
        }

        public override void Init()
        {
            base.Init();
            Log($"started (radius={_radius.Value}, minChars={_minimum.Value}, " +
                $"self={_self.Value})");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Log("stopped");
        }

        /// The item names and the container query both belong to a world.
        public override void OnWorldDestroyed()
        {
            ChestSearchIndex.Forget();
            Clear();
        }

        protected override void Apply()
        {
            ChestSearchState.Enabled = Running;
            ChestSearchState.Radius = _radius.Value;
            ChestSearchState.MinimumCharacters = _minimum.Value;
            ChestSearchState.SearchSelf = _self.Value;

            if (!Running) Clear();
        }

        private CkQolSearchPanel _panel;

        private readonly List<ItemEntry> _matches = new List<ItemEntry>();
        private readonly List<string> _names = new List<string>();
        private readonly List<ContainerHit> _hits = new List<ContainerHit>();
        private readonly List<SearchRow> _rows = new List<SearchRow>();

        private ObjectID _wanted = ObjectID.None;
        private string _wantedName;
        private double _nextScan;

        /// Beyond this the panel would run past the inventory it sits beside.
        private const int MaxRows = 8;
        private const int MaxSuggestions = 8;

        /// Containers do not move, but their contents change, so the answer is
        /// refreshed while the panel is open rather than frozen at the pick.
        private const double RescanSeconds = 0.5;

        /// The HUD only exists in game, and not on the first frame of it, so the
        /// panel is added on the first update that finds it.
        public override void Update()
        {
            if (_panel == null)
            {
                if (Manager.main == null || Manager.main.player == null) return;

                _panel = GameSearchPanel.Install();
                if (_panel == null) return;

                _panel.Visible = PanelVisible;
                _panel.Suggest = Suggest;
                _panel.Picked = Pick;
                _panel.Cleared = Clear;
            }

            if (_wanted == ObjectID.None || !PanelVisible()) return;

            double now = Time.timeAsDouble;
            if (now < _nextScan) return;
            _nextScan = now + RescanSeconds;

            Rescan();
        }

        private bool PanelVisible() =>
            ChestSearchState.Enabled &&
            Manager.ui != null && Manager.ui.isPlayerInventoryShowing;

        /// Names to offer for what has been typed so far, or nothing while it is
        /// too short to be worth a list.
        private List<string> Suggest(string query)
        {
            _names.Clear();
            _matches.Clear();

            if (query == null || query.Length < ChestSearchState.MinimumCharacters)
            {
                return _names;
            }

            ChestSearchIndex.Match(query, _matches, MaxSuggestions);
            for (int i = 0; i < _matches.Count; i++) _names.Add(_matches[i].Name);
            return _names;
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _matches.Count) return;

            _wanted = _matches[index].Id;
            _wantedName = _matches[index].Name;
            _nextScan = 0;
            Rescan();
        }

        private void Clear()
        {
            _wanted = ObjectID.None;
            _wantedName = null;
            _rows.Clear();
            _hits.Clear();
            if (_panel != null) _panel.SetRows(_wantedName, _rows);
        }

        private void Rescan()
        {
            ChestSearchIndex.Find(_wanted, ChestSearchState.Radius,
                                  ChestSearchState.SearchSelf, _hits);

            var player = Manager.main != null ? Manager.main.player : null;
            Vector3 here = player != null ? player.RenderPosition : Vector3.zero;

            _rows.Clear();
            for (int i = 0; i < _hits.Count && i < MaxRows; i++)
            {
                var hit = _hits[i];
                float dx = hit.Position.x - here.x;
                float dz = hit.Position.z - here.z;
                float away = Mathf.Sqrt(dx * dx + dz * dz);

                _rows.Add(new SearchRow
                {
                    Icon = IconFor(hit.ContainerId),
                    Text = hit.Label ?? (hit.ContainerId == ObjectID.None
                        ? "carried"
                        : $"{Compass(dx, dz)} {away:0.#}m"),
                    Amount = "x" + hit.Count,
                });
            }

            if (_panel != null) _panel.SetRows(_wantedName, _rows);
        }

        /// Letters rather than arrow glyphs: the game's font is a sprite sheet and
        /// carries no arrows to draw.
        private static string Compass(float dx, float dz)
        {
            if (math.abs(dx) < 0.5f && math.abs(dz) < 0.5f) return "here";

            float angle = Mathf.Atan2(dz, dx) * Mathf.Rad2Deg;
            int step = (int)Mathf.Round((angle + 360f) / 45f) % 8;

            switch (step)
            {
                case 0: return "E";
                case 1: return "NE";
                case 2: return "N";
                case 3: return "NW";
                case 4: return "W";
                case 5: return "SW";
                case 6: return "S";
                default: return "SE";
            }
        }

        private readonly Dictionary<ObjectID, Sprite> _icons = new Dictionary<ObjectID, Sprite>();

        private Sprite IconFor(ObjectID id)
        {
            if (_icons.TryGetValue(id, out var cached)) return cached;

            var info = id == ObjectID.None ? null : PugDatabase.GetObjectInfo(id);
            Sprite sprite = info != null ? info.smallIcon : null;

            _icons[id] = sprite;
            return sprite;
        }
    }

    /// What the search reads. Separate from the feature so nothing else holds a
    /// reference to it.
    internal static class ChestSearchState
    {
        internal static volatile bool Enabled;
        internal static volatile int Radius = 10;
        internal static volatile int MinimumCharacters = 3;
        internal static volatile bool SearchSelf = true;
    }
}
