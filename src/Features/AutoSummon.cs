using System.Collections.Generic;
using CkQol.Config;
using CkQol.Native;
using PlayerEquipment;
using UnityEngine;

namespace CkQol.Features
{
    /// Keeps minions summoned. The work is in AutoSummonSystem.cs; this owns the
    /// settings and mirrors them into AutoSummonState.
    public class AutoSummon : QolFeatureBase
    {
        public override string Name => "Auto Summon";

        public override string Description =>
            "Resummons your minions as they expire or die. Never summons past your cap, " +
            "so a change of plan fills in as the old ones run out rather than culling " +
            "them.";

        private const string Learned = "Learned set";
        private const string Latest = "Latest summon";
        private const string Split = "Split hotbar";

        private readonly ChoiceSetting _mode =
            new ChoiceSetting("Mode", "Mode", new[] { Learned, Latest, Split }, Learned,
                              "Learned set keeps the minions you summoned by hand, " +
                              "each at the count you had. Latest summon moves " +
                              "everything to the type you summoned last. Split hotbar " +
                              "divides your cap between the summoning weapons on your " +
                              "hotbar.");

        private const string AtPlayer = "At player";
        private const string AtCursor = "At cursor";

        private readonly ChoiceSetting _summonAt =
            new ChoiceSetting("SummonAt", "Summon position",
                              new[] { AtPlayer, AtCursor }, AtPlayer,
                              "At player keeps minions at your feet. At cursor uses " +
                              "your aim, which for command weapons can be up to twelve " +
                              "tiles away.");

        private const string Ctrl = "Ctrl";
        private const string Shift = "Shift";
        private const string Alt = "Alt";
        private const string NoModifier = "None";

        private readonly KeySetting _toggleKey =
            new KeySetting("ToggleKey", "Toggle key", KeyCode.R,
                           "With a summoning weapon in hand, switches auto summon on " +
                           "and off for this session. Switching it off also forgets " +
                           "the minions it learned. Never changes the setting above.");

        private readonly ChoiceSetting _toggleModifier =
            new ChoiceSetting("ToggleModifier", "Toggle modifier",
                              new[] { Ctrl, Shift, Alt, NoModifier }, Ctrl,
                              "Held alongside the toggle key.");

        /// The session toggle, persisted but deliberately not a menu row: it would sit
        /// next to Enabled meaning almost the same thing. Bound by hand in Init, since
        /// only settings returned below get a row and a binding.
        private readonly BoolSetting _armed =
            new BoolSetting("Armed", "Armed", true,
                            "Whether the toggle key has auto summon switched on.");

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _mode;
            yield return _summonAt;
            yield return _toggleKey;
            yield return _toggleModifier;
        }

        public override void Init()
        {
            base.Init();

            // Restored rather than forced on: quitting with it off and rejoining would
            // otherwise summon at once, which in Split hotbar mode needs nothing
            // learned to act on.
            _armed.Bind(CkQolMod.ModName, Name);
            AutoSummonState.Armed = _armed.Value;

            Log($"started (mode={_mode.Value}, at={_summonAt.Value}, toggle={_toggleModifier.Value}+{_toggleKey.Name})");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Log("stopped");
        }

        private CkQolHint _hint;

        /// The toggle is flipped by the summoning system, which has no business writing
        /// config, so the change is picked up here.
        private void SaveArmed()
        {
            if (_armed.Value != AutoSummonState.Armed) _armed.Value = AutoSummonState.Armed;
        }

        /// The HUD only exists in game, and not on the first frame of it, so the hint
        /// is added on the first update that finds it.
        public override void Update()
        {
            SaveArmed();

            if (_hint != null || Manager.main == null || Manager.main.player == null) return;
            _hint = GameHints.Install(HintLabel, HintVisible);
            if (_hint != null) _hint.Icon = HintIcon;
        }

        /// Just the binding: the icon says which weapon it is about, and the row has
        /// only room for labels the size of the game's own.
        private string HintLabel() =>
            AutoSummonState.ToggleModifier == Modifier.None
                ? _toggleKey.Name
                : $"{AutoSummonState.ToggleModifier}+{_toggleKey.Name}";

        /// Shown on the hint. Any item's icon works; a clock reads as "this keeps
        /// happening on its own" better than the weapon did, and the weapon is already
        /// in the player's hand when the hint is up.
        private const ObjectID HintIconItem = ObjectID.SeismicClock;

        private Sprite _iconSprite;
        private bool _iconResolved;

        /// Resolved once: unlike the held item, this never changes.
        private void HintIcon(SpriteRenderer renderer)
        {
            if (!_iconResolved)
            {
                _iconResolved = true;
                var info = PugDatabase.GetObjectInfo(HintIconItem);
                _iconSprite = info != null ? info.icon : null;
            }

            renderer.sprite = _iconSprite;
        }

        /// Only with a summoning weapon in hand, which is also when the binding works.
        private bool HintVisible()
        {
            if (!AutoSummonState.Enabled || _toggleKey.Value == KeyCode.None) return false;

            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || player.guestMode) return false;

            var slot = player.GetEquippedSlot();
            return slot != null && slot.GetSlotType() == EquipmentSlotType.SummoningWeaponSlot;
        }

        protected override void Apply()
        {
            AutoSummonState.Enabled = Running;

            AutoSummonState.Mode = _mode.Value == Latest ? SummonMode.Latest
                                 : _mode.Value == Split ? SummonMode.SplitHotbar
                                                        : SummonMode.Learned;

            AutoSummonState.AimAtSelf = _summonAt.Value != AtCursor;
            AutoSummonState.ToggleKey = (int)_toggleKey.Value;
            AutoSummonState.ToggleModifier =
                _toggleModifier.Value == Shift ? Modifier.Shift :
                _toggleModifier.Value == Alt ? Modifier.Alt :
                _toggleModifier.Value == NoModifier ? Modifier.None : Modifier.Ctrl;

            if (!Running) AutoSummonState.Forget();
        }
    }

    internal enum SummonMode { Learned, Latest, SplitHotbar }

    internal enum Modifier { None, Ctrl, Shift, Alt }

    /// What the summoning system reads. Separate from the feature so the system holds
    /// no reference to it and costs one bool test while off.
    internal static class AutoSummonState
    {
        internal static volatile bool Enabled;
        internal static volatile SummonMode Mode = SummonMode.Learned;
        /// Aim is pinned to the player while pressing, so summons land at their
        /// feet rather than wherever the cursor is.
        internal static volatile bool AimAtSelf = true;

        internal static volatile int ToggleKey;
        internal static volatile Modifier ToggleModifier = Modifier.Ctrl;

        /// Session switch, separate from the Enabled setting so it can be flipped
        /// mid-fight without writing config. Reset to on whenever the feature starts.
        internal static volatile bool Armed = true;

        /// The mix to keep alive, in the order learned. Session only.
        internal static readonly List<ObjectID> Wanted = new List<ObjectID>();

        /// The type the player summoned most recently, for Latest mode.
        internal static ObjectID LatestSummon = ObjectID.None;

        internal static void Forget()
        {
            Wanted.Clear();
            LatestSummon = ObjectID.None;
        }
    }
}
