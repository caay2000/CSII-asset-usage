using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetUsage.Core.LoadIssues
{
    public interface IPrefabOriginLookup
    {
        string Find(string prefabKey);
    }

    public sealed class PrefabNode
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public int Located { get; set; }

        public int Unlocated { get; set; }

        public int Cursor { get; set; }

        public List<string> KeptAliveBy { get; set; }
    }

    public sealed class ModNode
    {
        public string Name { get; set; }

        public int Count { get; set; }

        public List<PrefabNode> Prefabs { get; set; }
    }

    public sealed class BrokenKindNode
    {
        public string Name { get; set; }

        public int Count { get; set; }
    }

    public sealed class IssueGroupNode
    {
        public string Category { get; set; }

        public int Count { get; set; }

        public List<string> Fixes { get; set; }

        public List<ModNode> Mods { get; set; }
    }

    public static class LoadIssueFixes
    {
        public const string ChangeBrand = "changeBrand";
        public const string Remove = "remove";
        public const string Replace = "replace";

        public static List<string> For(LoadIssueCategory category)
        {
            switch (category)
            {
                case LoadIssueCategory.Brand:
                    return new List<string> { ChangeBrand };
                case LoadIssueCategory.Surface:
                    return new List<string> { Remove, Replace };
                default:
                    return new List<string>();
            }
        }
    }

    public sealed class LoadIssueTree
    {
        public string Error { get; set; }

        public string Result { get; set; }

        public bool Scanned { get; set; }

        public int Total { get; set; }

        public int BrokenNodes { get; set; }

        public int BrokenCursor { get; set; }

        public string BrokenCurrent { get; set; }

        public List<BrokenKindNode> BrokenKinds { get; set; } = new List<BrokenKindNode>();

        public List<IssueGroupNode> Groups { get; set; } = new List<IssueGroupNode>();

        public static LoadIssueTree Build(LoadIssueReport report, IPrefabOriginLookup origins)
        {
            var groups = report.Missing
                .Where(i => i.Category != LoadIssueCategory.Content && report.IsListed(i))
                .GroupBy(i => i.Category)
                .OrderBy(g => g.Key)
                .Select(g => BuildGroup(g, origins))
                .ToList();
            return new LoadIssueTree
            {
                Scanned = true,
                Total = groups.Sum(g => g.Count) + report.BrokenNodes.Count,
                BrokenNodes = report.BrokenNodes.Count,
                BrokenCursor = report.BrokenCursor,
                BrokenCurrent = report.BrokenCursor > 0 && report.BrokenCursor <= report.BrokenNodes.Count
                    ? report.BrokenKind(report.BrokenNodes[report.BrokenCursor - 1])
                    : null,
                BrokenKinds = report.BrokenNodes
                    .GroupBy(report.BrokenKind, StringComparer.Ordinal)
                    .Select(g => new BrokenKindNode { Name = g.Key, Count = g.Count() })
                    .OrderByDescending(k => k.Count)
                    .ThenBy(k => k.Name, StringComparer.Ordinal)
                    .ToList(),
                Groups = groups,
            };
        }

        static IssueGroupNode BuildGroup(IGrouping<LoadIssueCategory, MissingPrefabIssue> group, IPrefabOriginLookup origins)
        {
            var mods = group
                .GroupBy(i => PrefabIds.Origin(i, origins), StringComparer.Ordinal)
                .Select(BuildMod)
                .OrderByDescending(m => m.Count)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
            return new IssueGroupNode
            {
                Category = group.Key.ToString(),
                Count = mods.Sum(m => m.Count),
                Fixes = LoadIssueFixes.For(group.Key),
                Mods = mods,
            };
        }

        static ModNode BuildMod(IGrouping<string, MissingPrefabIssue> group)
        {
            var prefabs = group
                .OrderByDescending(i => i.CaseCount)
                .ThenBy(i => i.Id, StringComparer.Ordinal)
                .Select(BuildPrefab)
                .ToList();
            return new ModNode
            {
                Name = group.Key,
                Count = prefabs.Sum(p => p.Located + p.Unlocated),
                Prefabs = prefabs,
            };
        }

        static PrefabNode BuildPrefab(MissingPrefabIssue issue) => new PrefabNode
        {
            Id = issue.Id,
            Name = PrefabIds.Name(issue.Id),
            Located = issue.Located.Count,
            Unlocated = issue.UnlocatedCount,
            Cursor = issue.Cursor,
            KeptAliveBy = issue.CaseCount == 0 ? issue.KeptAliveBy.Select(PrefabIds.Name).ToList() : new List<string>(),
        };
    }

    public static class PrefabIds
    {
        public const string UnknownPack = "Unknown pack";

        public static string Key(string id)
        {
            int hash = HashStart(id);
            return hash > 0 ? id.Substring(0, hash).TrimEnd() : id;
        }

        public static string Name(string id)
        {
            string key = Key(id);
            int separator = key.IndexOf(':');
            return separator >= 0 ? key.Substring(separator + 1) : key;
        }

        public static string Hash(string id)
        {
            int hash = HashStart(id);
            return hash > 0 ? id.Substring(hash + 1, id.Length - hash - 2) : null;
        }

        public static string Origin(MissingPrefabIssue issue, IPrefabOriginLookup origins)
        {
            if (issue.Content != null)
            {
                return Name(issue.Content);
            }

            string pack = origins?.Find(Key(issue.Id));
            if (!string.IsNullOrEmpty(pack))
            {
                return pack;
            }

            string hash = Hash(issue.Id);
            return hash == null ? UnknownPack : $"{UnknownPack} ({hash})";
        }

        static int HashStart(string id) => id != null && id.EndsWith(")", StringComparison.Ordinal) ? id.LastIndexOf('(') : -1;
    }
}
