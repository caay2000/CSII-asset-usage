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

    public sealed class TypeNode
    {
        public string Category { get; set; }

        public int Count { get; set; }

        public List<PrefabNode> Prefabs { get; set; }
    }

    public sealed class ModNode
    {
        public string Name { get; set; }

        public int Count { get; set; }

        public List<TypeNode> Types { get; set; }
    }

    public sealed class LoadIssueTree
    {
        public string Error { get; set; }

        public bool Scanned { get; set; }

        public int Total { get; set; }

        public int BrokenNodes { get; set; }

        public int BrokenCursor { get; set; }

        public List<ModNode> Mods { get; set; } = new List<ModNode>();

        public static LoadIssueTree Build(LoadIssueReport report, IPrefabOriginLookup origins)
        {
            var listed = report.Missing
                .Where(i => i.Category != LoadIssueCategory.Content && report.IsListed(i))
                .ToList();
            var mods = listed
                .GroupBy(i => PrefabIds.Origin(i, origins), StringComparer.Ordinal)
                .Select(BuildMod)
                .OrderByDescending(m => m.Count)
                .ThenBy(m => m.Name, StringComparer.Ordinal)
                .ToList();
            return new LoadIssueTree
            {
                Scanned = true,
                Total = mods.Sum(m => m.Count) + report.BrokenNodes.Count,
                BrokenNodes = report.BrokenNodes.Count,
                BrokenCursor = report.BrokenCursor,
                Mods = mods,
            };
        }

        static ModNode BuildMod(IGrouping<string, MissingPrefabIssue> group)
        {
            var types = group
                .GroupBy(i => i.Category)
                .OrderBy(g => g.Key)
                .Select(BuildType)
                .ToList();
            return new ModNode { Name = group.Key, Count = types.Sum(t => t.Count), Types = types };
        }

        static TypeNode BuildType(IGrouping<LoadIssueCategory, MissingPrefabIssue> group)
        {
            var prefabs = group
                .OrderByDescending(i => i.CaseCount)
                .ThenBy(i => i.Id, StringComparer.Ordinal)
                .Select(BuildPrefab)
                .ToList();
            return new TypeNode
            {
                Category = group.Key.ToString(),
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
