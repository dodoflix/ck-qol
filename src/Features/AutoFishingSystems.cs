using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using PlayerEquipment;
using PlayerState;

namespace CkQol.Features
{
    /// Per-player state for the reeler: just the short hold timer.
    public struct CkQolAutoReelCD : IComponentData
    {
        public TickTimer ReelTimer;

        /// Counts down the configured pause between the bite and the reel.
        public TickTimer DelayTimer;
    }

    /// Reels in for the local player.
    ///
    /// Runs after SendClientInputSystem and sets the SecondInteract button in the
    /// player's ClientInputData, which is the same path a real button press takes -
    /// so the game's own fishing loop does the catching and carries on to the next
    /// cast. Nothing about fishing is reimplemented here.
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class CkQolAutoReelSystem : PugSimulationSystemBase
    {
        private EntityQuery _playerQuery;
        private EntityQuery _networkTimeQuery;
        private EntityQuery _tickRateQuery;

        /// World time the player's own reel began, or -1 when they are not reeling.
        private double _manualHoldStart = -1d;

        /// Whether the previous frame's button press was ours.
        ///
        /// We write the press into the same ClientInputData a real one arrives in, so
        /// without this the next frame could read our own press back and time it as if
        /// the player had done it.
        private bool _pressedLastFrame;

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadWrite<ClientInputData>(),
                ComponentType.ReadOnly<EquipmentSlotCD>(),
                ComponentType.ReadOnly<PlayerStateCD>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>(),
                ComponentType.ReadOnly<FishingStateCD>());

            _networkTimeQuery = GetEntityQuery(ComponentType.ReadOnly<NetworkTime>());
            _tickRateQuery = GetEntityQuery(ComponentType.ReadOnly<ClientServerTickRate>());

