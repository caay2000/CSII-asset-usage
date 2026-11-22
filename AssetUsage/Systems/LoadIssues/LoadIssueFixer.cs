using System;
using System.Collections.Generic;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaExtractor = Game.Areas.Extractor;
using AreaStorage = Game.Areas.Storage;
using AreaSurface = Game.Areas.Surface;
using AreaTerrain = Game.Areas.Terrain;
using Random = Unity.Mathematics.Random;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class LoadIssueFixer
    {
        const int MaxOwnerDepth = 8;

        static readonly ComponentType[] SimulationAreaTypes =
        {
            ComponentType.ReadWrite<AreaStorage>(),
            ComponentType.ReadWrite<AreaExtractor>(),
            ComponentType.ReadWrite<AreaTerrain>(),
        };

        static readonly ComponentType[] SpecialAreaTypes =
        {
            ComponentType.ReadWrite<AreaStorage>(),
            ComponentType.ReadWrite<AreaExtractor>(),
            ComponentType.ReadWrite<AreaTerrain>(),
            ComponentType.ReadWrite<Game.Areas.Clip>(),
            ComponentType.ReadWrite<Game.Areas.HangaroundLocation>(),
            ComponentType.ReadWrite<Game.Areas.SubArea>(),
            ComponentType.ReadWrite<Game.Net.SubLane>(),
            ComponentType.ReadWrite<Game.Objects.SubObject>(),
        };

        readonly EntityManager entityManager;
        readonly EndFrameBarrier barrier;
        readonly EntityArchetype rentersUpdated;
        readonly BulldozerRecordCleaner bulldozerRecords;

        public LoadIssueFixer(EntityManager entityManager, EndFrameBarrier barrier)
        {
            this.entityManager = entityManager;
            this.barrier = barrier;
            rentersUpdated = entityManager.CreateArchetype(ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<RentersUpdated>());
            bulldozerRecords = new BulldozerRecordCleaner(entityManager);
        }

        public (int Changed, int Skipped) ReassignBrands()
        {
            var candidates = new Dictionary<Entity, List<Entity>>();
            var updates = new List<(Entity Company, CompanyData Data)>();
            var properties = new HashSet<Entity>();
            int changed = 0;
            int skipped = 0;
            using (var query = PlacedQuery(ComponentType.ReadOnly<CompanyData>(), ComponentType.ReadOnly<PrefabRef>()))
            using (var entities = query.ToEntityArray(Allocator.TempJob))
            using (var companies = query.ToComponentDataArray<CompanyData>(Allocator.TempJob))
            using (var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var company = companies[i];
                    if (!IsMissing(company.m_Brand))
                    {
                        continue;
                    }

                    var brands = BrandsOf(prefabs[i].m_Prefab, candidates);
                    if (brands.Count == 0)
                    {
                        skipped++;
                        continue;
                    }

                    var random = Random.CreateFromIndex((uint)entities[i].Index);
                    company.m_Brand = brands[random.NextInt(brands.Count)];
                    changed++;
                    updates.Add((entities[i], company));
                    CollectProperty(entities[i], properties);
                }
            }

            var commands = barrier.CreateCommandBuffer();
            foreach (var (company, data) in updates)
            {
                commands.SetComponent(company, data);
            }

            foreach (var property in properties)
            {
                var notice = commands.CreateEntity(rentersUpdated);
                commands.SetComponent(notice, new RentersUpdated(property));
            }

            return (changed, skipped);
        }

        public (int Removed, int Kept) RemoveSurfaces(Entity missingSurface)
        {
            var (targets, kept) = CollectMissingSurfaces(missingSurface, SimulationAreaTypes);
            var commands = barrier.CreateCommandBuffer();
            foreach (var surface in targets)
            {
                commands.AddComponent<Deleted>(surface);
            }

            return (targets.Count, kept);
        }

        public (int Removed, int Kept, int Records) RemoveObjects()
        {
            var targets = new HashSet<Entity>();
            int kept = 0;
            using (var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Game.Objects.Object>(),
                    ComponentType.ReadOnly<Game.Objects.Static>(),
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                },
                None = new[]
                {
                    ComponentType.ReadOnly<Temp>(),
                    ComponentType.ReadOnly<Deleted>(),
                    ComponentType.ReadOnly<PrefabData>(),
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<Game.Objects.NetObject>(),
                    ComponentType.ReadOnly<Game.Objects.Pillar>(),
                    ComponentType.ReadOnly<Game.Objects.UtilityObject>(),
                    ComponentType.ReadOnly<Game.Objects.OutsideConnection>(),
                    ComponentType.ReadOnly<Game.Objects.Placeholder>(),
                },
            }))
            using (var entities = query.ToEntityArray(Allocator.TempJob))
            using (var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!IsMissing(prefabs[i].m_Prefab))
                    {
                        continue;
                    }

                    if (IsStandaloneOrInBuilding(entities[i]))
                    {
                        targets.Add(entities[i]);
                    }
                    else
                    {
                        kept++;
                    }
                }
            }

            int removed = targets.Count;
            AddOwnedChildren(targets);
            var commands = barrier.CreateCommandBuffer();
            int records = bulldozerRecords.Remove(commands, IsMissing);
            foreach (var target in targets)
            {
                commands.AddComponent<Deleted>(target);
            }

            return (removed, kept, records);
        }

        void AddOwnedChildren(HashSet<Entity> targets)
        {
            var open = new Stack<Entity>(targets);
            while (open.Count > 0)
            {
                var parent = open.Pop();
                if (!entityManager.HasBuffer<Game.Objects.SubObject>(parent))
                {
                    continue;
                }

                var children = entityManager.GetBuffer<Game.Objects.SubObject>(parent, true);
                for (int i = 0; i < children.Length; i++)
                {
                    var child = children[i].m_SubObject;
                    if (IsOwnedChild(child, parent) && targets.Add(child))
                    {
                        open.Push(child);
                    }
                }
            }
        }

        bool IsOwnedChild(Entity child, Entity parent) =>
            entityManager.Exists(child)
            && entityManager.HasComponent<Owner>(child)
            && entityManager.GetComponentData<Owner>(child).m_Owner == parent
            && !entityManager.HasComponent<Deleted>(child)
            && !entityManager.HasComponent<Game.Objects.Secondary>(child)
            && !entityManager.HasComponent<Building>(child)
            && !entityManager.HasComponent<Game.Buildings.ServiceUpgrade>(child);

        bool IsStandaloneOrInBuilding(Entity entity)
        {
            var current = entity;
            for (int depth = 0; depth < MaxOwnerDepth; depth++)
            {
                if (!entityManager.HasComponent<Owner>(current))
                {
                    return depth == 0;
                }

                current = entityManager.GetComponentData<Owner>(current).m_Owner;
                if (current == Entity.Null || !entityManager.Exists(current))
                {
                    return false;
                }

                if (entityManager.HasComponent<Building>(current))
                {
                    return true;
                }
            }

            return false;
        }

        public (int Replaced, int Kept) ReplaceSurfaces(Entity missingSurface, Entity replacement)
        {
            var (targets, kept) = CollectMissingSurfaces(missingSurface, SpecialAreaTypes);
            var commands = barrier.CreateCommandBuffer();
            foreach (var surface in targets)
            {
                commands.SetComponent(surface, new PrefabRef(replacement));
                commands.AddComponent<Updated>(surface);
            }

            return (targets.Count, kept);
        }

        public bool IsPlainSurface(Entity prefab)
        {
            if (!IsLoaded(prefab) || !entityManager.HasComponent<AreaData>(prefab))
            {
                return false;
            }

            using var types = entityManager.GetComponentData<AreaData>(prefab).m_Archetype.GetComponentTypes(Allocator.Temp);
            foreach (var type in types)
            {
                if (Array.Exists(SpecialAreaTypes, special => special.TypeIndex == type.TypeIndex))
                {
                    return false;
                }
            }

            return true;
        }

        (List<Entity> Targets, int Kept) CollectMissingSurfaces(Entity missingSurface, ComponentType[] excluded)
        {
            var targets = new List<Entity>();
            int kept = 0;
            using var query = PlacedQuery(ComponentType.ReadOnly<AreaSurface>(), ComponentType.ReadOnly<PrefabRef>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (prefabs[i].m_Prefab != missingSurface || !IsMissing(missingSurface))
                {
                    continue;
                }

                var surface = entities[i];
                if (!Array.Exists(excluded, type => entityManager.HasComponent(surface, type)))
                {
                    targets.Add(entities[i]);
                }
                else
                {
                    kept++;
                }
            }

            return (targets, kept);
        }

        List<Entity> BrandsOf(Entity companyPrefab, Dictionary<Entity, List<Entity>> cache)
        {
            if (cache.TryGetValue(companyPrefab, out var brands))
            {
                return brands;
            }

            brands = new List<Entity>();
            if (entityManager.HasBuffer<CompanyBrandElement>(companyPrefab))
            {
                var elements = entityManager.GetBuffer<CompanyBrandElement>(companyPrefab, true);
                for (int i = 0; i < elements.Length; i++)
                {
                    if (IsLoaded(elements[i].m_Brand))
                    {
                        brands.Add(elements[i].m_Brand);
                    }
                }
            }

            cache[companyPrefab] = brands;
            return brands;
        }

        void CollectProperty(Entity company, HashSet<Entity> properties)
        {
            if (!entityManager.HasComponent<PropertyRenter>(company))
            {
                return;
            }

            var property = entityManager.GetComponentData<PropertyRenter>(company).m_Property;
            if (property != Entity.Null && entityManager.Exists(property))
            {
                properties.Add(property);
            }
        }

        bool IsMissing(Entity prefab) =>
            prefab != Entity.Null && entityManager.HasComponent<PrefabData>(prefab) && entityManager.GetComponentData<PrefabData>(prefab).m_Index < 0;

        bool IsLoaded(Entity prefab) =>
            prefab != Entity.Null && entityManager.HasComponent<PrefabData>(prefab) && entityManager.GetComponentData<PrefabData>(prefab).m_Index >= 0;

        EntityQuery PlacedQuery(params ComponentType[] components) => entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = components,
            None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabData>() },
        });
    }
}
