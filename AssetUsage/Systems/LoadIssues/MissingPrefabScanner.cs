using System;
using System.Collections.Generic;
using AssetUsage.Core.LoadIssues;
using Game.Common;
using Game.Companies;
using Game.Objects;
using Game.Policies;
using Game.Prefabs;
using Game.Routes;
using Game.Simulation;
using Game.Tools;
using Game.Triggers;
using Unity.Collections;
using Unity.Entities;
using ChirpData = Game.Triggers.Chirp;
using EditorContainerData = Game.Tools.EditorContainer;
using SubReplacementData = Game.Net.SubReplacement;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class MissingPrefabScanner
    {
        readonly EntityManager entityManager;
        readonly PrefabSystem prefabSystem;
        readonly LoadIssueReport report;
        readonly Dictionary<Entity, string> missing = new Dictionary<Entity, string>();

        public MissingPrefabScanner(EntityManager entityManager, PrefabSystem prefabSystem, LoadIssueReport report)
        {
            this.entityManager = entityManager;
            this.prefabSystem = prefabSystem;
            this.report = report;
        }

        public void Scan()
        {
            CollectMissing();
            if (missing.Count == 0)
            {
                return;
            }

            ScanDirectReferences();
            ScanDependencies();
        }

        void CollectMissing()
        {
            using var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabData>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var data = query.ToComponentDataArray<PrefabData>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (data[i].m_Index < 0)
                {
                    string id = prefabSystem.GetObsoleteID(data[i]).ToString();
                    missing[entities[i]] = id;
                    report.AddMissing(id);
                }
            }
        }

        void ScanDirectReferences()
        {
            ScanComponent<PrefabRef>("PrefabRef", c => c.m_Prefab);
            ScanComponent<UnderConstruction>("UnderConstruction", c => c.m_NewPrefab);
            ScanComponent<CompanyData>("Company", c => c.m_Brand);
            ScanComponent<EditorContainerData>("EditorContainer", c => c.m_Prefab);
            ScanComponent<ChirpData>("Chirp", c => c.m_Sender);
            ScanBuffer<Policy>("Policy", c => c.m_Policy);
            ScanBuffer<ServiceBudgetData>("ServiceBudget", c => c.m_Service);
            ScanBuffer<VehicleModel>("VehicleModel", c => c.m_PrimaryPrefab, c => c.m_SecondaryPrefab);
            ScanBuffer<ChirpEntity>("ChirpEntity", c => c.m_Entity);
            ScanBuffer<SubReplacementData>("SubReplacement", c => c.m_Prefab);
        }

        void ScanDependencies()
        {
            ScanDependency<SpawnableBuildingData>(c => c.m_ZonePrefab);
            ScanDependency<PlaceholderBuildingData>(c => c.m_ZonePrefab);
            ScanDependency<ServiceObjectData>(c => c.m_Service);
            ScanDependency<NetLaneData>(c => c.m_PathfindPrefab);
            ScanDependency<TransportLineData>(c => c.m_PathfindPrefab);
            ScanDependency<ContentPrerequisiteData>(c => c.m_ContentPrerequisite);
            ScanDependency<ModPrerequisiteData>(c => c.m_ContentPrerequisite);
        }

        void ScanComponent<T>(string kind, Func<T, Entity> target)
            where T : unmanaged, IComponentData
        {
            using var query = PlacedQuery(ComponentType.ReadOnly<T>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var values = query.ToComponentDataArray<T>(Allocator.TempJob);
            var last = Entity.Null;
            string lastId = null;
            for (int i = 0; i < entities.Length; i++)
            {
                var prefab = target(values[i]);
                if (prefab != last)
                {
                    last = prefab;
                    missing.TryGetValue(prefab, out lastId);
                }

                if (lastId != null)
                {
                    report.AddReference(lastId, kind, Label(entities[i]));
                }
            }
        }

        void ScanBuffer<T>(string kind, params Func<T, Entity>[] targets)
            where T : unmanaged, IBufferElementData
        {
            using var query = PlacedQuery(ComponentType.ReadOnly<T>());
            using var entities = query.ToEntityArray(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                var buffer = entityManager.GetBuffer<T>(entities[i], true);
                for (int j = 0; j < buffer.Length; j++)
                {
                    foreach (var target in targets)
                    {
                        if (missing.TryGetValue(target(buffer[j]), out string id))
                        {
                            report.AddReference(id, kind, Label(entities[i]));
                        }
                    }
                }
            }
        }

        void ScanDependency<T>(Func<T, Entity> target)
            where T : unmanaged, IComponentData
        {
            using var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<T>() },
                Options = EntityQueryOptions.IgnoreComponentEnabledState,
            });
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var values = query.ToComponentDataArray<T>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (missing.TryGetValue(target(values[i]), out string id))
                {
                    report.AddDependency(id, SourceName(entities[i]));
                }
            }
        }

        EntityQuery PlacedQuery(ComponentType component) => entityManager.CreateEntityQuery(new EntityQueryDesc
        {
            All = new[] { component },
            None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabData>() },
        });

        string SourceName(Entity entity) => missing.TryGetValue(entity, out string id) ? id : prefabSystem.GetPrefabName(entity);

        static string Label(Entity entity) => $"{entity.Index}:{entity.Version}";
    }
}
