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
        const float RescanDelaySeconds = 1f;

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
        LoadIssueFixer fixer;
        ValueBinding<bool> open;
        ValueBinding<bool> notice;
        ValueBinding<string> tree;
        ValueBinding<string> surfaces;
        LoadIssueReport report;
        IReadOnlyDictionary<string, Entity> prefabsById;
        readonly Dictionary<string, Entity> replacementSurfaces = new Dictionary<string, Entity>(StringComparer.Ordinal);
        string fixResult;
        float rescanAt;
        float nextSweep;
        bool changed;

        protected override void OnCreate()
        {
            base.OnCreate();
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            selectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            locations = new LocationResolver(EntityManager);
            fixer = new LoadIssueFixer(EntityManager, World.GetOrCreateSystemManaged<Game.Tools.ToolOutputBarrier>());
            AddBinding(open = new ValueBinding<bool>(Group, "open", false));
            AddBinding(notice = new ValueBinding<bool>(Group, "notice", false));
            AddBinding(tree = new ValueBinding<string>(Group, "tree", "{}"));
            AddBinding(surfaces = new ValueBinding<string>(Group, "surfaces", "[]"));
            AddBinding(new TriggerBinding<bool>(Group, "setOpen", SetOpen));
            AddBinding(new TriggerBinding(Group, "dismissNotice", () => notice.Update(false)));
            AddBinding(new TriggerBinding(Group, "rescan", Rescan));
            AddBinding(new TriggerBinding<string, int>(Group, "step", Step));
            AddBinding(new TriggerBinding<int>(Group, "stepNode", StepNode));
            AddBinding(new TriggerBinding(Group, "changeBrands", ChangeBrands));
            AddBinding(new TriggerBinding(Group, "removeSurfaces", RemoveSurfaces));
            AddBinding(new TriggerBinding<string>(Group, "replaceSurfaces", ReplaceSurfaces));
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
                notice.Update(report != null && LoadIssueTree.Build(report, origins).Total > 0);
            }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (rescanAt > 0 && UnityEngine.Time.realtimeSinceStartup >= rescanAt)
            {
                rescanAt = 0;
                Scan(false);
            }

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
            fixResult = null;
            rescanAt = 0;
            replacementSurfaces.Clear();
            ResetSweep();
            open.Update(false);
            notice.Update(false);
            tree.Update("{}");
            surfaces.Update("[]");
        }

        void SetOpen(bool value)
        {
            open.Update(value);
            if (value)
            {
                notice.Update(false);
            }

            nextSweep = 0;
        }

        void Rescan() => Scan(false);

        void ChangeBrands() => ApplyFix(() =>
        {
            var (changedCount, cleared) = fixer.ReassignBrands();
            return $"Changed the brand of {changedCount} companies, cleared {cleared} without an available brand.";
        });

        void RemoveSurfaces() => ApplyFix(() =>
        {
            var (removed, kept) = fixer.RemoveSurfaces();
            return $"Removed {removed} surfaces, kept {kept} that belong to a building or lot.";
        });

        void ReplaceSurfaces(string id) => ApplyFix(() =>
            replacementSurfaces.TryGetValue(id, out var replacement) && EntityManager.Exists(replacement)
                ? $"Replaced {fixer.ReplaceSurfaces(replacement)} surfaces with {PrefabIds.Name(id)}."
                : "That surface is no longer available.");

        void ApplyFix(Func<string> fix)
        {
            if (report == null || rescanAt > 0)
            {
                return;
            }

            try
            {
                fixResult = fix();
                Mod.Log.Info(fixResult);
                rescanAt = UnityEngine.Time.realtimeSinceStartup + RescanDelaySeconds;
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                fixResult = "The fix failed. See asset-usage.log.";
            }

            Publish();
        }

        void PublishSurfaces()
        {
            replacementSurfaces.Clear();
            using var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<SurfaceData>(), ComponentType.ReadOnly<PrefabData>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.TempJob);
            foreach (var entity in entities)
            {
                if (prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab))
                {
                    replacementSurfaces[prefab.GetPrefabID().ToString()] = entity;
                }
            }

            var options = replacementSurfaces.Keys
                .Select(id => new { id, name = PrefabIds.Name(id) })
                .OrderBy(o => o.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            surfaces.Update(JsonConvert.SerializeObject(options));
        }

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
                foreach (string line in logDetails ? report.Lines(elapsed).Concat(scanner.UnlocatedSamples) : new[] { report.Summary(elapsed) })
                {
                    Mod.Log.Info(line);
                }

                PublishSurfaces();
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

        void Publish()
        {
            var built = LoadIssueTree.Build(report, origins);
            built.Result = fixResult;
            tree.Update(JsonConvert.SerializeObject(built, JsonSettings));
        }

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
