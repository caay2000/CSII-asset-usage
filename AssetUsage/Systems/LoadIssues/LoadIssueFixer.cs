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

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class LoadIssueFixer
    {
        readonly EntityManager entityManager;
        readonly EndFrameBarrier barrier;
        readonly EntityArchetype rentersUpdated;

        public LoadIssueFixer(EntityManager entityManager, EndFrameBarrier barrier)
        {
            this.entityManager = entityManager;
            this.barrier = barrier;
            rentersUpdated = entityManager.CreateArchetype(ComponentType.ReadWrite<Event>(), ComponentType.ReadWrite<RentersUpdated>());
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

        public (int Removed, int Kept) RemoveSurfaces()
        {
            var (targets, kept) = CollectMissingSurfaces();
            var commands = barrier.CreateCommandBuffer();
            foreach (var surface in targets)
            {
                commands.AddComponent<Deleted>(surface);
            }

            return (targets.Count, kept);
        }

        public (int Replaced, int Kept) ReplaceSurfaces(Entity replacement)
        {
            var (targets, kept) = CollectMissingSurfaces();
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
                if (type == ComponentType.ReadWrite<AreaStorage>()
                    || type == ComponentType.ReadWrite<AreaExtractor>()
                    || type == ComponentType.ReadWrite<AreaTerrain>())
                {
                    return false;
                }
            }

            return true;
        }

        (List<Entity> Targets, int Kept) CollectMissingSurfaces()
        {
            var targets = new List<Entity>();
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

                if (IsPlainInstance(entities[i]))
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

        bool IsPlainInstance(Entity surface) =>
            !entityManager.HasComponent<AreaStorage>(surface)
            && !entityManager.HasComponent<AreaExtractor>(surface)
            && !entityManager.HasComponent<AreaTerrain>(surface);

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
