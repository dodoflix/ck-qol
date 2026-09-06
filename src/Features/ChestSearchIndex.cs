using System.Collections.Generic;
using System.Text;
using PugMod;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;

namespace CkQol.Features
{
    /// One searchable item: the id, and the name as the player sees it.
    internal struct ItemEntry
    {
        internal ObjectID Id;
        internal string Name;
    }

    /// One container holding what was searched for.
    internal struct ContainerHit
    {
        internal Entity Entity;
        internal float3 Position;
        internal int Count;
        internal ObjectID ContainerId;
        internal string Label;
    }

    /// The item name index, and the scan that answers "who has this".
    ///
    /// Plain statics rather than an ECS system: nothing here writes player input,
    /// and the work only happens when the player asks for it.
    internal static class ChestSearchIndex
    {
        private static readonly List<ItemEntry> _items = new List<ItemEntry>();
        private static bool _built;

        private static EntityQuery _containers;
        private static World _queryWorld;

        /// Dropped on world change: the query belongs to a world, and the item
        /// names are only resolvable once a database exists.
        internal static void Forget()
        {
            _items.Clear();
            _built = false;
            _queryWorld = null;
        }

        /// Built on the first search, not at load: PugDatabase is empty until a
        /// world converts.
        private static bool Build()
        {
            if (_built) return true;
            if (PugDatabase.objectsByType == null || PugDatabase.objectsByType.Count == 0) return false;

            var seen = new HashSet<ObjectID>();
            foreach (var info in PugDatabase.objectsByType.Values)
            {
                if (info == null || info.variation != 0) continue;
                if (info.objectType == ObjectType.NonUsable ||
                    info.objectType == ObjectType.NonObtainable) continue;
                if (info.smallIcon == null && info.icon == null) continue;
                if (!seen.Add(info.objectID)) continue;

                string name = NameOf(info.objectID);
                if (name == null) continue;

                _items.Add(new ItemEntry { Id = info.objectID, Name = name });
            }

            _built = true;
            Debug.Log($"[CkQol/Chest Search] indexed {_items.Count} item name(s)");
            return true;
        }

        /// The player-facing name, or null for anything without one.
        ///
        /// Routed through PugText rather than I2.Loc directly, which is not among
        /// the assemblies the mod compiles against. A missing term comes back as
        /// "missing: <term>" rather than null, so that is the test.
        internal static string NameOf(ObjectID id)
        {
            if (!API.Authoring.ObjectProperties.TryGetPropertyString(id, "name", out string term))
            {
                return null;
            }

            string name = PugText.ProcessText("Items/" + term, new string[0],
                                              shouldLocalize: true,
                                              shouldLocalizeFormatFields: false);

            if (string.IsNullOrEmpty(name) || name.StartsWith("missing:")) return null;
            return name;
        }

        /// Items whose name contains the query, the ones that start with it first.
        internal static void Match(string query, List<ItemEntry> into, int limit)
        {
            into.Clear();
            if (!Build() || string.IsNullOrEmpty(query)) return;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < _items.Count && into.Count < limit; i++)
                {
                    int at = _items[i].Name.IndexOf(query, System.StringComparison.OrdinalIgnoreCase);
                    bool starts = at == 0;

                    if (pass == 0 ? !starts : at <= 0) continue;
                    into.Add(_items[i]);
                }
            }
        }

        /// Containers within range holding the item, nearest first.
        ///
        /// A plain entity query rather than the physics helper the game's own quick
        /// stack uses (InventoryUtility.cs:3374): that one matches on
        /// InventoryAutoTransferEnabledCD, which marks chests but not every crafting
        /// station. Ghost relevancy already bounds the client's world to the
        /// player's surroundings, so the query is small either way.
        internal static void Find(ObjectID wanted, float radius, bool includeSelf,
                                  List<ContainerHit> into)
        {
            into.Clear();

            var player = Manager.main != null ? Manager.main.player : null;
            if (player == null || !player.entityExist) return;

            var world = player.world;
            if (world == null || !world.IsCreated) return;

            var em = world.EntityManager;
            float3 here = em.GetComponentData<LocalTransform>(player.entity).Position;

            if (includeSelf)
            {
                int mine = CountIn(em, player.entity, wanted);
                if (mine > 0)
                {
                    into.Add(new ContainerHit
                    {
                        Position = here,
                        Count = mine,
                        ContainerId = ObjectID.None,
                    });
                }
            }

            var entities = Containers(world).ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                float3 at = em.GetComponentData<LocalTransform>(entities[i]).Position;
                if (math.distance(here.xz, at.xz) > radius) continue;

                int count = CountIn(em, entities[i], wanted);
                if (count <= 0) continue;

                into.Add(new ContainerHit
                {
                    Entity = entities[i],
                    Position = at,
                    Count = count,
                    ContainerId = em.HasComponent<ObjectDataCD>(entities[i])
                        ? em.GetComponentData<ObjectDataCD>(entities[i]).objectID
                        : ObjectID.None,
                    Label = LabelOf(em, entities[i]),
                });
            }
            entities.Dispose();

            into.Sort((a, b) => math.distance(here.xz, a.Position.xz)
                        .CompareTo(math.distance(here.xz, b.Position.xz)));
        }

        /// InventoryBuffer as well as the contents: it is what separates a container
        /// from anything else that happens to carry an object, such as an item lying
        /// on the ground.
        private static EntityQuery Containers(World world)
        {
            if (_queryWorld == world) return _containers;

            _containers = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<ContainedObjectsBuffer>(),
                    ComponentType.ReadOnly<InventoryBuffer>(),
                    ComponentType.ReadOnly<LocalTransform>()
                },
                None = new[] { ComponentType.ReadOnly<PlayerGhost>() }
            });
            _queryWorld = world;
            return _containers;
        }

        /// The whole buffer, not a walk of the InventoryBuffer ranges: those are
        /// windows over these same slots, so counting per range would double up the
        /// player's hotbar.
        private static int CountIn(EntityManager em, Entity container, ObjectID wanted)
        {
            if (!em.HasBuffer<ContainedObjectsBuffer>(container)) return 0;

            var contained = em.GetBuffer<ContainedObjectsBuffer>(container, true);

            int total = 0;
            for (int i = 0; i < contained.Length; i++)
            {
                if (contained[i].objectData.objectID != wanted) continue;
                total += contained[i].objectData.amount;
            }
            return total;
        }

        /// The name a player has written on a chest, or null. Stored as raw UTF-8
        /// bytes rather than a string component (WorldLabel.cs:77-95).
        private static string LabelOf(EntityManager em, Entity container)
        {
            if (!em.HasBuffer<DescriptionBuffer>(container)) return null;

            var buffer = em.GetBuffer<DescriptionBuffer>(container, true);
            if (buffer.Length == 0) return null;

            var bytes = new byte[buffer.Length];
            for (int i = 0; i < buffer.Length; i++) bytes[i] = buffer[i].Value;

            string label = Encoding.UTF8.GetString(bytes).Trim();
            return label.Length == 0 ? null : label;
        }
    }
}
