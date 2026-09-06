using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CkQol.Features
{
    /// Shared by the features that use an inventory item without changing what the
    /// player has selected.
    internal static class PlayerSlots
    {
        /// Equips a slot and holds the use button, for this frame only.
        ///
        /// Using an item always consumes the equipped slot, so it has to be equipped.
        /// Writing ClientInput.equippedSlotIndex reaches SelectedEquipmentChangeSystem
        /// on the same tick (:198-204). The player's real selection lives in a client
        /// MonoBehaviour that SendClientInputSystem copies back every tick, so the
        /// override lasts only as long as it is written and the hotbar never moves.
        internal static void Press(EntityManager entityManager, Entity player, int slot,
                                   bool aimAtSelf = false)
        {
            var inputData = entityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);

            input.equippedSlotIndex = (byte)slot;
            input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);

            // Summons land at player position + aimDirection, or for command-minion
            // weapons up to 12 tiles along the aim marker (SummoningWeaponSlot.cs:74-77),
            // so an untouched aim drops them wherever the mouse happens to be.
            //
            // mouseOrJoystickWorldPoint is an absolute point, not an offset: leaving it
            // at zero would aim at the world origin rather than at the player.
            if (aimAtSelf && entityManager.HasComponent<LocalTransform>(player))
            {
                float3 here = entityManager.GetComponentData<LocalTransform>(player).Position;

                input.aimDirection = default;
                input.targetingDirection = default;
                input.mouseOrJoystickWorldPoint = new float2(here.x, here.z);
            }

            inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
            entityManager.SetComponentData(player, inputData);
        }

        /// Whether an index can be used as ClientInput.equippedSlotIndex, which is a
        /// byte that SelectedEquipmentChangeSystem indexes the buffer with and no
        /// bounds check.
        internal static bool Usable(int slot) => slot >= 0 && slot <= byte.MaxValue;

        /// Last slot of a container, clamped to the buffer. Index 0 of InventoryBuffer
        /// is the main inventory and 1.. are the pouches, all ranges into one
        /// ContainedObjectsBuffer.
        internal static int End(in InventoryBuffer inventory, int containedLength)
        {
            int last = inventory.startIndex + inventory.size;
            return last > containedLength ? containedLength : last;
        }
    }
}
