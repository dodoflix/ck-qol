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
        private const double PressSeconds = 0.2;

        /// Walking the inventory costs a database lookup per slot, so it happens at this
        /// rate rather than per frame.
        private const double ScanSeconds = 0.5;

        private EntityQuery _playerQuery;

        private double _nextScan;

        /// Slot being eaten from while the press is held, or -1.
        private int _slot = -1;
        private double _pressUntil;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<EquipmentSlotCD>(),
                ComponentType.ReadOnly<PlayerStateCD>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<HungerCD>());

            RequireForUpdate(_playerQuery);
            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/Auto Eat] eating system created");
        }

        protected override void OnUpdate()
        {
            Tick();
            base.OnUpdate();
        }

        /// Split out so the guards can return plainly rather than each repeating the
        /// base call.
        private void Tick()
        {
            if (!AutoEatState.Enabled || _playerQuery.IsEmpty)
            {
                Release();
                return;
            }

            Entity player = _playerQuery.GetSingletonEntity();
            double now = World.Time.ElapsedTime;

            // Mid-press: keep the food equipped and the button down until it elapses.
            if (_slot >= 0)
            {
                UseButton.Claim(this);

                if (now < _pressUntil)
                {
                    PlayerSlots.Press(EntityManager, player, _slot);
                }
                else
                {
                    PlayerSlots.EndPress(EntityManager, player, _slot);
                    Release();
                }
                return;
            }

            // Cheapest meaningful gate, and the common case, so nothing above it reads
            // more than one component.
            var hunger = EntityManager.GetComponentData<HungerCD>(player);
            if (hunger.hunger >= AutoEatState.Threshold) return;

            // An open inventory does not stop it, but a menu or a dragged item does
            // (see AutoFishingSystems).
            if (Manager.menu.IsAnyMenuActive() || PlayerSlots.DragInProgress()) return;

            // Never while fishing. Eating equips the food, and the game leaves the
            // fishing state as soon as the equipped item is not a rod
            // (Fishing.cs:255-262), so this would cancel the cast. Auto Fishing also
            // drives the same button, which alone would make one of them lose.
            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (playerState.HasAnyState(PlayerStateEnum.Fishing)) return;

            // EquipmentUpdateSystem:140 would drop the press anyway.
            var slotCD = EntityManager.GetComponentData<EquipmentSlotCD>(player);
            if (slotCD.secondInteractBlockedUntilRelease) return;

            // Mid-action: swapping the slot now would swing the wrong item, and a slot
            // change resets equipmentSlotCD (SelectedEquipmentChangeSystem:211-215).
            var inputData = EntityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);
            if (input.IsButtonStateSet(CommandInputButtonStateNames.Interact_HeldDown) ||
                input.IsButtonStateSet(CommandInputButtonStateNames.SecondInteract_HeldDown))
            {
                return;
            }

            if (now < _nextScan) return;
            _nextScan = now + ScanSeconds;

            int slot = FindFood(player, out int restores, out ObjectID picked);
            if (slot < 0) return;

            if (!UseButton.Claim(this)) return;

            _slot = slot;
            _pressUntil = now + PressSeconds;
            PlayerSlots.Press(EntityManager, player, slot);

            UnityEngine.Debug.Log(
                $"[CkQol/Auto Eat] eating {picked} (+{restores}) at hunger {hunger.hunger}");
        }

        private void Release()
        {
            _slot = -1;
            UseButton.Release(this);
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
                int last = PlayerSlots.End(inventories[inv], contained.Length);

                for (int i = inventories[inv].startIndex; i < last; i++)
                {
                    if (!PlayerSlots.Usable(i)) continue;

                    bool inHotbar = i >= hotbarFirst && i < hotbarLast;
                    bool allowed = inHotbar ? AutoEatState.UseHotbar
                                 : inv == 0 ? AutoEatState.UseInventory
                                            : AutoEatState.UsePouches;
                    if (!allowed) continue;

                    var objectData = contained[i].objectData;
                    if (objectData.objectID == ObjectID.None || objectData.amount <= 0) continue;

                    // First, because it rejects everything inedible in one lookup and
                    // most of what a player carries is inedible.
                    if (!PugDatabase.HasComponent<GivesConditionsWhenConsumedBuffer>(objectData))
                    {
                        continue;
                    }

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
        /// potions and everything else inedible are rejected.
        ///
        /// Caller has already established the buffer exists: PugDatabase.GetBuffer does
        /// not check that itself (PugDatabase.cs:545), unlike TryGetComponent.
        private static int HungerValue(ObjectDataCD objectData, bool cooked)
        {
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
