using System.Collections.Generic;
using System.Linq;
using AssetUsage.Core;
using Xunit;

namespace AssetUsage.Tests
{
    public class UsageTests
    {
        static readonly PackRef PackA = new PackRef { Id = "pdx:1", Source = PackSource.ParadoxMods, PdxId = "1", Name = "A" };
        static readonly PackRef PackB = new PackRef { Id = "pdx:2", Source = PackSource.ParadoxMods, PdxId = "2", Name = "B" };

        static Inventory Graph()
        {
            var inventory = new Inventory();
            inventory.Add("BuildingPrefab", "House", PackA, new[] { "RenderPrefab:HouseMesh", "StaticObjectPrefab:Fence" });
            inventory.Add("RenderPrefab", "HouseMesh", PackA, new string[0]);
            inventory.Add("StaticObjectPrefab", "Fence", PackB, new[] { "RenderPrefab:FenceMesh" });
            inventory.Add("RenderPrefab", "FenceMesh", PackB, new string[0]);
            inventory.Add("StaticObjectPrefab", "Loop1", PackB, new[] { "StaticObjectPrefab:Loop2" });
            inventory.Add("StaticObjectPrefab", "Loop2", PackB, new[] { "StaticObjectPrefab:Loop1" });
            return inventory;
        }

        [Fact]
        public void Everything_a_used_prefab_references_is_used_indirectly_through_it()
        {
            var indirect = Propagation.Indirect(new[] { "BuildingPrefab:House" }, Graph().RefsOf);

            Assert.Equal(new[] { "RenderPrefab:HouseMesh", "StaticObjectPrefab:Fence", "RenderPrefab:FenceMesh" }.OrderBy(k => k), indirect.Keys.OrderBy(k => k));
            Assert.Equal(new[] { "StaticObjectPrefab:Fence" }, indirect["RenderPrefab:FenceMesh"]);
        }

        [Fact]
        public void Reference_cycles_end()
        {
            var indirect = Propagation.Indirect(new[] { "StaticObjectPrefab:Loop1" }, Graph().RefsOf);

            Assert.Equal(new[] { "StaticObjectPrefab:Loop2" }, indirect.Keys);
        }

        [Fact]
        public void A_prefab_used_directly_is_not_also_counted_as_indirect()
        {
            var indirect = Propagation.Indirect(new[] { "BuildingPrefab:House", "StaticObjectPrefab:Fence" }, Graph().RefsOf);

            Assert.DoesNotContain("StaticObjectPrefab:Fence", indirect.Keys);
            Assert.Contains("RenderPrefab:FenceMesh", indirect.Keys);
        }

        [Fact]
        public void A_prefab_id_from_two_packs_belongs_to_both()
        {
            var inventory = Graph();
            inventory.Add("RenderPrefab", "HouseMesh", PackB, new string[0]);

            Assert.Equal(new[] { "pdx:1", "pdx:2" }, inventory.Prefabs["RenderPrefab:HouseMesh"].Packs);
            Assert.Equal(5, inventory.Packs["pdx:2"].PrefabCount);
        }

        [Fact]
        public void The_ledger_keeps_the_first_and_last_day_for_each_city()
        {
            var inventory = Graph();
            var ledger = new UsageLedger();
            var direct = new[] { "BuildingPrefab:House" };
            var indirect = Propagation.Indirect(direct, inventory.RefsOf);

            ledger.Record("c1", "Town", "2026-11-14", direct, indirect, k => inventory.Prefabs[k]);
            ledger.Record("c1", "Town", "2026-11-20", direct, new Dictionary<string, List<string>>(), k => inventory.Prefabs[k]);

            var house = ledger.Prefabs["BuildingPrefab:House"];
            Assert.Equal(new[] { "pdx:1" }, house.Packs);
            Assert.Equal("2026-11-14", house.Cities["c1"].FirstDirect);
            Assert.Equal("2026-11-20", house.Cities["c1"].LastDirect);
            var fence = ledger.Prefabs["StaticObjectPrefab:Fence"].Cities["c1"];
            Assert.Null(fence.FirstDirect);
            Assert.Equal("2026-11-14", fence.LastIndirect);
            Assert.Equal(new[] { "BuildingPrefab:House" }, fence.Via);
            Assert.Equal("2026-11-20", ledger.Cities["c1"].LastSeen);
        }

        [Fact]
        public void The_ledger_keeps_prefabs_that_are_no_longer_loaded()
        {
            var inventory = Graph();
            var ledger = new UsageLedger();
            ledger.Record("c1", "Town", "2026-11-14", new[] { "BuildingPrefab:House" }, new Dictionary<string, List<string>>(), k => inventory.Prefabs[k]);

            ledger.Record("c2", "Other", "2026-11-15", new[] { "BuildingPrefab:Gone" }, new Dictionary<string, List<string>>(), k => null);

            Assert.Equal(new[] { "pdx:1" }, ledger.Prefabs["BuildingPrefab:House"].Packs);
            Assert.Empty(ledger.Prefabs["BuildingPrefab:Gone"].Packs);
            Assert.Equal(2, ledger.Cities.Count);
        }
    }
}
