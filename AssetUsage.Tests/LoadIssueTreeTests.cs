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

            var mod = Assert.Single(Assert.Single(tree.Groups).Mods);
            Assert.Equal("Mod:160092", mod.Name);
            Assert.Equal(3, mod.Count);
            var prefab = Assert.Single(mod.Prefabs);
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

            var names = LoadIssueTree.Build(report, origins).Groups.SelectMany(g => g.Mods).Select(m => m.Name).ToList();

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

            var zone = LoadIssueTree.Build(report, null).Groups.Single(g => g.Category == "Zone").Mods.Single().Prefabs.Single();
            Assert.Equal(new[] { "House" }, zone.KeptAliveBy);

            var building = report.Missing.Single(i => i.Id == Building);
            building.Remove(building.Located.Single());

            Assert.Empty(LoadIssueTree.Build(report, null).Groups);
        }

        [Fact]
        public void Groups_by_error_type_then_mod()
        {
            var report = new LoadIssueReport();
            report.AddReference("BrandPrefab:Cafe (c460)", "Company", new CaseRef(1, 1), true);
            report.AddReference("BrandPrefab:Shop (a2c8)", "Company", new CaseRef(2, 1), true);
            report.AddReference("SurfacePrefab:Golf (c0fa)", "PrefabRef", new CaseRef(3, 1), true);

            var tree = LoadIssueTree.Build(report, null);

            Assert.Equal(new[] { "Surface", "Brand" }, tree.Groups.Select(g => g.Category));
            var brands = tree.Groups.Single(g => g.Category == "Brand");
            Assert.Equal(2, brands.Count);
            Assert.Equal(2, brands.Mods.Count);
            Assert.Equal(3, tree.Total);
        }

        [Theory]
        [InlineData(LoadIssueCategory.Brand, new[] { LoadIssueFixes.ChangeBrand })]
        [InlineData(LoadIssueCategory.Surface, new[] { LoadIssueFixes.Remove, LoadIssueFixes.Replace })]
        [InlineData(LoadIssueCategory.Object, new string[0])]
        [InlineData(LoadIssueCategory.Building, new string[0])]
        public void Offers_only_the_bulk_fixes_of_each_type(LoadIssueCategory category, string[] expected)
        {
            Assert.Equal(expected, LoadIssueFixes.For(category));
        }

        [Fact]
        public void Broken_nodes_are_summarised_by_kind_with_the_current_one()
        {
            var report = new LoadIssueReport();
            report.AddBrokenNode(new CaseRef(1, 1), "Road");
            report.AddBrokenNode(new CaseRef(2, 1), "Road");
            report.AddBrokenNode(new CaseRef(3, 1), "Water pipe (underground)");
            report.StepBrokenNode(-1);

            var tree = LoadIssueTree.Build(report, null);

            Assert.Equal("Water pipe (underground)", tree.BrokenCurrent);
            Assert.Equal(new[] { "Road", "Water pipe (underground)" }, tree.BrokenKinds.Select(k => k.Name));
            Assert.Equal(2, tree.BrokenKinds[0].Count);
        }

        [Fact]
        public void The_next_issue_follows_the_panel_order_and_skips_issues_without_cases()
        {
            var report = new LoadIssueReport();
            report.AddReference("SurfacePrefab:A (a)", "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference("SurfacePrefab:B (b)", "PrefabRef", new CaseRef(2, 1), false);
            report.AddReference("BrandPrefab:C (c)", "Company", new CaseRef(3, 1), true);

            Assert.Equal("BrandPrefab:C (c)", report.NextIssueWithCases("SurfacePrefab:A (a)"));
            Assert.Equal("SurfacePrefab:A (a)", report.NextIssueWithCases("BrandPrefab:C (c)"));
            Assert.Equal("SurfacePrefab:A (a)", report.NextIssueWithCases(null));
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
        public void Removing_an_earlier_case_keeps_the_cursor_on_the_same_case()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(2, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(3, 1), true);
            report.Step(Building, 1);
            report.Step(Building, 1);
            var issue = report.Missing.Single();

            issue.Remove(issue.Located[0]);

            Assert.Equal(1, issue.Cursor);
            Assert.Equal(new CaseRef(2, 1), issue.Located[issue.Cursor - 1].Entity);
            Assert.Equal(new CaseRef(3, 1), report.Step(Building, 1));
        }

        [Theory]
        [InlineData(1, 3)]
        [InlineData(-1, 1)]
        public void Removing_a_stale_current_case_lands_on_its_neighbour(int delta, int expected)
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(2, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(3, 1), true);
            report.Step(Building, 1);
            report.Step(Building, 1);

            report.RemoveCurrent(Building, delta);

            Assert.Equal(new CaseRef(expected, 1), report.Step(Building, delta));
        }

        [Fact]
        public void Removing_the_only_stale_case_ends_navigation()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(1, 1), true);
            report.Step(Building, 1);

            report.RemoveCurrent(Building, 1);

            Assert.Null(report.Step(Building, 1));
            Assert.Equal(0, report.Missing.Single().Cursor);
        }

        [Fact]
        public void Removing_an_earlier_broken_node_keeps_the_cursor_on_the_same_node()
        {
            var report = new LoadIssueReport();
            report.AddBrokenNode(new CaseRef(5, 1));
            report.AddBrokenNode(new CaseRef(6, 1));
            report.AddBrokenNode(new CaseRef(7, 1));
            report.StepBrokenNode(1);
            report.StepBrokenNode(1);

            report.RemoveBrokenNode(new CaseRef(5, 1));

            Assert.Equal(1, report.BrokenCursor);
            Assert.Equal(new CaseRef(7, 1), report.StepBrokenNode(1));
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
