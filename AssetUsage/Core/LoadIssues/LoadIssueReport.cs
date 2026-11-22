using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetUsage.Core.LoadIssues
{
    public sealed class MissingPrefabIssue
    {
        public MissingPrefabIssue(string id)
        {
            Id = id;
            Category = LoadIssueClassifier.Classify(id);
        }

        public string Id { get; }

        public LoadIssueCategory Category { get; }

        public string Content { get; set; }

        public string FirstEntity { get; set; }

        public SortedDictionary<string, int> References { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public SortedSet<string> KeptAliveBy { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public int ReferenceCount => References.Values.Sum();
    }

    public sealed class LoadIssueReport
    {
        readonly Dictionary<string, MissingPrefabIssue> missing = new Dictionary<string, MissingPrefabIssue>(StringComparer.Ordinal);
        readonly SortedSet<int> brokenNodes = new SortedSet<int>();

        public IReadOnlyCollection<MissingPrefabIssue> Missing => missing.Values;

        public IReadOnlyCollection<int> BrokenNodes => brokenNodes;

        public void AddMissing(string id)
        {
            if (!missing.ContainsKey(id))
            {
                missing[id] = new MissingPrefabIssue(id);
            }
        }

        public void AddReference(string id, string kind, string entity)
        {
            AddMissing(id);
            var issue = missing[id];
            issue.References.TryGetValue(kind, out int count);
            issue.References[kind] = count + 1;
            issue.FirstEntity ??= entity;
        }

        public void AddDependency(string target, string source)
        {
            AddMissing(target);
            var issue = missing[target];
            issue.KeptAliveBy.Add(source);
            if (issue.Category == LoadIssueCategory.Content && missing.TryGetValue(source, out var dependent))
            {
                dependent.Content ??= target;
            }
        }

        public void AddBrokenNode(int node) => brokenNodes.Add(node);

        public IEnumerable<string> Lines(double elapsedMilliseconds)
        {
            yield return $"Load issues: {missing.Count} missing prefabs, {missing.Values.Sum(i => i.ReferenceCount)} references, {brokenNodes.Count} broken nodes, scanned in {elapsedMilliseconds:F0} ms.";
            foreach (var issue in missing.Values.OrderBy(i => i.Category).ThenBy(i => i.Id, StringComparer.Ordinal))
            {
                yield return Describe(issue);
            }

            foreach (int node in brokenNodes)
            {
                yield return $"Broken node {node}";
            }
        }

        static string Describe(MissingPrefabIssue issue)
        {
            string references = issue.References.Count == 0 ? "none" : string.Join(", ", issue.References.Select(r => $"{r.Key}={r.Value}"));
            string line = $"Missing [{issue.Category}] {issue.Id} | references: {references}";
            if (issue.FirstEntity != null)
            {
                line += $" | first: {issue.FirstEntity}";
            }

            if (issue.Content != null)
            {
                line += $" | content: {issue.Content}";
            }

            if (issue.KeptAliveBy.Count > 0)
            {
                line += $" | kept alive by: {string.Join(", ", issue.KeptAliveBy)}";
            }

            return line;
        }
    }
}