            RequireForUpdate(_playerQuery);
            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/AutoFishing] reel system created");
        }

        private static void StartReel(ref CkQolAutoReelCD state, ref ClientInput input,
                                      NetworkTick tick, uint tps)
        {
            state.ReelTimer.Start(tick, AutoFishingState.EffectiveReelHold, tps);
            input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);

            AutoFishingState.RaiseShoalCheck();
        }

        /// Drops a half-finished measurement. Walking away mid-hold would otherwise
        /// time the gap until the next reel instead of the reel itself.
        private void Forget()
        {
            _manualHoldStart = -1d;
            _pressedLastFrame = false;
        }

        /// The player has stopped fishing, so the learned timing goes with it.
        ///
        /// Safe to key off the Fishing state: the game does not leave it between
        /// casts - a catch sets queueThrowAgain and throws again from inside the
        /// state, and PopState only runs in Fishing.OnExitFishing.
        private void EndSession()
        {
            Forget();
            AutoFishingState.ClearLearnedHold();
        }

        protected override void OnUpdate()
        {
            if (!AutoFishingState.ReelEnabled || _playerQuery.IsEmpty)
            {
                EndSession();
                base.OnUpdate();
                return;
            }

            Entity player = _playerQuery.GetSingletonEntity();

            var slot = EntityManager.GetComponentData<EquipmentSlotCD>(player);
            if (slot.slotType != EquipmentSlotType.FishingRodSlot)
            {
                EndSession();
                base.OnUpdate();
                return;
            }

            // A menu or inventory pauses the mod but the rod is still out, so the
            // learned timing survives - unlike putting the rod away.
            if (Manager.ui.isAnyInventoryShowing || Manager.menu.IsAnyMenuActive())
            {
                Forget();
                base.OnUpdate();
                return;
            }

            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (!playerState.HasAnyState(PlayerStateEnum.Fishing))
            {
                EndSession();
                base.OnUpdate();
                return;
            }

            if (!EntityManager.HasComponent<CkQolAutoReelCD>(player))
            {
                EntityManager.AddComponentData(player, new CkQolAutoReelCD
                {
                    ReelTimer = new TickTimer(0),
                    DelayTimer = new TickTimer(0)
                });
            }

            var fishState = EntityManager.GetComponentData<FishingStateCD>(player);
            var state = EntityManager.GetComponentData<CkQolAutoReelCD>(player);

            var inputData = EntityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);

            NetworkTick tick = _networkTimeQuery.GetSingleton<NetworkTime>().ServerTick;
            uint tps = (uint)_tickRateQuery.GetSingleton<ClientServerTickRate>().SimulationTickRate;

            double now = World.Time.ElapsedTime;

            // Reeling by hand wins. Without this the mod's own hold would fight the
            // player's, and neither press would land cleanly.
            if (!state.ReelTimer.isRunning && !_pressedLastFrame &&
                input.IsButtonStateSet(CommandInputButtonStateNames.SecondInteract_HeldDown))
            {
                // Time it, so auto reel can use the player's own timing. Only a hold
                // that started on a bite counts - anything else is not a reel.
                if (_manualHoldStart < 0d && AutoFishingState.LearnEnabled &&
                    fishState.fishIsNibbling)
                {
                    _manualHoldStart = now;
                }

                // They beat us to it - drop any pending pull so we do not yank the line
                // a second time once the delay runs out.
                if (state.DelayTimer.isRunning)
                {
                    state.DelayTimer.Stop(tick);
                    EntityManager.SetComponentData(player, state);
                }

                _pressedLastFrame = false;
                inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
                EntityManager.SetComponentData(player, inputData);
                base.OnUpdate();
                return;
            }

            // Not holding any more: whatever was being timed has ended.
            if (_manualHoldStart >= 0d)
            {
                AutoFishingState.ReportLearnedHold((float)(now - _manualHoldStart));
                _manualHoldStart = -1d;
            }

            bool pressing = false;

            if (state.ReelTimer.isRunning)
            {
                if (!state.ReelTimer.IsTimerElapsed(tick))
                {
                    input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);
                    pressing = true;
                }
                else
                {
                    state.ReelTimer.Stop(tick);
                }
            }
            else if (state.DelayTimer.isRunning)
            {
                // Waiting out the configured pause. If the fish gets bored first there
                // is nothing left to reel, so drop it rather than yanking an empty line.
                if (!fishState.fishIsNibbling)
                {
                    state.DelayTimer.Stop(tick);
                }
                else if (state.DelayTimer.IsTimerElapsed(tick))
                {
                    state.DelayTimer.Stop(tick);
                    StartReel(ref state, ref input, tick, tps);
                    pressing = true;
                }
            }
            else if (fishState.fishIsNibbling && !fishState.isFishingAtOctopusBoss)
            {
                float delay = AutoFishingState.PullDelaySeconds;
                if (delay > 0f)
                {
                    state.DelayTimer.Start(tick, delay, tps);
                }
                else
                {
                    StartReel(ref state, ref input, tick, tps);
                    pressing = true;
                }
            }

            _pressedLastFrame = pressing;

            inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
            EntityManager.SetComponentData(player, inputData);
            EntityManager.SetComponentData(player, state);

            base.OnUpdate();
        }
    }

    /// Stops baited fishing spots from depleting.
    ///
    /// The game counts catches in the shoal's own ObjectDataCD.amount and destroys the
    /// shoal once it reaches 3 (half the time) or 6. That counter is only ever bumped
    /// from Burst-compiled jobs, so there is no managed method to patch - putting the
    /// counter back to zero afterwards is the only thing a mod can do.
    ///
    /// Cost here is per read, not per shoal: the query asks for disabled entities too,
    /// so it matches every shoal in the explored world, and touching it from the main
    /// thread blocks on the jobs writing that data. So it reads only in the seconds
    /// after a bite, when the counter can actually have moved, plus a slow backstop
    /// for hand-reeling and dedicated servers.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CkQolInfiniteShoalSystem : PugSimulationSystemBase
    {
        /// How often to look while a catch is resolving.
        private const double PollSeconds = 0.5;

        /// How long to keep looking after a bite. The signal fires when reeling
        /// starts; the counter moves when the catch resolves a moment later.
        private const double SweepWindowSeconds = 2.5;

        /// Runs regardless of any signal, for players who reel by hand and for
        /// dedicated servers where no local client can signal at all.
        private const double BackstopSeconds = 15.0;

        private double _nextPoll;
        private double _sweepUntil;
        private double _nextBackstop;

        private EntityQuery _shoals;
        private EntityTypeHandle _entityHandle;
        private ComponentTypeHandle<ObjectDataCD> _objectDataHandle;

        protected override void OnCreate()
        {
            _shoals = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<FishShoalCD>(),
                    ComponentType.ReadOnly<ObjectDataCD>()
                },
                Options = EntityQueryOptions.IncludeDisabledEntities
            });

            _entityHandle = GetEntityTypeHandle();

            // Read-only on purpose. Taking a read-write handle bumps a chunk's change
            // version even when nothing is stored, which would defeat any later use of
            // a change filter; the rare reset goes through the EntityManager instead.
            _objectDataHandle = GetComponentTypeHandle<ObjectDataCD>(true);

            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/AutoFishing] shoal system created");
        }

        protected override void OnUpdate()
        {
            if (!AutoFishingState.ShoalEnabled)
            {
                // Zeroed so the tick it comes back on sweeps at once - every catch made
                // while it was off went unseen.
                _sweepUntil = 0d;
                _nextPoll = 0d;
                _nextBackstop = 0d;

                base.OnUpdate();
                return;
            }

            // Nothing above the sweep may touch a query or the EntityManager: those are
            // the sync points this is avoiding. Two double compares and a bool are free.
            double now = World.Time.ElapsedTime;

            if (AutoFishingState.ConsumeShoalCheck())
            {
                _sweepUntil = now + SweepWindowSeconds;
                _nextPoll = 0d;
            }

            if ((now < _sweepUntil && now >= _nextPoll) || now >= _nextBackstop)
            {
                _nextPoll = now + PollSeconds;
                _nextBackstop = now + BackstopSeconds;
                ResetDepletedShoals();
            }

            base.OnUpdate();
        }

        private void ResetDepletedShoals()
        {
            NativeArray<ArchetypeChunk> chunks = _shoals.ToArchetypeChunkArray(Allocator.Temp);
            if (chunks.Length == 0)
            {
                chunks.Dispose();
                return;
            }

            // Handles go stale after any structural change.
            _entityHandle.Update(this);
            _objectDataHandle.Update(this);

            // Collected first: SetComponentData is a sync point that can invalidate the
            // chunk array being walked.
            NativeList<Entity> toReset = default;

            for (int c = 0; c < chunks.Length; c++)
            {
                ArchetypeChunk chunk = chunks[c];
                NativeArray<ObjectDataCD> objectData = chunk.GetNativeArray(ref _objectDataHandle);

                NativeArray<Entity> entities = default;
                bool haveEntities = false;

                for (int i = 0; i < chunk.Count; i++)
                {
                    if (objectData[i].amount == 0) continue;

                    if (!haveEntities)
                    {
                        entities = chunk.GetNativeArray(_entityHandle);
                        haveEntities = true;
                    }

                    if (!toReset.IsCreated) toReset = new NativeList<Entity>(8, Allocator.Temp);
                    toReset.Add(entities[i]);
                }
            }

            chunks.Dispose();
            if (!toReset.IsCreated) return;

            for (int i = 0; i < toReset.Length; i++)
            {
                Entity shoal = toReset[i];
                var objectData = EntityManager.GetComponentData<ObjectDataCD>(shoal);
                objectData.amount = 0;
                EntityManager.SetComponentData(shoal, objectData);
            }

            toReset.Dispose();
        }
    }
}
