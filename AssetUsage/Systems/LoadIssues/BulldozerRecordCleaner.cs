using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.Common;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;

namespace AssetUsage.Systems.LoadIssues
{
    public sealed class BulldozerRecordCleaner
    {
        const string RecordTypeName = "Better_Bulldozer.Components.OwnerRecord";
        const string OwnerBufferTypeName = "Better_Bulldozer.Components.PermanentlyRemovedSubElementPrefab";

        readonly EntityManager entityManager;

        public BulldozerRecordCleaner(EntityManager entityManager)
        {
            this.entityManager = entityManager;
        }

        public int Remove(EntityCommandBuffer commands, Func<Entity, bool> isMissing)
        {
            var recordType = FindType(RecordTypeName);
            var ownerBufferType = FindType(OwnerBufferTypeName);
            if (recordType == null || ownerBufferType == null)
            {
                return 0;
            }

            var records = CollectRecords(recordType, isMissing);
            if (records.Count == 0)
            {
                return 0;
            }

            typeof(BulldozerRecordCleaner)
                .GetMethod(nameof(DetachFromOwners), BindingFlags.Instance | BindingFlags.NonPublic)
                .MakeGenericMethod(ownerBufferType)
                .Invoke(this, new object[] { commands, records });
            foreach (var record in records)
            {
                commands.AddComponent<Deleted>(record);
            }

            return records.Count;
        }

        static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "BetterBulldozer")
            .Select(assembly => assembly.GetType(name, false))
            .FirstOrDefault(type => type != null);

        HashSet<Entity> CollectRecords(Type recordType, Func<Entity, bool> isMissing)
        {
            var records = new HashSet<Entity>();
            using var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly(recordType), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabData>() },
            });
            using var entities = query.ToEntityArray(Allocator.TempJob);
            using var prefabs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob);
            for (int i = 0; i < entities.Length; i++)
            {
                if (isMissing(prefabs[i].m_Prefab))
                {
                    records.Add(entities[i]);
                }
            }

            return records;
        }

        void DetachFromOwners<T>(EntityCommandBuffer commands, HashSet<Entity> records)
            where T : unmanaged, IBufferElementData
        {
            if (UnsafeUtility.SizeOf<T>() != UnsafeUtility.SizeOf<Entity>())
            {
                throw new InvalidOperationException($"Unexpected layout of {typeof(T).FullName}.");
            }

            using var query = entityManager.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<T>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
            });
            using var owners = query.ToEntityArray(Allocator.TempJob);
            foreach (var owner in owners)
            {
                var elements = entityManager.GetBuffer<T>(owner, true);
                var targets = elements.Reinterpret<Entity>();
                var kept = new List<T>(elements.Length);
                for (int i = 0; i < elements.Length; i++)
                {
                    if (!records.Contains(targets[i]))
                    {
                        kept.Add(elements[i]);
                    }
                }

                if (kept.Count == elements.Length)
                {
                    continue;
                }

                var replacement = commands.SetBuffer<T>(owner);
                foreach (var element in kept)
                {
                    replacement.Add(element);
                }
            }
        }
    }
}
