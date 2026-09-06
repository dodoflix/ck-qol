using System.Collections.Generic;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using PlayerEquipment;
using PlayerState;

namespace CkQol.Features
{
    /// Resummons minions to match the mix the player summoned themselves.
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

        /// Live minions per type, rebuilt each scan.
        private readonly Dictionary<ObjectID, int> _census = new Dictionary<ObjectID, int>();

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
                if (now < _pressUntil) PlayerSlots.Press(EntityManager, player, _slot);
                else _slot = -1;
                return;
            }

            if (Manager.ui.isAnyInventoryShowing || Manager.menu.IsAnyMenuActive()) return;

            // Equipping a staff mid-cast would cancel the cast: the game leaves the
            // fishing state as soon as the equipped item is not a rod
            // (Fishing.cs:255-262).
            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (playerState.HasAnyState(PlayerStateEnum.Fishing)) return;

            // Auto Eat drives the same button and gets it first; going hungry matters
            // more than a minion being a second late.
            if (AutoEatState.Busy) return;

            var slotCD = EntityManager.GetComponentData<EquipmentSlotCD>(player);
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
            Learn(player);

            ObjectID missing = FirstMissing();
            if (missing == ObjectID.None) return;

            int slot = FindWeapon(player, missing);
            if (slot < 0) return;

            _slot = slot;
            _pressUntil = now + PressSeconds;
            PlayerSlots.Press(EntityManager, player, slot);

            UnityEngine.Debug.Log(
                $"[CkQol/Auto Summon] summoning {missing} ({Alive()}/{AutoSummonState.Wanted.Count} alive)");
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

        /// Adopts anything the player summoned themselves.
        ///
        /// No input inspection: our own summons never take a type above its wanted
        /// count, so a census above it can only have come from the player.
        private void Learn(Entity player)
        {
            var wanted = AutoSummonState.Wanted;

            foreach (var entry in _census)
            {
                int have = Count(wanted, entry.Key);
                for (int i = have; i < entry.Value; i++) wanted.Add(entry.Key);
            }

            // Trim oldest first, mirroring the game's own over-cap rule: it culls the
            // minion with the lowest remaining lifespan (MinionHandlerSystem.cs:198-220).
            int cap = MinionExtensions.GetMaxMinions(
                EntityManager.GetBuffer<SummarizedConditionEffectsBuffer>(player, true));

            while (wanted.Count > cap && wanted.Count > 0) wanted.RemoveAt(0);
        }

        /// A wanted type that is short, or None.
        private ObjectID FirstMissing()
        {
            var wanted = AutoSummonState.Wanted;
            if (wanted.Count == 0) return ObjectID.None;

            // Off, this waits for a wipe rather than topping up.
            if (!AutoSummonState.TopUp && Alive() > 0) return ObjectID.None;

            for (int i = 0; i < wanted.Count; i++)
            {
                ObjectID id = wanted[i];
                _census.TryGetValue(id, out int alive);
                if (alive < Count(wanted, id)) return id;
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

                    var objectData = contained[i].objectData;
                    if (objectData.objectID == ObjectID.None || objectData.amount <= 0) continue;

                    // Guarded: PugDatabase.GetComponent throws on a prefab without the
                    // component, the same trap as GetBuffer (PugDatabase.cs:545).
                    if (!PugDatabase.HasComponent<SecondaryUseCD>(objectData)) continue;

                    var use = PugDatabase.GetComponent<SecondaryUseCD>(objectData);
                    if (use.summonsMinion && use.minionToSpawn == minion) return i;
                }
            }

            return -1;
        }
    }
}
