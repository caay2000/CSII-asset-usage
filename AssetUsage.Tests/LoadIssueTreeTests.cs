using System.Collections.Generic;
using System.Linq;
using AssetUsage.Core.LoadIssues;
using Xunit;

namespace AssetUsage.Tests
{
    public class LoadIssueTreeTests
    {
        const string Building = "BuildingPrefab:House (f344)";
        const string Zone = "ZonePrefab:Italian (f344)";
        const string Brand = "BrandPrefab:Cafe (c460)";
        const string Content = "ContentPrefab:Mod:160092";

        sealed class Origins : IPrefabOriginLookup
        {
            readonly Dictionary<string, string> packs;

            public Origins(Dictionary<string, string> packs) => this.packs = packs;

            public string Find(string prefabKey) => packs.TryGetValue(prefabKey, out var pack) ? pack : null;
        }

        [Fact]
        public void Groups_by_mod_then_type_and_counts_cases()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(2, 1), true);
            report.AddReference(Building, "Chirp", new CaseRef(3, 1), false);
            report.AddDependency(Content, Building);
            report.AddBrokenNode(new CaseRef(9, 1));

            var tree = LoadIssueTree.Build(report, null);

            var mod = Assert.Single(tree.Mods);
            Assert.Equal("Mod:160092", mod.Name);
            Assert.Equal(3, mod.Count);
            var prefab = Assert.Single(Assert.Single(mod.Types).Prefabs);
            Assert.Equal("House", prefab.Name);
            Assert.Equal(2, prefab.Located);
            Assert.Equal(1, prefab.Unlocated);
            Assert.Equal(4, tree.Total);
            Assert.Equal(1, tree.BrokenNodes);
        }

        [Fact]
        public void Attribution_falls_back_from_content_to_history_to_unknown_pack()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference(Brand, "Company", new CaseRef(2, 1), true);
            var origins = new Origins(new Dictionary<string, string> { ["BuildingPrefab:House"] = "pack-a" });

            var names = LoadIssueTree.Build(report, origins).Mods.Select(m => m.Name).ToList();

            Assert.Contains("pack-a", names);
            Assert.Contains("Unknown pack (c460)", names);
        }

        [Fact]
        public void A_prefab_only_kept_alive_shows_what_keeps_it_and_leaves_with_it()
        {
            var report = new LoadIssueReport();
            var entity = new CaseRef(1, 1);
            report.AddReference(Building, "PrefabRef", entity, true);
            report.AddDependency(Zone, Building);

            var zone = LoadIssueTree.Build(report, null).Mods.SelectMany(m => m.Types).Single(t => t.Category == "Zone").Prefabs.Single();
            Assert.Equal(new[] { "House" }, zone.KeptAliveBy);

            var building = report.Missing.Single(i => i.Id == Building);
            building.Remove(building.Located.Single());

            Assert.Empty(LoadIssueTree.Build(report, null).Mods);
        }

        [Fact]
        public void The_same_reference_counts_once()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "VehicleModel", new CaseRef(1, 1), false);
            report.AddReference(Building, "VehicleModel", new CaseRef(1, 1), false);

            Assert.Equal(1, report.Missing.Single().ReferenceCount);
        }

        [Theory]
        [InlineData(0, 1, 3, 1)]
        [InlineData(0, -1, 3, 3)]
        [InlineData(3, 1, 3, 1)]
        [InlineData(1, -1, 3, 3)]
        [InlineData(2, 1, 3, 3)]
        public void The_cursor_wraps(int cursor, int delta, int count, int expected)
        {
            Assert.Equal(expected, CaseCursor.Next(cursor, delta, count));
        }

        [Fact]
        public void Removing_the_last_case_keeps_the_cursor_in_range()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(2, 1), true);
            report.Step(Building, -1);
            var issue = report.Missing.Single();

            issue.Remove(issue.Located[1]);

            Assert.Equal(1, issue.Cursor);
            Assert.Equal(1, issue.References["PrefabRef"]);
        }

        [Fact]
        public void Broken_nodes_are_navigated_and_removed()
        {
            var report = new LoadIssueReport();
            report.AddBrokenNode(new CaseRef(7, 1));
            report.AddBrokenNode(new CaseRef(5, 1));

            Assert.Equal(new CaseRef(5, 1), report.StepBrokenNode(1));
            Assert.True(report.RemoveBrokenNode(new CaseRef(5, 1)));
            Assert.Equal(new CaseRef(7, 1), report.StepBrokenNode(1));
        }
    }
}
