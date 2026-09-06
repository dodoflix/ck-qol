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

        /// A carried weapon that spawns this minion or projectile, and when the answer
        /// was reached. Misses are kept too - this runs on every hit - but expire, so a
        /// weapon picked back up is found again.
        private struct Weapon
        {
            public ObjectID Of;
            public double At;
        }

        private const double RescanSeconds = 5.0;

        private readonly Dictionary<ObjectID, Weapon> _weaponFor =
            new Dictionary<ObjectID, Weapon>();

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

            int source = SourceOf(player, e.entity2, now);
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
        /// (EntityUtility.GetOwnerInfo, :1517-1540), naming the row from the first thing
        /// in it that the player carries a weapon for. An explosion owned by a projectile
        /// owned by the player therefore lands on that projectile's weapon.
        ///
        /// A spawned entity's own ObjectDataCD is never the row: it names the prefab the
        /// weapon spawns, which for the Grubzooka is a mining projectile that shows up as
        /// a pickaxe. Nothing that fails to resolve is counted - a thrown bomb has no
        /// weapon behind it, and guessing at the item in hand is worse than a gap.
        private int SourceOf(Entity player, Entity attacker, double now)
        {
            if (attacker == Entity.Null) return NotOurs;

            Entity at = attacker;
            ObjectID named = ObjectID.None;
            bool direct = false;

            for (int hop = 0; hop < MaxOwnerHops; hop++)
            {
                if (at == Entity.Null || !EntityManager.Exists(at)) return NotOurs;

                if (at == player)
                {
                    direct = hop == 0;
                    break;
                }

                if (named == ObjectID.None && EntityManager.HasComponent<ObjectDataCD>(at))
                {
                    bool own = EntityManager.HasComponent<MinionCD>(at) ||
                               EntityManager.HasComponent<PetCD>(at);

                    named = WeaponFor(
                        player,
                        EntityManager.GetComponentData<ObjectDataCD>(at).objectID,
                        keepUnmatched: own,
                        now: now);
                }

                at = EntityManager.HasComponent<OwnerReferenceCD>(at)
                    ? EntityManager.GetComponentData<OwnerReferenceCD>(at).owner
                    : Entity.Null;
            }

            if (at != player) return NotOurs;
            if (named != ObjectID.None) return (int)named;

            // The player's own swing, which is whatever they are holding.
            return direct ? (int)Equipped(player) : NotOurs;
        }

        private ObjectID Equipped(Entity player)
        {
            if (!EntityManager.HasComponent<EquippedObjectCD>(player)) return ObjectID.None;

            return EntityManager.GetComponentData<EquippedObjectCD>(player)
                                .containedObject.objectData.objectID;
        }

        /// The carried weapon that spawns this minion or projectile, so a row shows the
        /// staff or the gun rather than what it put on the field.
        ///
        /// keepUnmatched falls back to the spawned thing itself, for minions: their staff
        /// may have been put away, and a missing minion row is worse than one whose icon
        /// the creature prefab does not have. A projectile with no weapon behind it is
        /// dropped instead.
        private ObjectID WeaponFor(Entity player, ObjectID spawned, bool keepUnmatched,
                                   double now)
        {
            ObjectID miss = keepUnmatched ? spawned : ObjectID.None;

            if (_weaponFor.TryGetValue(spawned, out var known) &&
                (known.Of != miss || now - known.At < RescanSeconds))
            {
                return known.Of;
            }

            ObjectID found = miss;

            var contained = EntityManager.GetBuffer<ContainedObjectsBuffer>(player, true);
            var inventories = EntityManager.GetBuffer<InventoryBuffer>(player, true);

            for (int inv = 0; inv < inventories.Length && found == miss; inv++)
            {
                int last = PlayerSlots.End(inventories[inv], contained.Length);

                for (int i = inventories[inv].startIndex; i < last; i++)
                {
                    var data = contained[i].objectData;
                    if (data.objectID == ObjectID.None || data.amount <= 0) continue;

                    if (!Spawns(data, spawned)) continue;

                    found = data.objectID;
                    break;
                }
            }

            _weaponFor[spawned] = new Weapon { Of = found, At = now };
            return found;
        }

        /// Whether an item declares this minion or projectile as its own.
        private static bool Spawns(ObjectDataCD item, ObjectID spawned)
        {
            // Guarded: PugDatabase.GetComponent throws on a prefab without the component,
            // the same trap as GetBuffer (PugDatabase.cs:545).
            if (PugDatabase.HasComponent<SecondaryUseCD>(item))
            {
                var use = PugDatabase.GetComponent<SecondaryUseCD>(item);
                if (use.summonsMinion && use.minionToSpawn == spawned) return true;
            }

            if (!PugDatabase.HasComponent<RangeWeaponCD>(item)) return false;

            var weapon = PugDatabase.GetComponent<RangeWeaponCD>(item);
            if (weapon.projectileID == spawned || weapon.windupProjectileID == spawned)
            {
                return true;
            }

            if (!weapon.spawnRandomProjectile) return false;

            for (int i = 0; i < weapon.randomProjectiles.Length; i++)
            {
                if (weapon.randomProjectiles[i] == spawned) return true;
            }
            return false;
        }
    }
}
