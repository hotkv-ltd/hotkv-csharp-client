// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class HashCommandsTests : IntegrationTestBase
    {
        public HashCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task SetGetAllDelete()
        {
            RequireServer();
            string key = UniqueKey("hash-basic");
            try
            {
                Assert.Equal(2, await Client.HashSetAsync(key, new Dictionary<string, string> { ["f1"] = "v1", ["f2"] = "v2" }));
                Assert.Equal("v1", await Client.HashGetAsync(key, "f1"));
                Assert.Equal(2, await Client.HashLengthAsync(key));
                Assert.True(await Client.HashExistsAsync(key, "f1"));

                var all = await Client.HashGetAllAsync(key);
                Assert.Equal("v1", all["f1"]);
                Assert.Equal("v2", all["f2"]);

                var multi = await Client.HashGetMultipleAsync(key, new[] { "f1", "missing" });
                Assert.Equal("v1", multi[0]);
                Assert.Null(multi[1]);

                Assert.Equal(1, await Client.HashDeleteAsync(key, new[] { "f1" }));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SetNxKeysValuesIncrement()
        {
            RequireServer();
            string key = UniqueKey("hash-incr");
            try
            {
                Assert.True(await Client.HashSetIfNotExistsAsync(key, "counter", "10"));
                Assert.False(await Client.HashSetIfNotExistsAsync(key, "counter", "99"));
                Assert.Equal(15, await Client.HashIncrementByAsync(key, "counter", 5));
                double f = await Client.HashIncrementByFloatAsync(key, "counter", 0.5);
                Assert.Equal(15.5, f, 6);
                Assert.Equal(4, await Client.HashStringLengthAsync(key, "counter"));

                await Client.HashSetAsync(key, "name", "x");
                var keys = await Client.HashKeysAsync(key);
                Assert.Contains("counter", keys);
                Assert.Contains("name", keys);
                var values = await Client.HashValuesAsync(key);
                Assert.Contains("x", values);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RandomField()
        {
            RequireServer();
            string key = UniqueKey("hash-random");
            try
            {
                await Client.HashSetAsync(key, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3" });
                var one = await Client.HashRandomFieldAsync(key);
                Assert.Single(one);
                var many = await Client.HashRandomFieldAsync(key, 2);
                Assert.Equal(2, many.Count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task FieldExpiryFamily()
        {
            RequireServer();
            string key = UniqueKey("hash-fieldttl");
            try
            {
                await Client.HashSetAsync(key, new Dictionary<string, string> { ["f1"] = "v1", ["f2"] = "v2" });
                var expireResult = await Client.HashFieldExpireAsync(key, TimeSpan.FromSeconds(60), new[] { "f1" });
                Assert.Equal(1, expireResult[0]);

                var ttl = await Client.HashFieldTtlAsync(key, new[] { "f1", "f2" });
                Assert.InRange(ttl[0], 1, 60);
                Assert.Equal(-1, ttl[1]);

                var persisted = await Client.HashFieldPersistAsync(key, new[] { "f1" });
                Assert.Equal(1, persisted[0]);
                Assert.Equal(-1, (await Client.HashFieldTtlAsync(key, new[] { "f1" }))[0]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
