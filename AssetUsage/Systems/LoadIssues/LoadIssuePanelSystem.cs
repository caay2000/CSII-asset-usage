using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AssetUsage.Core.LoadIssues;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Common;
using Game.Companies;
using Game.Prefabs;
using Game.UI;
using Game.UI.InGame;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Unity.Entities;
using UnderConstruction = Game.Objects.UnderConstruction;

namespace AssetUsage.Systems.LoadIssues
{
    public partial class LoadIssuePanelSystem : UISystemBase
    {
        const string Group = "AssetUsage.LoadIssues";
        const float SweepEverySeconds = 1f;
        const double SweepBudgetMilliseconds = 2;

        static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
        };

        sealed class LedgerOrigins : IPrefabOriginLookup
        {
            public string Find(string prefabKey) => UsageSystem.Instance?.FindPack(prefabKey);
        }

        readonly IPrefabOriginLookup origins = new LedgerOrigins();
        readonly Queue<Action> sweep = new Queue<Action>();
        PrefabSystem prefabSystem;
        SelectedInfoUISystem selectedInfo;
        LocationResolver locations;
        ValueBinding<bool> open;
        ValueBinding<string> tree;
        LoadIssueReport report;
        IReadOnlyDictionary<string, Entity> prefabsById;
        float nextSweep;
        bool changed;

        protected override void OnCreate()
        {
            base.OnCreate();
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            selectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            locations = new LocationResolver(EntityManager);
            AddBinding(open = new ValueBinding<bool>(Group, "open", false));
            AddBinding(tree = new ValueBinding<string>(Group, "tree", "{}"));
            AddBinding(new TriggerBinding<bool>(Group, "setOpen", SetOpen));
            AddBinding(new TriggerBinding(Group, "rescan", Rescan));
            AddBinding(new TriggerBinding<string, int>(Group, "step", Step));
            AddBinding(new TriggerBinding<int>(Group, "stepNode", StepNode));
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            Clear();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode == GameMode.Game)
            {
                Scan(true);
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (!open.value || report == null || UnityEngine.Time.realtimeSinceStartup < nextSweep)
            {
                return;
            }

            nextSweep = UnityEngine.Time.realtimeSinceStartup + SweepEverySeconds;
            try
            {
                Sweep();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                sweep.Clear();
            }
        }

        void Clear()
        {
            report = null;
            prefabsById = null;
            sweep.Clear();
            open.Update(false);
            tree.Update("{}");
        }

        void SetOpen(bool value)
        {
            open.Update(value);
            nextSweep = 0;
        }

        void Rescan() => Scan(false);

        void Scan(bool logDetails)
        {
            sweep.Clear();
            try
            {
                var watch = Stopwatch.StartNew();
                var scanned = new LoadIssueReport();
                var scanner = new MissingPrefabScanner(EntityManager, prefabSystem, scanned, locations);
                scanner.Scan();
                new BrokenNodeScanner(EntityManager, scanned).Scan();
                report = scanned;
                prefabsById = scanner.PrefabsById;
                double elapsed = watch.Elapsed.TotalMilliseconds;
                foreach (string line in logDetails ? report.Lines(elapsed) : new[] { report.Summary(elapsed) })
                {
                    Mod.Log.Info(line);
                }

                Publish();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                report = null;
                prefabsById = null;
                tree.Update(JsonConvert.SerializeObject(new LoadIssueTree { Error = "The scan failed. See asset-usage.log." }, JsonSettings));
            }
        }

        void Publish() => tree.Update(JsonConvert.SerializeObject(LoadIssueTree.Build(report, origins), JsonSettings));

        void Step(string id, int delta)
        {
            if (report != null)
            {
                Focus(report.Step(id, delta));
            }
        }

        void StepNode(int delta)
        {
            if (report != null)
            {
                Focus(report.StepBrokenNode(delta));
            }
        }

        void Focus(CaseRef? target)
        {
            try
            {
                if (target.HasValue && locations.TryResolve(ToEntity(target.Value), out var location))
                {
                    selectedInfo.SetSelection(location);
                    selectedInfo.Focus(location);
                }

                Publish();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        void Sweep()
        {
            if (sweep.Count == 0)
            {
                QueueSweep();
            }

            var watch = Stopwatch.StartNew();
            while (sweep.Count > 0 && watch.Elapsed.TotalMilliseconds < SweepBudgetMilliseconds)
            {
                sweep.Dequeue()();
            }

            if (changed && sweep.Count == 0)
            {
                changed = false;
                Publish();
            }
        }

        void QueueSweep()
        {
            foreach (var issue in report.Missing)
            {
                foreach (var located in issue.Located.ToList())
                {
                    sweep.Enqueue(() => CheckCase(issue, located));
                }
            }

            foreach (var node in report.BrokenNodes.ToList())
            {
                sweep.Enqueue(() => CheckNode(node));
            }
        }

        void CheckCase(MissingPrefabIssue issue, LocatedCase located)
        {
            if (!StillReferences(ToEntity(located.Entity), located.Kind, issue.Id) && issue.Remove(located))
            {
                changed = true;
            }
        }

        void CheckNode(CaseRef node)
        {
            if (IsGone(ToEntity(node)) && report.RemoveBrokenNode(node))
            {
                changed = true;
            }
        }

        bool IsGone(Entity entity) => !EntityManager.Exists(entity) || EntityManager.HasComponent<Deleted>(entity);

        bool StillReferences(Entity entity, string kind, string id)
        {
            if (IsGone(entity))
            {
                return false;
            }

            if (!prefabsById.TryGetValue(id, out var prefab))
            {
                return true;
            }

            switch (kind)
            {
                case MissingPrefabScanner.PrefabRefKind:
                    return EntityManager.HasComponent<PrefabRef>(entity) && EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab == prefab;
                case MissingPrefabScanner.UnderConstructionKind:
                    return EntityManager.HasComponent<UnderConstruction>(entity) && EntityManager.GetComponentData<UnderConstruction>(entity).m_NewPrefab == prefab;
                case MissingPrefabScanner.CompanyKind:
                    return EntityManager.HasComponent<CompanyData>(entity) && EntityManager.GetComponentData<CompanyData>(entity).m_Brand == prefab;
                default:
                    return true;
            }
        }

        static Entity ToEntity(CaseRef reference) => new Entity { Index = reference.Index, Version = reference.Version };
    }
}
