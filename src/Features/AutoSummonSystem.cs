using System.Collections.Generic;
using CkQol.Config;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using PlayerEquipment;
using PlayerState;
using UnityEngine;

namespace CkQol.Features
{
    /// Resummons minions towards a target set, which the mode decides.
    ///
    /// Summoning consumes the equipped slot (SummoningWeaponSlot.cs:21,
    /// MinionHandlerSystem.cs:3789), so the weapon is equipped through
    /// PlayerSlots.Press rather than by changing what the player has selected.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class CkQolAutoSummonSystem : PugSimulationSystemBase
    {
        private const double PressSeconds = 0.2;

        /// Above the 0.6s summon cooldown (SummoningWeaponSlot.cs:63), and long enough
        /// for a death to reach the client: the count is recomputed server-side each
        /// tick, so it lags by a tick plus the round trip.
        private const double ScanSeconds = 1.0;

        private EntityQuery _playerQuery;
        private EntityQuery _minionQuery;

        private double _nextScan;

        private int _slot = -1;
        private double _pressUntil;

        private readonly Dictionary<ObjectID, int> _census = new Dictionary<ObjectID, int>();
        private readonly Dictionary<ObjectID, int> _previous = new Dictionary<ObjectID, int>();
        private readonly Dictionary<ObjectID, int> _target = new Dictionary<ObjectID, int>();

        /// What we summoned last, so the next census does not mistake it for the
        /// player's own.
        private ObjectID _justSummoned = ObjectID.None;

        private bool _toggleHeld;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<EquipmentSlotCD>(),
                ComponentType.ReadOnly<PlayerStateCD>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<MinionCountTrackerCD>());

