using System.Collections.Generic;
using Game.Buildings;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using AreaSurface = Game.Areas.Surface;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class LoadIssueFixer
    {
        readonly EntityManager entityManager;
        readonly ToolOutputBarrier barrier;
        readonly EntityArchetype rentersUpdated;

        public LoadIssueFixer(EntityManager entityManager, ToolOutputBarrier barrier)
        {
            this.entityManager = entityManager;
            this.barrier = barrier;
            rentersUpdated = entityManager.CreateArchetype(ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<RentersUpdated>());
        }

        public (int Changed, int Cleared) ReassignBrands()
        {
            var commands = barrier.CreateCommandBuffer();
            var candidates = new Dictionary<Entity, List<Entity>>();
            var refreshed = new HashSet<Entity>();
            int changed = 0;
            int cleared = 0;
            using var query = PlacedQuery(ComponentType.ReadOnly<CompanyData>(), ComponentType.ReadOnly<PrefabRef>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var companies = query.ToComponentDataArray<CompanyData>(Allocator.TempJob);
            using var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                var company = companies[i];
                if (!IsMissing(company.m_Brand))
                {
                    continue;
                }

                var brands = BrandsOf(prefabs[i].m_Prefab, candidates);
                if (brands.Count > 0)
                {
                    var random = Random.CreateFromIndex((uint)entities[i].Index);
                    company.m_Brand = brands[random.NextInt(brands.Count)];
                    changed++;
                }
                else
                {
                    company.m_Brand = Entity.Null;
                    cleared++;
                }

                commands.SetComponent(entities[i], company);
                RefreshProperty(commands, entities[i], refreshed);
            }

            return (changed, cleared);
        }

        public (int Removed, int Kept) RemoveSurfaces()
        {
            var commands = barrier.CreateCommandBuffer();
            int removed = 0;
            int kept = 0;
            using var query = PlacedQuery(ComponentType.ReadOnly<AreaSurface>(), ComponentType.ReadOnly<PrefabRef>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (!IsMissing(prefabs[i].m_Prefab))
                {
                    continue;
                }

                if (entityManager.HasComponent<Owner>(entities[i]))
                {
                    kept++;
                }
                else
                {
                    commands.AddComponent<Deleted>(entities[i]);
                    removed++;
                }
            }

            return (removed, kept);
        }

        public int ReplaceSurfaces(Entity replacement)
        {
            var commands = barrier.CreateCommandBuffer();
            int replaced = 0;
            using var query = PlacedQuery(ComponentType.ReadOnly<AreaSurface>(), ComponentType.ReadOnly<PrefabRef>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (IsMissing(prefabs[i].m_Prefab))
                {
                    commands.SetComponent(entities[i], new PrefabRef(replacement));
                    commands.AddComponent<Updated>(entities[i]);
                    replaced++;
                }
            }

            return replaced;
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

        void RefreshProperty(EntityCommandBuffer commands, Entity company, HashSet<Entity> refreshed)
        {
            if (!entityManager.HasComponent<PropertyRenter>(company))
            {
                return;
            }

            var property = entityManager.GetComponentData<PropertyRenter>(company).m_Property;
            if (property != Entity.Null && entityManager.Exists(property) && refreshed.Add(property))
            {
                var notice = commands.CreateEntity(rentersUpdated);
                commands.SetComponent(notice, new RentersUpdated(property));
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
