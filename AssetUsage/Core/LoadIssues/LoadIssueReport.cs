using System;
using System.Collections.Generic;
using System.Linq;

namespace AssetUsage.Core.LoadIssues
{
    public readonly struct CaseRef : IEquatable<CaseRef>
    {
        public CaseRef(int index, int version)
        {
            Index = index;
            Version = version;
        }

        public int Index { get; }

        public int Version { get; }

        public bool Equals(CaseRef other) => Index == other.Index && Version == other.Version;

        public override bool Equals(object obj) => obj is CaseRef other && Equals(other);

        public override int GetHashCode() => (Index * 397) ^ Version;

        public override string ToString() => $"{Index}:{Version}";
    }

    public sealed class LocatedCase
    {
        public LocatedCase(CaseRef entity, string kind)
        {
            Entity = entity;
            Kind = kind;
        }

        public CaseRef Entity { get; }

        public string Kind { get; }
    }

    public sealed class MissingPrefabIssue
    {
        readonly HashSet<(string Kind, CaseRef Entity)> seen = new HashSet<(string Kind, CaseRef Entity)>();

        public MissingPrefabIssue(string id)
        {
            Id = id;
            Category = LoadIssueClassifier.Classify(id);
        }

        public string Id { get; }

        public LoadIssueCategory Category { get; }

        public string Content { get; set; }

        public string FirstEntity { get; private set; }

        public SortedDictionary<string, int> References { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public SortedSet<string> KeptAliveBy { get; } = new SortedSet<string>(StringComparer.Ordinal);

        public List<LocatedCase> Located { get; } = new List<LocatedCase>();

        public int UnlocatedCount { get; private set; }

        public int Cursor { get; private set; }

        public int ReferenceCount => References.Values.Sum();

        public int CaseCount => Located.Count + UnlocatedCount;

        public void AddReference(string kind, CaseRef entity, bool located)
        {
            if (!seen.Add((kind, entity)))
            {
                return;
            }

            References.TryGetValue(kind, out int count);
            References[kind] = count + 1;
            FirstEntity ??= entity.ToString();
            if (located)
            {
                Located.Add(new LocatedCase(entity, kind));
            }
            else
            {
                UnlocatedCount++;
            }
        }

        public bool Remove(LocatedCase located)
        {
            if (!Located.Remove(located))
            {
                return false;
            }

            seen.Remove((located.Kind, located.Entity));
            int remaining = References[located.Kind] - 1;
            if (remaining > 0)
            {
                References[located.Kind] = remaining;
            }
            else
            {
                References.Remove(located.Kind);
            }

            Cursor = Math.Min(Cursor, Located.Count);
            return true;
        }

        public CaseRef? Step(int delta)
        {
            if (Located.Count == 0)
            {
                return null;
            }

            Cursor = CaseCursor.Next(Cursor, delta, Located.Count);
            return Located[Cursor - 1].Entity;
        }
    }

    public static class CaseCursor
    {
        public static int Next(int cursor, int delta, int count)
        {
            if (cursor <= 0)
            {
                return delta >= 0 ? 1 : count;
            }

            return ((((cursor - 1 + delta) % count) + count) % count) + 1;
        }
    }

    public sealed class LoadIssueReport
    {
        readonly Dictionary<string, MissingPrefabIssue> missing = new Dictionary<string, MissingPrefabIssue>(StringComparer.Ordinal);
        readonly List<CaseRef> brokenNodes = new List<CaseRef>();

        public IReadOnlyCollection<MissingPrefabIssue> Missing => missing.Values;

        public IReadOnlyList<CaseRef> BrokenNodes => brokenNodes;

        public int BrokenCursor { get; private set; }

        public void AddMissing(string id)
        {
            if (!missing.ContainsKey(id))
            {
                missing[id] = new MissingPrefabIssue(id);
            }
        }

        public void AddReference(string id, string kind, CaseRef entity, bool located)
        {
            AddMissing(id);
            missing[id].AddReference(kind, entity, located);
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

        public void AddBrokenNode(CaseRef node)
        {
            int position = brokenNodes.FindIndex(n => n.Index >= node.Index);
            if (position < 0)
            {
                brokenNodes.Add(node);
            }
            else if (!brokenNodes[position].Equals(node))
            {
                brokenNodes.Insert(position, node);
            }
        }

        public bool RemoveBrokenNode(CaseRef node)
        {
            bool removed = brokenNodes.Remove(node);
            BrokenCursor = Math.Min(BrokenCursor, brokenNodes.Count);
            return removed;
        }

        public CaseRef? Step(string id, int delta) => missing.TryGetValue(id, out var issue) ? issue.Step(delta) : null;

        public CaseRef? StepBrokenNode(int delta)
        {
            if (brokenNodes.Count == 0)
            {
                return null;
            }

            BrokenCursor = CaseCursor.Next(BrokenCursor, delta, brokenNodes.Count);
            return brokenNodes[BrokenCursor - 1];
        }

        public bool IsListed(MissingPrefabIssue issue) => IsListed(issue, new HashSet<string>(StringComparer.Ordinal));

        bool IsListed(MissingPrefabIssue issue, HashSet<string> visiting)
        {
            if (issue.CaseCount > 0)
            {
                return true;
            }

            if (!visiting.Add(issue.Id))
            {
                return false;
            }

            return issue.KeptAliveBy.Any(source => !missing.TryGetValue(source, out var parent) || IsListed(parent, visiting));
        }

        public IEnumerable<string> Lines(double elapsedMilliseconds)
        {
            yield return Summary(elapsedMilliseconds);
            foreach (var issue in missing.Values.OrderBy(i => i.Category).ThenBy(i => i.Id, StringComparer.Ordinal))
            {
                yield return Describe(issue);
            }

            foreach (var node in brokenNodes)
            {
                yield return $"Broken node {node.Index}";
            }
        }

        public string Summary(double elapsedMilliseconds) =>
            $"Load issues: {missing.Count} missing prefabs, {missing.Values.Sum(i => i.ReferenceCount)} references, {brokenNodes.Count} broken nodes, scanned in {elapsedMilliseconds:F0} ms.";

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
