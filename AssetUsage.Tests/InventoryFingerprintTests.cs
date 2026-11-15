using System;
using AssetUsage.Core;
using Xunit;

namespace AssetUsage.Tests
{
    public class InventoryFingerprintTests
    {
        static readonly DateTime Now = new DateTime(2026, 11, 15, 12, 0, 0);

        static Inventory Saved(string fingerprint, DateTime generated) =>
            new Inventory { Fingerprint = fingerprint, GeneratedAt = generated.ToString("yyyy-MM-ddTHH:mm:ss") };

        [Fact]
        public void The_order_of_the_prefabs_does_not_change_the_fingerprint()
        {
            Assert.Equal(InventoryFingerprint.Of(new[] { "A:1", "B:2", "C:3" }), InventoryFingerprint.Of(new[] { "C:3", "A:1", "B:2" }));
        }

        [Fact]
        public void Adding_removing_or_renaming_a_prefab_changes_the_fingerprint()
        {
            string original = InventoryFingerprint.Of(new[] { "A:1", "B:2" });

            Assert.NotEqual(original, InventoryFingerprint.Of(new[] { "A:1", "B:2", "C:3" }));
            Assert.NotEqual(original, InventoryFingerprint.Of(new[] { "A:1" }));
            Assert.NotEqual(original, InventoryFingerprint.Of(new[] { "A:1", "B:3" }));
        }

        [Fact]
        public void Missing_prefabs_are_skipped()
        {
            Assert.Equal(InventoryFingerprint.Of(new[] { "A:1" }), InventoryFingerprint.Of(new[] { null, "A:1", null }));
        }

        [Fact]
        public void A_recent_inventory_with_the_same_fingerprint_is_reused()
        {
            Assert.True(InventoryFingerprint.Matches(Saved("f", Now.AddDays(-1)), "f", Now));
        }

        [Fact]
        public void An_inventory_is_rebuilt_when_it_differs_is_old_or_missing()
        {
            Assert.False(InventoryFingerprint.Matches(Saved("other", Now.AddDays(-1)), "f", Now));
            Assert.False(InventoryFingerprint.Matches(Saved("f", Now.AddDays(-8)), "f", Now));
            Assert.False(InventoryFingerprint.Matches(Saved(null, Now), "f", Now));
            Assert.False(InventoryFingerprint.Matches(null, "f", Now));
        }
    }
}
