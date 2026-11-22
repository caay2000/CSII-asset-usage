using System.Linq;
using AssetUsage.Core.LoadIssues;
using Xunit;

namespace AssetUsage.Tests
{
    public class LoadIssueReportTests
    {
        const string Building = "BuildingPrefab:EU_ItMixedResidentialHigh01_L2_2x2 (f344)";
        const string Zone = "ZonePrefab:Italian wall-to-wall (f344)";
        const string Content = "ContentPrefab:Mod:160092";

        [Theory]
        [InlineData(Content, LoadIssueCategory.Content)]
        [InlineData(Building, LoadIssueCategory.Building)]
        [InlineData("StaticObjectPrefab:pd Bloom (c0b3)", LoadIssueCategory.Object)]
        [InlineData("SurfacePrefab:GolfRough (c0fa)", LoadIssueCategory.Surface)]
        [InlineData(Zone, LoadIssueCategory.Zone)]
        [InlineData("BrandPrefab:Q0024__Noodles (c460)", LoadIssueCategory.Brand)]
        [InlineData("NetPrefab:Road (aaaa)", LoadIssueCategory.Other)]
        [InlineData("[Missing]", LoadIssueCategory.Other)]
        [InlineData(null, LoadIssueCategory.Other)]
        public void Classifies_by_prefab_type(string id, LoadIssueCategory expected)
        {
            Assert.Equal(expected, LoadIssueClassifier.Classify(id));
        }

        [Fact]
        public void Counts_references_by_kind_and_keeps_the_first_entity()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(10, 1), true);
            report.AddReference(Building, "PrefabRef", new CaseRef(11, 1), true);
            report.AddReference(Building, "UnderConstruction", new CaseRef(12, 1), true);

            var issue = report.Missing.Single();
            Assert.Equal(3, issue.ReferenceCount);
            Assert.Equal(2, issue.References["PrefabRef"]);
            Assert.Equal("10:1", issue.FirstEntity);
        }

        [Fact]
        public void A_content_dependency_attributes_the_source_to_its_mod()
        {
            var report = new LoadIssueReport();
            report.AddMissing(Building);
            report.AddDependency(Content, Building);
            report.AddDependency(Zone, Building);

            Assert.Equal(Content, report.Missing.Single(i => i.Id == Building).Content);
            Assert.Contains(Building, report.Missing.Single(i => i.Id == Zone).KeptAliveBy);
            Assert.Null(report.Missing.Single(i => i.Id == Zone).Content);
        }

        [Fact]
        public void A_broken_node_is_listed_once()
        {
            var report = new LoadIssueReport();
            report.AddBrokenNode(new CaseRef(398223, 1));
            report.AddBrokenNode(new CaseRef(398223, 1));

            Assert.Single(report.BrokenNodes);
        }

        [Fact]
        public void Lines_start_with_the_summary_and_order_by_category()
        {
            var report = new LoadIssueReport();
            report.AddReference(Building, "PrefabRef", new CaseRef(10, 1), true);
            report.AddMissing(Content);
            report.AddBrokenNode(new CaseRef(5, 1));

            var lines = report.Lines(12).ToList();

            Assert.Equal("Load issues: 2 missing prefabs, 1 references, 1 broken nodes, scanned in 12 ms.", lines[0]);
            Assert.StartsWith("Missing [Content]", lines[1]);
            Assert.Equal($"Missing [Building] {Building} | references: PrefabRef=1 | first: 10:1", lines[2]);
            Assert.Equal("Broken node 5", lines[3]);
        }
    }
}