            // None on an enableable component excludes the entities where it is on, so
            // dying minions drop out.
            _minionQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<MinionCD>(),
                    ComponentType.ReadOnly<OwnerReferenceCD>(),
                    ComponentType.ReadOnly<ObjectDataCD>()
                },
                None = new[] { ComponentType.ReadOnly<EntityDestroyedCD>() }
            });

            RequireForUpdate(_playerQuery);
            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/Auto Summon] summoning system created");
        }

        protected override void OnUpdate()
        {
            Tick();
            base.OnUpdate();
        }

        private void Tick()
        {
            if (!AutoSummonState.Enabled || _playerQuery.IsEmpty)
            {
                _slot = -1;
                return;
            }

            Entity player = _playerQuery.GetSingletonEntity();
            double now = World.Time.ElapsedTime;

            if (_slot >= 0)
            {
                if (now < _pressUntil)
                {
                    PlayerSlots.Press(EntityManager, player, _slot,
                                      aimAtSelf: AutoSummonState.AimAtSelf);
                }
                else _slot = -1;
                return;
            }

            if (Manager.ui.isAnyInventoryShowing || Manager.menu.IsAnyMenuActive()) return;

            var slotCD = EntityManager.GetComponentData<EquipmentSlotCD>(player);
            CheckToggle(slotCD);
            if (!AutoSummonState.Armed) return;

            // Equipping a staff mid-cast would cancel the cast: the game leaves the
            // fishing state as soon as the equipped item is not a rod
            // (Fishing.cs:255-262).
            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (playerState.HasAnyState(PlayerStateEnum.Fishing)) return;

            // Auto Eat drives the same button and gets it first; going hungry matters
            // more than a minion being a second late.
            if (AutoEatState.Busy) return;

            if (slotCD.secondInteractBlockedUntilRelease) return;

            var inputData = EntityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);
            if (input.IsButtonStateSet(CommandInputButtonStateNames.Interact_HeldDown) ||
                input.IsButtonStateSet(CommandInputButtonStateNames.SecondInteract_HeldDown))
            {
                return;
            }

            if (now < _nextScan) return;
            _nextScan = now + ScanSeconds;

            Census(player);
            Learn();

            int cap = MinionExtensions.GetMaxMinions(
                EntityManager.GetBuffer<SummarizedConditionEffectsBuffer>(player, true));

            // Never summon past the cap. The game would accept it and cull the minion
            // with the least life left (MinionHandlerSystem.cs:198-220), so a change of
            // plan would replace healthy minions instead of filling in as they expire.
            if (Alive() >= cap) return;

            BuildTarget(player, cap);

            ObjectID missing = FirstMissing();
            if (missing == ObjectID.None) return;

            int slot = FindWeapon(player, missing);
            if (slot < 0) return;

            _slot = slot;
            _pressUntil = now + PressSeconds;
            _justSummoned = missing;
            PlayerSlots.Press(EntityManager, player, slot, aimAtSelf: AutoSummonState.AimAtSelf);

            UnityEngine.Debug.Log(
                $"[CkQol/Auto Summon] summoning {missing} ({Alive()}/{cap} alive)");
        }

        /// Switches the feature on and off for this session, without touching the
        /// saved setting. Only with a summoning weapon in hand, so the binding does not
        /// fire during unrelated play.
        private void CheckToggle(EquipmentSlotCD slotCD)
        {
            var keyboard = KeySetting.Keyboard;
            if (keyboard == null) return;

            var key = (KeyCode)AutoSummonState.ToggleKey;
            bool down = key != KeyCode.None &&
                        slotCD.slotType == EquipmentSlotType.SummoningWeaponSlot &&
                        ModifierHeld(keyboard) &&
                        keyboard.GetKey(key);

            // Edge-triggered: the key is held, so without this it would flip every frame.
            if (down && !_toggleHeld)
            {
                bool armed = !AutoSummonState.Armed;
                AutoSummonState.Armed = armed;

                if (armed)
                {
                    // Adopt whatever is alive: the player switched it back on with the
                    // minions they want already out.
                    _previous.Clear();
                    _justSummoned = ObjectID.None;
                }
                else
                {
                    AutoSummonState.Forget();
                }

                Say(armed ? "Auto summon on" : "Auto summon off");
                UnityEngine.Debug.Log($"[CkQol/Auto Summon] armed={armed}");
            }
            _toggleHeld = down;
        }

        /// Floats a line over the player, the way the game acknowledges a skill
        /// increase (PlayerController.SpawnSkillIncreasePopup).
        private static void Say(string text)
        {
            var local = Manager.main != null ? Manager.main.player : null;
            if (local == null) return;

            Vector3 position = local.RenderPosition + Vector3.up * 0.7f;
            CombatText.SpawnCombatText(text, CombatText.NumberColor.White, position,
                                       isDamageNumber: false);
        }

        private static bool ModifierHeld(Rewired.Keyboard keyboard)
        {
            switch (AutoSummonState.ToggleModifier)
            {
                case Modifier.None: return true;
                case Modifier.Shift:
                    return keyboard.GetKey(KeyCode.LeftShift) || keyboard.GetKey(KeyCode.RightShift);
                case Modifier.Alt:
                    return keyboard.GetKey(KeyCode.LeftAlt) || keyboard.GetKey(KeyCode.RightAlt);
                default:
                    return keyboard.GetKey(KeyCode.LeftControl) || keyboard.GetKey(KeyCode.RightControl);
            }
        }

        /// Counts the local player's live minions by type.
        private void Census(Entity player)
        {
            _census.Clear();

            var owners = _minionQuery.ToComponentDataArray<OwnerReferenceCD>(Allocator.Temp);
            var objects = _minionQuery.ToComponentDataArray<ObjectDataCD>(Allocator.Temp);

            for (int i = 0; i < owners.Length; i++)
            {
                if (owners[i].owner != player) continue;

                ObjectID id = objects[i].objectID;
                _census.TryGetValue(id, out int count);
                _census[id] = count + 1;
            }

            owners.Dispose();
            objects.Dispose();
        }

        /// Adopts anything the player summoned themselves, by comparing against the
        /// previous census rather than inspecting input - which would mean edge
        /// detecting a held button and misreading summons that failed on mana.
        private void Learn()
        {
            foreach (var entry in _census)
            {
                _previous.TryGetValue(entry.Key, out int before);
                if (entry.Value <= before) continue;
                if (entry.Key == _justSummoned) continue;

                AutoSummonState.LatestSummon = entry.Key;

                int have = Count(AutoSummonState.Wanted, entry.Key);
                for (int i = have; i < entry.Value; i++) AutoSummonState.Wanted.Add(entry.Key);
            }

            _justSummoned = ObjectID.None;
            Remember();
        }

        private void Remember()
        {
            _previous.Clear();
            foreach (var entry in _census) _previous[entry.Key] = entry.Value;
        }

        /// How many of each type the current mode wants.
        private void BuildTarget(Entity player, int cap)
        {
            _target.Clear();

            switch (AutoSummonState.Mode)
            {
                case SummonMode.Latest:
                    if (AutoSummonState.LatestSummon != ObjectID.None)
                    {
                        _target[AutoSummonState.LatestSummon] = cap;
                    }
                    break;

                case SummonMode.SplitHotbar:
                    SplitHotbar(player, cap);
                    break;

                default:
                    // Trim oldest first, mirroring the game's own over-cap rule.
                    var wanted = AutoSummonState.Wanted;
                    while (wanted.Count > cap && wanted.Count > 0) wanted.RemoveAt(0);

                    for (int i = 0; i < wanted.Count; i++)
                    {
                        _target.TryGetValue(wanted[i], out int count);
                        _target[wanted[i]] = count + 1;
                    }
                    break;
            }
        }

        /// Divides the cap between the summoning weapons on the open hotbar row, giving
        /// the remainder to the leftmost.
        private void SplitHotbar(Entity player, int cap)
        {
            var local = Manager.main != null ? Manager.main.player : null;
            if (local == null) return;

            var contained = EntityManager.GetBuffer<ContainedObjectsBuffer>(player, true);

            var types = new List<ObjectID>();
            int last = local.hotbarEndIndex > contained.Length ? contained.Length : local.hotbarEndIndex;

            for (int i = local.hotbarStartIndex; i < last; i++)
            {
                if (!PlayerSlots.Usable(i)) continue;
                ObjectID minion = MinionOf(contained[i].objectData);
                if (minion != ObjectID.None && !types.Contains(minion)) types.Add(minion);
            }

            if (types.Count == 0) return;

            int share = cap / types.Count;
            int spare = cap % types.Count;

            for (int i = 0; i < types.Count; i++)
            {
                _target[types[i]] = share + (i < spare ? 1 : 0);
            }
        }

        /// A wanted type that is short, or None.
        private ObjectID FirstMissing()
        {
            foreach (var entry in _target)
            {
                _census.TryGetValue(entry.Key, out int alive);
                if (alive < entry.Value) return entry.Key;
            }
            return ObjectID.None;
        }

        private int Alive()
        {
            int total = 0;
            foreach (var entry in _census) total += entry.Value;
            return total;
        }

        private static int Count(List<ObjectID> list, ObjectID id)
        {
            int count = 0;
            for (int i = 0; i < list.Count; i++) if (list[i] == id) count++;
            return count;
        }

        /// The minion an item summons, or None if it is not a summoning weapon.
        private static ObjectID MinionOf(ObjectDataCD objectData)
        {
            if (objectData.objectID == ObjectID.None || objectData.amount <= 0) return ObjectID.None;

            // Guarded: PugDatabase.GetComponent throws on a prefab without the
            // component, the same trap as GetBuffer (PugDatabase.cs:545).
            if (!PugDatabase.HasComponent<SecondaryUseCD>(objectData)) return ObjectID.None;

            var use = PugDatabase.GetComponent<SecondaryUseCD>(objectData);
            return use.summonsMinion ? use.minionToSpawn : ObjectID.None;
        }

        /// A carried weapon that summons this minion, or -1.
        private int FindWeapon(Entity player, ObjectID minion)
        {
            var contained = EntityManager.GetBuffer<ContainedObjectsBuffer>(player, true);
            var inventories = EntityManager.GetBuffer<InventoryBuffer>(player, true);

            for (int inv = 0; inv < inventories.Length; inv++)
            {
                int last = PlayerSlots.End(inventories[inv], contained.Length);

                for (int i = inventories[inv].startIndex; i < last; i++)
                {
                    if (!PlayerSlots.Usable(i)) continue;
                    if (MinionOf(contained[i].objectData) == minion) return i;
                }
            }

            return -1;
        }
    }
}
