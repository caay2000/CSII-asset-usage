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
        IssueViewController views;
        BrokenNodeScanner brokenScanner;
        string activeIssue;
        CaseRef? activeCase;
        bool fixPending;
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
            fixer = new LoadIssueFixer(EntityManager, World.GetOrCreateSystemManaged<EndFrameBarrier>());
            views = new IssueViewController(EntityManager, World.GetOrCreateSystemManaged<Game.Tools.ToolSystem>(), prefabSystem, World.GetOrCreateSystemManaged<InfoviewInitializeSystem>());
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
                fixPending = false;
                Scan(false);
            }

            if (!open.value || report == null || sweepFailed || UnityEngine.Time.realtimeSinceStartup < nextSweep)
            {
                return;
            }

            nextSweep = UnityEngine.Time.realtimeSinceStartup + SweepEverySeconds;
            try
            {
                AdvanceIfResolved();
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
            activeIssue = null;
            activeCase = null;
        }

        void AdvanceIfResolved()
        {
            if (!activeCase.HasValue || fixPending || !views.PlayerIsIdle)
            {
                return;
            }

            var current = activeCase.Value;
            if (activeIssue == null)
            {
                AdvanceFromBrokenNode(current);
                return;
            }

            string id = activeIssue;
            var issue = report.Missing.FirstOrDefault(i => i.Id == id);
            if (issue == null || !issue.Located.Any(l => l.Entity.Equals(current)))
            {
                activeCase = null;
            }
            else if (!IsOpen(id, current))
            {
                string next = NextIssue(id);
                report.RemoveCurrent(id, 1);
                activeCase = null;
                if (issue.Located.Count > 0)
                {
                    Step(id, 1);
                }

                if (!activeCase.HasValue)
                {
                    GoToIssueOrNode(next);
                }
            }
        }

        void AdvanceFromBrokenNode(CaseRef current)
        {
            if (!report.BrokenNodes.Contains(current))
            {
                activeCase = null;
            }
            else if (!IsOpen(null, current))
            {
                report.RemoveCurrentBrokenNode(1);
                activeCase = null;
                GoToIssueOrNode(report.BrokenNodes.Count > 0 ? null : NextIssue(null));
            }
        }

        string NextIssue(string afterId) => LoadIssueTree.NextIssue(LoadIssueTree.Build(report, origins), afterId);

        void GoToIssueOrNode(string issueId)
        {
            int attempts = report.Missing.Count;
            for (; issueId != null && attempts > 0; attempts--)
            {
                Step(issueId, 1);
                if (activeCase.HasValue)
                {
                    return;
                }

                issueId = NextIssue(issueId);
            }

            if (report.BrokenNodes.Count > 0)
            {
                StepNode(1);
                if (activeCase.HasValue)
                {
                    return;
                }
            }

            views.Reset();
            Publish();
        }

        bool IsOpen(string issueId, CaseRef target)
        {
            var entity = ToEntity(target);
            if (issueId == null)
            {
                return !IsGone(entity) && brokenScanner.StillBroken(entity);
            }

            var located = report.Missing.FirstOrDefault(i => i.Id == issueId)?.Located.FirstOrDefault(l => l.Entity.Equals(target));
            return located != null && StillReferences(entity, located.Kind, issueId);
        }

        bool IsActive(string issueId, CaseRef target) =>
            activeCase.HasValue && activeCase.Value.Equals(target) && activeIssue == issueId;

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
            brokenScanner = null;
            fixResult = null;
            fixPending = false;
            rescanAt = 0;
            replacementSurfaces.Clear();
            ResetSweep();
            views.Reset();
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
            else
            {
                activeCase = null;
                views.Reset();
            }

            nextSweep = 0;
        }

        void Rescan()
        {
            if (!fixPending)
            {
                fixResult = null;
                Scan(false);
            }
        }

        void ChangeBrands() => ApplyFix(() =>
        {
            var (changedCount, skipped) = fixer.ReassignBrands();
            return $"Changed the brand of {changedCount} companies, left {skipped} unchanged because no compatible brand is loaded.";
        });

        void RemoveSurfaces() => ApplyFix(() =>
        {
            var (removed, kept) = fixer.RemoveSurfaces();
            return $"Removed {removed} surfaces, kept {kept} storage, extractor or terrain areas.";
        });

        void ReplaceSurfaces(string id) => ApplyFix(() =>
        {
            if (!replacementSurfaces.TryGetValue(id, out var replacement) || !fixer.IsPlainSurface(replacement))
            {
                return "That surface is no longer available.";
            }

            var (replaced, kept) = fixer.ReplaceSurfaces(replacement);
            return $"Replaced {replaced} surfaces with {PrefabIds.Name(id)}, kept {kept} special areas.";
        });

        void ApplyFix(Func<string> fix)
        {
            if (report == null || fixPending)
            {
                return;
            }

            try
            {
                fixResult = fix();
                Mod.Log.Info(fixResult);
                fixPending = true;
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
                if (fixer.IsPlainSurface(entity) && prefabSystem.TryGetPrefab<PrefabBase>(entity, out var prefab))
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
                var nodes = new BrokenNodeScanner(EntityManager, scanned);
                nodes.Scan();
                report = scanned;
                brokenScanner = nodes;
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
                brokenScanner = null;
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
                Navigate(() => report.Step(id, delta), () => report.RemoveCurrent(id, delta), id);
            }
        }

        void StepNode(int delta)
        {
            if (report != null)
            {
                Navigate(() => report.StepBrokenNode(delta), () => report.RemoveCurrentBrokenNode(delta), null);
            }
        }

        void Navigate(Func<CaseRef?> step, Action removeCurrent, string issueId)
        {
            try
            {
                activeCase = null;
                for (var target = step(); target.HasValue; target = step())
                {
                    var entity = ToEntity(target.Value);
                    if (IsOpen(issueId, target.Value) && locations.TryResolve(entity, out var location))
                    {
                        ShowView(issueId, entity, location);
                        selectedInfo.SetSelection(location);
                        selectedInfo.Focus(location);
                        activeIssue = issueId;
                        activeCase = target;
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

        void ShowView(string issueId, Entity entity, Entity location)
        {
            try
            {
                if (issueId == null)
                {
                    views.ShowBrokenNode(entity);
                }
                else
                {
                    views.ShowCase(location);
                }
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
                    if (!IsActive(issue.Id, located.Entity) && !StillReferences(ToEntity(located.Entity), located.Kind, issue.Id) && issue.Remove(located))
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
                var nodeEntity = ToEntity(node);
                if (!IsActive(null, node) && (IsGone(nodeEntity) || !brokenScanner.StillBroken(nodeEntity)) && report.RemoveBrokenNode(node))
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
