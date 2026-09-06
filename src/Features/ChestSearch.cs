using System.Collections.Generic;
using CkQol.Config;
using CkQol.Native;
using Outlines.Components;
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

        private readonly BoolSetting _self =
            new BoolSetting("SearchSelf", "Search my inventory", true,
                            "Also report what you are already carrying.");

        private readonly BoolSetting _point =
            new BoolSetting("PointAtChests", "Point at chests", true,
                            "Outline each container that has it and float the count " +
                            "over it, so you can see which one is which.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _radius;
            yield return _self;
            yield return _point;
        }

        public override void Init()
        {
            base.Init();
            Log($"started (radius={_radius.Value}, self={_self.Value}, " +
                $"point={_point.Value})");
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
            ChestSearchState.SearchSelf = _self.Value;
            ChestSearchState.Point = _point.Value;
            if (!Running) Clear();
        }

        private CkQolSearchPanel _panel;

        private readonly HashSet<ObjectID> _stock = new HashSet<ObjectID>();
        private readonly List<ItemEntry> _matches = new List<ItemEntry>();
        private readonly List<SearchRow> _names = new List<SearchRow>();
        private readonly List<ContainerHit> _hits = new List<ContainerHit>();
        private readonly List<SearchRow> _rows = new List<SearchRow>();

        private ObjectID _wanted = ObjectID.None;
        private double _nextScan;

        /// Beyond this the panel would run past the inventory it sits beside.
        private const int MaxRows = 8;
        private const int MaxSuggestions = 8;

        /// One letter is enough: a suggestion pass stops at MaxSuggestions, and what
        /// is nearby is offered before what is not, so the short list is still useful.
        private const int MinimumCharacters = 1;

        /// Containers do not move, but their contents change, so the answer is
        /// refreshed while the panel is open rather than frozen at the pick.
        private const double RescanSeconds = 0.5;

        /// Stands in for the player's own pockets, which have no container icon.
        private const ObjectID CarriedIcon = ObjectID.ExplorerBackpack;

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

            // Not gated on the panel being up: a search stays live once picked, so
            // the containers keep being pointed at while the player walks to them
            // with the inventory closed. Clearing the search stops it.
            if (_wanted == ObjectID.None || !ChestSearchState.Enabled) return;

            // Every frame, not only on a rescan: the game drives the outline of
            // whatever is closest to the player, and putting that one back resets the
            // colour for every container sharing its prefab - which takes ours with
            // it. Re-applying wins it back the same frame.
            Light();

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
        private List<SearchRow> Suggest(string query)
        {
            _names.Clear();
            _matches.Clear();

            if (query == null || query.Length < MinimumCharacters)
            {
                return _names;
            }

            // Refreshed per keystroke rather than per suggestion: one walk of the
            // containers answers "is this nearby" for the whole list.
            ChestSearchIndex.Stock(ChestSearchState.Radius, ChestSearchState.SearchSelf,
                                   _stock);

            ChestSearchIndex.Match(query, _stock, _matches, MaxSuggestions);
            for (int i = 0; i < _matches.Count; i++)
            {
                _names.Add(new SearchRow
                {
                    Icon = IconFor(_matches[i].Id),
                    Text = _matches[i].Name,
                    Dim = !_matches[i].Nearby,
                });
            }
            return _names;
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _matches.Count) return;

            _wanted = _matches[index].Id;
            _nextScan = 0;
            Rescan();
        }

        private void Clear()
        {
            _wanted = ObjectID.None;
            _rows.Clear();
            _hits.Clear();
            Douse();
            if (_panel != null) _panel.SetRows(_rows);
        }

        private void Rescan()
        {
            ChestSearchIndex.Find(_wanted, ChestSearchState.Radius,
                                  ChestSearchState.SearchSelf, _hits);

            _rows.Clear();
            for (int i = 0; i < _hits.Count && i < MaxRows; i++)
            {
                var hit = _hits[i];
                bool carried = hit.ContainerId == ObjectID.None;

                _rows.Add(new SearchRow
                {
                    Icon = IconFor(carried ? CarriedIcon
                                 : hit.Ground ? _wanted
                                 : hit.ContainerId),
                    Text = carried ? "Inventory"
                         : hit.Ground ? "Ground"
                         : hit.Label ?? NameOf(hit.ContainerId),
                    Amount = "x" + Compact(hit.Count),
                });
            }

            if (_panel != null) _panel.SetRows(_rows);
            Point();
        }

        private double _nextPoint;

        private struct Lit
        {
            internal EntityMonoBehaviour Mono;
            internal Color Tint;
        }

        /// What is outlined right now, so it can be put back when it stops matching.
        private readonly List<Lit> _lit = new List<Lit>();

        /// How much counts as a lot. A container with four of something and one with
        /// four thousand should not look the same across a room.
        private const int Some = 10;
        private const int Many = 100;
        private const int Lots = 1000;

        private static Color TintFor(int count) =>
            count >= Lots ? new Color(0.95f, 0.35f, 0.35f) :
            count >= Many ? new Color(0.95f, 0.80f, 0.35f) :
            count >= Some ? new Color(0.45f, 0.85f, 0.40f) :
                            Color.white;

        /// The floating count takes one of five the game defines rather than a
        /// colour, so it gets the nearest to the outline.
        private static CombatText.NumberColor SayFor(int count) =>
            count >= Lots ? CombatText.NumberColor.Red :
            count >= Many ? CombatText.NumberColor.Yellow :
            count >= Some ? CombatText.NumberColor.Green :
                            CombatText.NumberColor.White;

        /// Outlines each container that has it, with the same call the game uses to
        /// ring the interactable you are standing next to, and floats the count over
        /// it. The number is slower than the rescan: it drifts and fades, so
        /// re-spawning it every scan would smear.
        private void Point()
        {
            Douse();
            if (!ChestSearchState.Point || _hits.Count == 0) return;

            bool say = Time.timeAsDouble >= _nextPoint;
            if (say) _nextPoint = Time.timeAsDouble + 1.6;

            for (int i = 0; i < _hits.Count && i < MaxRows; i++)
            {
                if (_hits[i].Entity == Unity.Entities.Entity.Null) continue;

                var mono = Manager.memory != null
                    ? Manager.memory.GetEntityMono(_hits[i].Entity)
                    : null;
                if (mono == null) continue;

                _lit.Add(new Lit { Mono = mono, Tint = TintFor(_hits[i].Count) });

                if (!say) continue;

                CombatText.SpawnCombatText("x" + Compact(_hits[i].Count),
                                           SayFor(_hits[i].Count),
                                           mono.RenderPosition + Vector3.up * 0.8f,
                                           isDamageNumber: false,
                                           isCrit: false,
                                           localize: false);
            }

            Light();
        }

        private void Light()
        {
            if (!ChestSearchState.Point) return;

            for (int i = 0; i < _lit.Count; i++)
            {
                if (_lit[i].Mono != null) Outline(_lit[i].Mono, _lit[i].Tint);
            }
        }

        /// What EntityMonoBehaviour.UpdateOutline does, with a colour of our own
        /// rather than the one the game keeps for whatever you are standing next to.
        /// Putting an outline back is still left to the game's own call.
        private static void Outline(EntityMonoBehaviour mono, Color tint)
        {
            var interactable = mono.interactable;
            if (interactable != null)
            {
                Paint(interactable.optionalOutlineController, tint);
                if (interactable.additionalOutlineControllers != null)
                {
                    foreach (var extra in interactable.additionalOutlineControllers)
                    {
                        Paint(extra, tint);
                    }
                }

                if (interactable.spriteObjects == null) return;
                foreach (var sprite in interactable.spriteObjects)
                {
                    if (sprite != null) sprite.outlineColor = tint;
                }
                return;
            }

            if (mono.outlineControllers != null)
            {
                foreach (var controller in mono.outlineControllers) Paint(controller, tint);
            }

            if (mono.spriteObjects != null && mono.spriteObjects.Count > 0 &&
                mono.spriteObjects[0] != null)
            {
                mono.spriteObjects[0].outlineColor = tint;
            }
        }

        private static void Paint(OutlineController controller, Color tint)
        {
            if (controller == null) return;

            controller.showOutline = true;
            controller.SetColor(tint);
        }

        /// The game only drives the outline of whatever is closest to the player, so
        /// anything else this lit stays lit until it is put back.
        private void Douse()
        {
            for (int i = 0; i < _lit.Count; i++)
            {
                if (_lit[i].Mono != null) _lit[i].Mono.UpdateOutline(OutlineType.None);
            }
            _lit.Clear();
        }

        private readonly Dictionary<ObjectID, string> _containerNames = new Dictionary<ObjectID, string>();

        /// What the container itself is called, for a chest nobody has named. Cached:
        /// this runs for every row of every rescan.
        private string NameOf(ObjectID id)
        {
            if (_containerNames.TryGetValue(id, out var cached)) return cached;

            string name = ChestSearchIndex.NameOf(id) ?? "Container";
            _containerNames[id] = name;
            return name;
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
        internal static volatile bool SearchSelf = true;
        internal static volatile bool Point = true;

    }
}
