using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace CkQol.Features
{
    /// Counts the damage the player deals, from the damage numbers the client draws.
    ///
    /// PlayLocalEffectEventSystem takes each replicated GhostEffectEventBuffer entry,
    /// de-duplicates it against the client's own predicted copy within two ticks, plays
    /// it and records it in LocalEffectEventBuffer. Reading that buffer gives the
    /// numbers the client actually showed, already de-duplicated - and every one of them
    /// carries the attacker in EffectEventCD.entity2, which only the damage-number UI
    /// throws away (EffectEventExtensions.cs:224).
    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(PlayLocalEffectEventSystem))]
    public partial class CkQolDpsSystem : SystemBase
    {
        /// An attacker that does not lead back to the player.
        private const int NotOurs = int.MinValue;

        /// Attacker to player is one hop for a minion and two for its projectile. The
        /// cap only stops a malformed chain looping.
        private const int MaxOwnerHops = 4;

        private EntityQuery _playerQuery;
        private EntityQuery _eventQuery;

        /// Everything up to here has been counted. Entries land in tick order and a
        /// snapshot's worth arrives in one frame, so one cursor covers every entity and
        /// cannot leak state as they churn.
        private NetworkTick _cursor;
        private bool _seeded;

        private readonly Dictionary<ObjectID, ObjectID> _weaponForMinion =
            new Dictionary<ObjectID, ObjectID>();

        private readonly Dictionary<ObjectID, bool> _hasIcon = new Dictionary<ObjectID, bool>();

        protected override void OnCreate()
        {
            _playerQuery = GetEntityQuery(
                ComponentType.ReadOnly<PlayerGhost>(),
                ComponentType.ReadOnly<EquippedObjectCD>(),
                ComponentType.ReadOnly<GhostOwnerIsLocal>());

            _eventQuery = GetEntityQuery(ComponentType.ReadOnly<LocalEffectEventBuffer>());

            // Only entities that were written to this frame can hold anything new.
            _eventQuery.SetChangedVersionFilter(ComponentType.ReadOnly<LocalEffectEventBuffer>());

            RequireForUpdate(_playerQuery);
            base.OnCreate();
            Debug.Log("[CkQol/DPS Tracker] tracking system created");
        }

        protected override void OnUpdate()
        {
            if (!DpsState.Enabled)
            {
                if (_seeded) DpsMeter.Clear();
                _seeded = false;
                return;
            }

            double now = UnityEngine.Time.timeAsDouble;
            DpsMeter.Prune(now);

            Entity player = _playerQuery.GetSingletonEntity();
            NetworkTick newest = _cursor;

            var entities = _eventQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var buffer = EntityManager.GetBuffer<LocalEffectEventBuffer>(entities[i], true);

                for (int j = 0; j < buffer.Length; j++)
                {
                    NetworkTick tick = buffer[j].Tick;
                    if (!tick.IsValid) continue;
                    if (_cursor.IsValid && !tick.IsNewerThan(_cursor)) continue;

                    if (!newest.IsValid || tick.IsNewerThan(newest)) newest = tick;

                    // The first pass only sets the cursor: joining a world should not
                    // count the backlog already sitting in the rings.
                    if (_seeded) Consider(player, buffer[j].value, now);
                }
            }
            entities.Dispose();

            _cursor = newest;
            _seeded = true;
        }

        private void Consider(Entity player, EffectEventCD e, double now)
        {
            switch (e.effectID)
            {
                case EffectID.WhiteDamageNumber:
                case EffectID.CritNumber:
                    break;

                // Burning, and acid: the condition systems emit a damage number with no
                // attacker at all (DealDamageFromConditionSystem.cs:193, :1782).
                case EffectID.FireDamage:
                    RecordDot(e, DpsMeter.BurningSource, now);
                    return;

                case EffectID.RedDamageNumber:
                    if (e.entity2 == Entity.Null) RecordDot(e, DpsMeter.OtherDotSource, now);
                    return;

                default:
                    return;
            }

            if (e.value1 <= 0) return;

            int source = SourceOf(player, e.entity2);
            if (source == NotOurs) return;

            DpsMeter.Record(source, e.value1, now);
        }

        private void RecordDot(EffectEventCD e, int source, double now)
        {
            if (!DpsState.CountDamageOverTime || e.value1 <= 0) return;
            if (!EntityManager.Exists(e.entity)) return;

            // The player burning is not the player dealing damage.
            if (EntityManager.HasComponent<PlayerGhost>(e.entity)) return;

            DpsMeter.Record(source, e.value1, now);
        }

        /// Walks the attacker up its owner chain the way the server does
        /// (EntityUtility.GetOwnerInfo, :1517-1540) and names what it finds. A minion or
        /// pet anywhere in the chain wins over the projectile it fired.
        private int SourceOf(Entity player, Entity attacker)
        {
            if (attacker == Entity.Null) return NotOurs;

            Entity origin = attacker;
            Entity at = attacker;

            for (int hop = 0; hop < MaxOwnerHops; hop++)
            {
                if (at == Entity.Null || !EntityManager.Exists(at)) return NotOurs;
                if (at == player) break;

                if (EntityManager.HasComponent<MinionCD>(at) ||
                    EntityManager.HasComponent<PetCD>(at))
                {
                    origin = at;
                }

                at = EntityManager.HasComponent<OwnerReferenceCD>(at)
                    ? EntityManager.GetComponentData<OwnerReferenceCD>(at).owner
                    : Entity.Null;
            }

            return at == player ? NameOf(player, origin) : NotOurs;
        }

        private int NameOf(Entity player, Entity origin)
        {
            if (origin != player && EntityManager.HasComponent<ObjectDataCD>(origin))
            {
                ObjectID id = EntityManager.GetComponentData<ObjectDataCD>(origin).objectID;

                if (EntityManager.HasComponent<MinionCD>(origin) ||
                    EntityManager.HasComponent<PetCD>(origin))
                {
                    return (int)WeaponFor(player, id);
                }

                // A projectile or explosion is worth a row of its own, but only when it
                // has an icon to put on it.
                if (HasIcon(id)) return (int)id;
            }

            return (int)Equipped(player);
        }

        private ObjectID Equipped(Entity player)
        {
            if (!EntityManager.HasComponent<EquippedObjectCD>(player)) return ObjectID.None;

            return EntityManager.GetComponentData<EquippedObjectCD>(player)
                                .containedObject.objectData.objectID;
        }

        /// The weapon that summons a minion, so the row shows a staff rather than the
        /// minion - creature prefabs often carry no icon. Cached, misses included: this
        /// runs on every minion hit.
        private ObjectID WeaponFor(Entity player, ObjectID minion)
        {
            if (_weaponForMinion.TryGetValue(minion, out var known)) return known;

            ObjectID found = minion;

            var contained = EntityManager.GetBuffer<ContainedObjectsBuffer>(player, true);
            var inventories = EntityManager.GetBuffer<InventoryBuffer>(player, true);

            for (int inv = 0; inv < inventories.Length && found == minion; inv++)
            {
                int last = PlayerSlots.End(inventories[inv], contained.Length);

                for (int i = inventories[inv].startIndex; i < last; i++)
                {
                    var data = contained[i].objectData;
                    if (data.objectID == ObjectID.None || data.amount <= 0) continue;

                    // Guarded: PugDatabase.GetComponent throws on a prefab without the
                    // component, the same trap as GetBuffer (PugDatabase.cs:545).
                    if (!PugDatabase.HasComponent<SecondaryUseCD>(data)) continue;

                    var use = PugDatabase.GetComponent<SecondaryUseCD>(data);
                    if (!use.summonsMinion || use.minionToSpawn != minion) continue;

                    found = data.objectID;
                    break;
                }
            }

            _weaponForMinion[minion] = found;
            return found;
        }

        private bool HasIcon(ObjectID id)
        {
            if (_hasIcon.TryGetValue(id, out bool known)) return known;

            var info = PugDatabase.GetObjectInfo(id);
            bool has = info != null && (info.smallIcon != null || info.icon != null);

            _hasIcon[id] = has;
            return has;
        }
    }
}
