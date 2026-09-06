using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.NetCode;
using PlayerEquipment;
using PlayerState;

namespace CkQol.Features
{
    public struct CkQolAutoReelCD : IComponentData
    {
        public TickTimer ReelTimer;
    }

    /// Sets SecondInteract in ClientInputData, the same path a real press takes.
    ///
    /// That one button does three jobs by phase: held while castTimer runs it charges
    /// the cast (Fishing.cs:462), pressed on a nibble it hooks, pressed with the line
    /// out and no bite it pulls up empty (Fishing.cs:272).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(RunSimulationSystemGroup), OrderLast = true)]
    [UpdateAfter(typeof(SendClientInputSystem))]
    public partial class CkQolAutoReelSystem : PugSimulationSystemBase
    {
        private EntityQuery _playerQuery;
        private EntityQuery _networkTimeQuery;
        private EntityQuery _tickRateQuery;

        /// Start of the player's own hold, or -1.
        private double _holdStart = -1d;

        /// Our press lands in the same field a real one does; without this we would
        /// read it back next frame and time it as the player's.
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
            UnityEngine.Debug.Log("[CkQol/Auto Fishing] fishing system created");
        }

        private static void StartReel(ref CkQolAutoReelCD state, ref ClientInput input,
                                      NetworkTick tick, uint tps)
        {
            state.ReelTimer.Start(tick, AutoFishingState.ReelHoldSeconds, tps);
            input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);

            AutoFishingState.RaiseShoalCheck();
        }

        /// Drops a half-finished measurement, which would otherwise time the gap
        /// until the next press.
        private void Forget()
        {
            _holdStart = -1d;
            _pressedLastFrame = false;
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
            if (!AutoFishingState.ReelEnabled || _playerQuery.IsEmpty)
            {
                Forget();
                return;
            }

            Entity player = _playerQuery.GetSingletonEntity();

            var slot = EntityManager.GetComponentData<EquipmentSlotCD>(player);
            if (slot.slotType != EquipmentSlotType.FishingRodSlot)
            {
                Forget();
                return;
            }

            // Only a menu stops it. An open inventory does not: the simulation never
            // checks the UI, and the game guards the cases that matter itself by
            // refusing to act while an item is held on the cursor.
            if (Manager.menu.IsAnyMenuActive())
            {
                Forget();
                return;
            }

            var playerState = EntityManager.GetComponentData<PlayerStateCD>(player);
            if (!playerState.HasAnyState(PlayerStateEnum.Fishing))
            {
                Forget();
                return;
            }

            if (!EntityManager.HasComponent<CkQolAutoReelCD>(player))
            {
                EntityManager.AddComponentData(player, new CkQolAutoReelCD
                {
                    ReelTimer = new TickTimer(0)
                });
            }

            var fishState = EntityManager.GetComponentData<FishingStateCD>(player);
            var state = EntityManager.GetComponentData<CkQolAutoReelCD>(player);

            var inputData = EntityManager.GetComponentData<ClientInputData>(player);
            ClientInput input = UnsafeUtility.As<ClientInputData, ClientInput>(ref inputData);

            NetworkTick tick = _networkTimeQuery.GetSingleton<NetworkTime>().ServerTick;
            uint tps = (uint)_tickRateQuery.GetSingleton<ClientServerTickRate>().SimulationTickRate;

            double now = World.Time.ElapsedTime;

            // The player's own press wins; ours would fight it.
            if (!state.ReelTimer.isRunning && !_pressedLastFrame &&
                input.IsButtonStateSet(CommandInputButtonStateNames.SecondInteract_HeldDown))
            {
                if (_holdStart < 0d && AutoFishingState.LearnEnabled) _holdStart = now;

                _pressedLastFrame = false;
                inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
                EntityManager.SetComponentData(player, inputData);
                return;
            }

            if (_holdStart >= 0d)
            {
                AutoFishingState.ReportHold((float)(now - _holdStart));
                _holdStart = -1d;
            }

            bool pressing = false;

            // Cast distance is castTimer's elapsed ratio when the button comes up,
            // and the rod throws as soon as it is released - so letting go early lands
            // the line at the player's feet.
            if (fishState.castTimer.isRunning &&
                !fishState.castTimer.IsTimerElapsed(tick) &&
                fishState.castTimer.GetElapsedSeconds(tick, tps) <
                    AutoFishingState.EffectiveCastingTime)
            {
                input.SetButtonState(CommandInputButtonStateNames.SecondInteract_HeldDown, true);
                pressing = true;
            }
            else if (state.ReelTimer.isRunning)
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
            else if (fishState.fishIsNibbling && !fishState.isFishingAtOctopusBoss)
            {
                StartReel(ref state, ref input, tick, tps);
                pressing = true;
            }

            _pressedLastFrame = pressing;

            inputData = UnsafeUtility.As<ClientInput, ClientInputData>(ref input);
            EntityManager.SetComponentData(player, inputData);
            EntityManager.SetComponentData(player, state);
        }
    }

    /// Stops baited fishing spots from depleting.
    ///
    /// The game destroys a shoal once ObjectDataCD.amount hits 3 (half the time) or 6,
    /// and only ever bumps it from Burst jobs - so zeroing it afterwards is the only
    /// option. Cost is per read, not per shoal: the query includes disabled entities
    /// and reading it blocks on the jobs writing it, so it reads only just after a
    /// bite, plus a slow backstop.
    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class CkQolInfiniteShoalSystem : PugSimulationSystemBase
    {
        private const double PollSeconds = 0.5;

        /// The signal fires when reeling starts; the counter moves a moment later.
        private const double SweepWindowSeconds = 2.5;

        /// For hand reeling and dedicated servers, where nothing signals.
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

            // Read-only: a read-write handle bumps the chunk's change version even
            // when nothing is stored. Resets go through the EntityManager.
            _objectDataHandle = GetComponentTypeHandle<ObjectDataCD>(true);

            base.OnCreate();
            UnityEngine.Debug.Log("[CkQol/Auto Fishing] shoal system created");
        }

        protected override void OnUpdate()
        {
            Tick();
            base.OnUpdate();
        }

        private void Tick()
        {
            if (!AutoFishingState.ShoalEnabled)
            {
                // Sweep at once when it comes back on: catches made while off went
                // unseen.
                _sweepUntil = 0d;
                _nextPoll = 0d;
                _nextBackstop = 0d;
                return;
            }

            // Nothing here may touch a query or the EntityManager unless it sweeps -
            // those are the sync points being avoided.
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

            // Collected first: SetComponentData can invalidate the chunk array.
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
