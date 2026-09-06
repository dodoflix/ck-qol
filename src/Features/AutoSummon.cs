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

        private const string KeepMix = "Keep mix";
        private const string Latest = "Latest summon";
        private const string Split = "Split hotbar";

        private readonly ChoiceSetting _mode =
            new ChoiceSetting("Mode", "Mode", new[] { KeepMix, Latest, Split }, KeepMix,
                              "Keep mix holds the set you summoned by hand. Latest " +
                              "summon moves everything to the type you summoned last. " +
                              "Split hotbar divides your cap between the summoning " +
                              "weapons on your hotbar.");

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

        public override IEnumerable<ModSetting> GetSettings()
        {
            yield return _mode;
            yield return _toggleKey;
            yield return _toggleModifier;
        }

        public override void Init()
        {
            base.Init();
            Log($"started (mode={_mode.Value}, toggle={_toggleModifier.Value}+{_toggleKey.Name})");
        }

        public override void Shutdown()
        {
            base.Shutdown();
            Log("stopped");
        }

        private CkQolHint _hint;

        /// The HUD only exists in game, and not on the first frame of it, so the hint
        /// is added on the first update that finds it.
        public override void Update()
        {
            if (_hint != null || Manager.main == null || Manager.main.player == null) return;
            _hint = GameHints.Install(HintLabel, HintVisible);
        }

        private string HintLabel()
        {
            string key = AutoSummonState.ToggleModifier == Modifier.None
                ? _toggleKey.Name
                : $"{AutoSummonState.ToggleModifier}+{_toggleKey.Name}";
            return $"{key}  Auto summon: {(AutoSummonState.Armed ? "on" : "off")}";
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
                                                        : SummonMode.KeepMix;

            AutoSummonState.ToggleKey = (int)_toggleKey.Value;
            AutoSummonState.ToggleModifier =
                _toggleModifier.Value == Shift ? Modifier.Shift :
                _toggleModifier.Value == Alt ? Modifier.Alt :
                _toggleModifier.Value == NoModifier ? Modifier.None : Modifier.Ctrl;

            if (!Running) AutoSummonState.Forget();
            else AutoSummonState.Armed = true;
        }
    }

    internal enum SummonMode { KeepMix, Latest, SplitHotbar }

    internal enum Modifier { None, Ctrl, Shift, Alt }

    /// What the summoning system reads. Separate from the feature so the system holds
    /// no reference to it and costs one bool test while off.
    internal static class AutoSummonState
    {
        internal static volatile bool Enabled;
        internal static volatile SummonMode Mode = SummonMode.KeepMix;
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
