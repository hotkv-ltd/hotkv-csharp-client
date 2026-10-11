// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class ScanCommandsTests : IntegrationTestBase
    {
        public ScanCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task ScanFindsKeysByPattern()
        {
            RequireServer();
            string prefix = UniqueKey("scan");
            var keys = new[] { prefix + ":1", prefix + ":2", prefix + ":3" };
            try
            {
                foreach (var k in keys)
                {
                    await Client.StringSetAsync(k, "v");
                }

                var found = new HashSet<string>();
                await foreach (var key in Client.ScanAsync(match: prefix + ":*", count: 2))
                {
                    found.Add(key);
                }
                Assert.Equal(keys.Length, found.Count);
                foreach (var k in keys)
                {
                    Assert.Contains(k, found);
                }
            }
            finally
            {
                await Client.DeleteAsync(keys);
            }
        }

        [SkippableFact]
        public async Task HScanIteratesFields()
        {
            RequireServer();
            string key = UniqueKey("hscan");
            try
            {
                await Client.HashSetAsync(key, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3" });
                var found = new Dictionary<string, string>();
                await foreach (var (field, value) in Client.HashScanAsync(key, count: 1))
                {
                    found[field] = value;
                }
                Assert.Equal(3, found.Count);
                Assert.Equal("1", found["a"]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SScanIteratesMembers()
        {
            RequireServer();
            string key = UniqueKey("sscan");
            try
            {
                await Client.SetAddAsync(key, new[] { "a", "b", "c" });
                var found = new HashSet<string>();
                await foreach (var member in Client.SetScanAsync(key, count: 1))
                {
                    found.Add(member);
                }
                Assert.Equal(3, found.Count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ZScanIteratesMembersWithScores()
        {
            RequireServer();
            string key = UniqueKey("zscan");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2) });
                var found = new Dictionary<string, double>();
                await foreach (var (member, score) in Client.SortedSetScanAsync(key, count: 1))
                {
                    found[member] = score;
                }
                Assert.Equal(2, found.Count);
                Assert.Equal(1.0, found["a"]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
