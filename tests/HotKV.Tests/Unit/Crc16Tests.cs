// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using HotKV.Cluster;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class Crc16Tests
    {
        [Fact]
        public void RawCrc16XmodemMatchesTheStandardCheckValue()
        {
            Assert.Equal(0x31C3, Crc16.Compute("123456789"));
        }

        [Theory]
        [InlineData("foo", 12182)]
        [InlineData("bar", 5061)]
        [InlineData("hello", 866)]
        [InlineData("{user1000}.following", 3443)]
        [InlineData("{user1000}.followers", 3443)]
        [InlineData("{}foo", 9500)]
        [InlineData("foo{}{bar}", 8363)]
        [InlineData("foo{{bar}}zap", 4015)]
        [InlineData("foo{bar}{zap}", 5061)]
        public void KeySlotMatchesClusterKeyslotVectors(string key, int expectedSlot)
        {
            Assert.Equal(expectedSlot, ClusterKeySlot.Compute(key));
        }

        [Fact]
        public void HashTagPinsTwoDifferentKeysToTheSameSlot()
        {
            // "foo{bar}{zap}" hashes on "bar" alone, same as the bare key "bar".
            Assert.Equal(ClusterKeySlot.Compute("bar"), ClusterKeySlot.Compute("foo{bar}{zap}"));
        }

        [Fact]
        public void EveryComputedSlotIsInRange()
        {
            foreach (var key in new[] { "foo", "bar", "", "{}", "a very long key indeed with lots of characters 0123456789" })
            {
                int slot = ClusterKeySlot.Compute(key);
                Assert.InRange(slot, 0, ClusterKeySlot.SlotCount - 1);
            }
        }
    }
}
