using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using PlayerEquipment;
using PlayerState;

namespace CkQol.Features
{
    /// Eats without changing the player's selection.
    ///
    /// Eating only ever consumes the equipped slot (EatableSlot.cs:50), so the food has
    /// to be equipped. Writing ClientInput.equippedSlotIndex here reaches
    /// SelectedEquipmentChangeSystem on the same tick (:198-204), which equips it for
    /// EatableSlot to consume. The player's real selection lives in a client
    /// MonoBehaviour that SendClientInputSystem copies back every tick, so the override
    /// lasts only as long as we write it and the hotbar never moves.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class CkQolAutoEatSystem : PugSimulationSystemBase
    {
        /// Long enough for the press to reach the server, as with the fishing hold. The
        /// 0.4s eat cooldown (EatableSlot.cs:9) rules out a second bite inside it.
        private const float PressSeconds = 0.2f;

        /// Walking the inventory is a managed database call per slot, so it happens at
        /// this rate rather than per frame.
        private const double ScanSeconds = 0.5;

        private EntityQuery _playerQuery;
        private EntityQuery _networkTimeQuery;
        private EntityQuery _tickRateQuery;

        private double _nextScan;

        /// Slot being eaten from while the press is held, or -1.
        private int _slot = -1;
        private TickTimer _press;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<EquipmentSlotCD>(),
                ComponentType.ReadOnly<PlayerStateCD>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<HungerCD>());

            _networkTimeQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            _tickRateQuery = GetEntityQuery(ComponentType.ReadOnly<ClientServerTickRate>());

            RequireForUpdate(_playerQuery);
            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/Auto Eat] eating system created");
        }

        protected override void OnUpdate()
        {
            if (!AutoEatState.Enabled || _playerQuery.IsEmpty)
            {
                _slot = -1;
                base.OnUpdate();
                return;
            }

            Entity player = _playerQuery.GetSingletonEntity();

            NetworkTick tick = _networkTimeQuery.GetSingleton<NetworkTime>().ServerTick;
            uint tps = (uint)_tickRateQuery.GetSingleton<ClientServerTickRate>().SimulationTickRate;

            var inputData = EntityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);

            // Mid-press: keep the food equipped and the button down until it elapses.
            if (_slot >= 0)
            {
                if (!_press.IsTimerElapsed(tick))
                {
                    input.equippedSlotIndex = (byte)_slot;
                    input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);
                    inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
                    EntityManager.SetComponentData(player, inputData);
                }
                else
                {
                    _slot = -1;
                }

                base.OnUpdate();
                return;
            }

            if (Manager.ui.isAnyInventoryShowing || Manager.menu.IsAnyMenuActive())
            {
                base.OnUpdate();
                return;
            }

            var hunger = EntityManager.GetComponentData<HungerCD>(player);
            if (hunger.hunger >= AutoEatState.Threshold)
            {
                base.OnUpdate();
                return;
            }

            // Auto Fishing drives the same button; both writing it in one frame is
            // undefined, so fishing wins.
            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (playerState.HasAnyState(PlayerStateEnum.Fishing))
            {
                base.OnUpdate();
                return;
            }

            // Mid-action: swapping the slot now would swing the wrong item, and a slot
            // change resets equipmentSlotCD (SelectedEquipmentChangeSystem:211-215).
            if (input.IsButtonStateSet(CommandInputButtonStateNames.Interact_HeldDown) ||
                input.IsButtonStateSet(CommandInputButtonStateNames.SecondInteract_HeldDown))
            {
                base.OnUpdate();
                return;
            }

            // EquipmentUpdateSystem:140 would drop the press anyway.
            var slotCD = EntityManager.GetComponentData<EquipmentSlotCD>(player);
            if (slotCD.secondInteractBlockedUntilRelease)
            {
                base.OnUpdate();
                return;
            }

            double now = World.Time.ElapsedTime;
            if (now < _nextScan)
            {
                base.OnUpdate();
                return;
            }
            _nextScan = now + ScanSeconds;

            int slot = FindFood(player, out int restores, out ObjectID picked);
            if (slot < 0)
            {
                base.OnUpdate();
                return;
            }

            _slot = slot;
            _press.Start(tick, PressSeconds, tps);

            input.equippedSlotIndex = (byte)slot;
            input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);
            inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
            EntityManager.SetComponentData(player, inputData);

            UnityEngine.Debug.Log($"[CkQol/Auto Eat] eating {picked} (+{restores}) at hunger {hunger.hunger}");

            base.OnUpdate();
        }

        /// The smallest edible thing in scope, so a big dish is not spent on a small
        /// gap. Returns -1 when there is nothing to eat.
        ///
        /// Index 0 of InventoryBuffer is the main inventory and 1.. are the pouches, all
        /// sub-ranges of one buffer. The hotbar is not a container of its own but a
        /// moving window over those (ItemSlotsBarUI.cs:264-289), so a slot inside it is
        /// governed by the hotbar setting whichever container it sits in.
        private int FindFood(Entity player, out int restores, out ObjectID picked)
        {
            restores = 0;
            picked = ObjectID.None;

            var contained = EntityManager.GetBuffer<ContainedObjectsBuffer>(player, true);
            var inventories = EntityManager.GetBuffer<InventoryBuffer>(player, true);

            var local = Manager.main != null ? Manager.main.player : null;
            int hotbarFirst = local != null ? local.hotbarStartIndex : 0;
            int hotbarLast = local != null ? local.hotbarEndIndex : 0;

            int best = -1;

            for (int inv = 0; inv < inventories.Length; inv++)
            {
                int first = inventories[inv].startIndex;
                int last = first + inventories[inv].size;
                if (last > contained.Length) last = contained.Length;

                for (int i = first; i < last; i++)
                {
                    // equippedSlotIndex is a byte, and SelectedEquipmentChangeSystem
                    // indexes the buffer with it and no bounds check.
                    if (i < 0 || i > byte.MaxValue) continue;

                    bool inHotbar = i >= hotbarFirst && i < hotbarLast;
                    bool allowed = inHotbar ? AutoEatState.UseHotbar
                                 : inv == 0 ? AutoEatState.UseInventory
                                            : AutoEatState.UsePouches;
                    if (!allowed) continue;

                    var objectData = contained[i].objectData;
                    if (objectData.objectID == ObjectID.None || objectData.amount <= 0) continue;

                    bool cooked = PugDatabase.HasComponent<CookedFoodCD>(objectData);
                    if (cooked && !AutoEatState.AllowCooked) continue;

                    int value = HungerValue(objectData, cooked);
                    if (value <= 0) continue;

                    if (best < 0 || value < restores)
                    {
                        best = i;
                        restores = value;
                        picked = objectData.objectID;
                    }
                }
            }

            return best;
        }

        /// Hunger an item restores, or 0 if it restores none - which is also how
        /// potions and everything inedible are rejected.
        private static int HungerValue(ObjectDataCD objectData, bool cooked)
        {
            // PugDatabase.GetBuffer does not check the prefab actually has the buffer
            // (PugDatabase.cs:545), unlike TryGetComponent - so anything inedible throws.
            if (!PugDatabase.HasComponent<GivesConditionsWhenConsumedBuffer>(objectData)) return 0;

            var conditions = PugDatabase.GetBuffer<GivesConditionsWhenConsumedBuffer>(objectData);
            for (int i = 0; i < conditions.Length; i++)
            {
                var container = conditions[i].conditionDataContainer;
                var data = cooked ? container.conditionDataWhenCooked : container.conditionData;
                if (data.conditionID == ConditionID.HungerAddition) return data.value;
            }
            return 0;
        }
    }
}
