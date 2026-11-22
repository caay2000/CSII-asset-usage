using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        readonly List<MissingPrefabIssue> sweepIssues = new List<MissingPrefabIssue>();
        int sweepIssue;
        int sweepCase;
        int sweepNode;
        bool sweepFailed;
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
            if (!open.value || report == null || sweepFailed || UnityEngine.Time.realtimeSinceStartup < nextSweep)
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
                sweepFailed = true;
            }
        }

        void ResetSweep()
        {
            sweepIssues.Clear();
            if (report != null)
            {
                sweepIssues.AddRange(report.Missing);
            }

            RestartSweepPass();
            sweepFailed = false;
            changed = false;
        }

        void RestartSweepPass()
        {
            sweepIssue = 0;
            sweepCase = 0;
            sweepNode = 0;
        }

        void Clear()
        {
            report = null;
            prefabsById = null;
            ResetSweep();
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
            try
            {
                var watch = Stopwatch.StartNew();
                var scanned = new LoadIssueReport();
                var scanner = new MissingPrefabScanner(EntityManager, prefabSystem, scanned, locations);
                scanner.Scan();
                new BrokenNodeScanner(EntityManager, scanned).Scan();
                report = scanned;
                prefabsById = scanner.PrefabsById;
                ResetSweep();
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
                ResetSweep();
                tree.Update(JsonConvert.SerializeObject(new LoadIssueTree { Error = "The scan failed. See asset-usage.log." }, JsonSettings));
            }
        }

        void Publish() => tree.Update(JsonConvert.SerializeObject(LoadIssueTree.Build(report, origins), JsonSettings));

        void Step(string id, int delta)
        {
            if (report != null)
            {
                Navigate(() => report.Step(id, delta), () => report.RemoveCurrent(id, delta));
            }
        }

        void StepNode(int delta)
        {
            if (report != null)
            {
                Navigate(() => report.StepBrokenNode(delta), () => report.RemoveCurrentBrokenNode(delta));
            }
        }

        void Navigate(Func<CaseRef?> step, Action removeCurrent)
        {
            try
            {
                for (var target = step(); target.HasValue; target = step())
                {
                    if (locations.TryResolve(ToEntity(target.Value), out var location))
                    {
                        selectedInfo.SetSelection(location);
                        selectedInfo.Focus(location);
                        break;
                    }

                    removeCurrent();
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
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < SweepBudgetMilliseconds)
            {
                if (!SweepNext())
                {
                    RestartSweepPass();
                    break;
                }
            }

            if (changed)
            {
                changed = false;
                Publish();
            }
        }

        bool SweepNext()
        {
            while (sweepIssue < sweepIssues.Count)
            {
                var issue = sweepIssues[sweepIssue];
                if (sweepCase < issue.Located.Count)
                {
                    var located = issue.Located[sweepCase];
                    if (!StillReferences(ToEntity(located.Entity), located.Kind, issue.Id) && issue.Remove(located))
                    {
                        changed = true;
                    }
                    else
                    {
                        sweepCase++;
                    }

                    return true;
                }

                sweepIssue++;
                sweepCase = 0;
            }

            if (sweepNode < report.BrokenNodes.Count)
            {
                var node = report.BrokenNodes[sweepNode];
                if (IsGone(ToEntity(node)) && report.RemoveBrokenNode(node))
                {
                    changed = true;
                }
                else
                {
                    sweepNode++;
                }

                return true;
            }

            return false;
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
