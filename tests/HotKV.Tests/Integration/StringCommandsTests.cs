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
    public class StringCommandsTests : IntegrationTestBase
    {
        public StringCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task SetAndGetRoundTrip()
        {
            RequireServer();
            string key = UniqueKey("str");
            try
            {
                Assert.True(await Client.StringSetAsync(key, "hello"));
                Assert.Equal("hello", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task GetOfMissingKeyReturnsNull()
        {
            RequireServer();
            string key = UniqueKey("str-missing");
            Assert.Null(await Client.StringGetAsync(key));
        }

        [SkippableFact]
        public async Task SetWithNxOnlySetsOnce()
        {
            RequireServer();
            string key = UniqueKey("str-nx");
            try
            {
                Assert.True(await Client.StringSetAsync(key, "first", new SetOptions { OnlyIfExists = false }));
                Assert.False(await Client.StringSetAsync(key, "second", new SetOptions { OnlyIfExists = false }));
                Assert.Equal("first", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SetWithXxOnlySetsWhenExists()
        {
            RequireServer();
            string key = UniqueKey("str-xx");
            try
            {
                Assert.False(await Client.StringSetAsync(key, "v", new SetOptions { OnlyIfExists = true }));
                Assert.Null(await Client.StringGetAsync(key));
                await Client.StringSetAsync(key, "v1");
                Assert.True(await Client.StringSetAsync(key, "v2", new SetOptions { OnlyIfExists = true }));
                Assert.Equal("v2", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SetWithExpirySetsTtl()
        {
            RequireServer();
            string key = UniqueKey("str-ex");
            try
            {
                await Client.StringSetAsync(key, "v", new SetOptions { Expiry = TimeSpan.FromSeconds(60) });
                long ttl = await Client.TtlAsync(key);
                Assert.InRange(ttl, 1, 60);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SetWithKeepTtlPreservesExistingExpiry()
        {
            RequireServer();
            string key = UniqueKey("str-keepttl");
            try
            {
                await Client.StringSetAsync(key, "v1", new SetOptions { Expiry = TimeSpan.FromSeconds(120) });
                await Client.StringSetAsync(key, "v2", new SetOptions { KeepTtl = true });
                long ttl = await Client.TtlAsync(key);
                Assert.InRange(ttl, 1, 120);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SetAndGetReturnsPreviousValue()
        {
            RequireServer();
            string key = UniqueKey("str-getset");
            try
            {
                await Client.StringSetAsync(key, "old");
                string? previous = await Client.StringSetAndGetAsync(key, "new");
                Assert.Equal("old", previous);
                Assert.Equal("new", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task BinarySafeRoundTrip()
        {
            RequireServer();
            string key = UniqueKey("str-bin");
            byte[] payload = { 0x00, 0x01, 0xFF, (byte)'\r', (byte)'\n', 0x10, 0x00 };
            try
            {
                await Client.StringSetAsync(key, payload);
                byte[]? read = await Client.StringGetBytesAsync(key);
                Assert.Equal(payload, read);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task IncrDecrAndIncrByFloat()
        {
            RequireServer();
            string key = UniqueKey("str-incr");
            try
            {
                Assert.Equal(1, await Client.IncrementAsync(key));
                Assert.Equal(6, await Client.IncrementByAsync(key, 5));
                Assert.Equal(5, await Client.DecrementAsync(key));
                Assert.Equal(2, await Client.DecrementByAsync(key, 3));
                double f = await Client.IncrementByFloatAsync(key, 1.5);
                Assert.Equal(3.5, f, 6);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task AppendStrlenGetrangeSetrange()
        {
            RequireServer();
            string key = UniqueKey("str-manip");
            try
            {
                await Client.StringSetAsync(key, "Hello");
                Assert.Equal(11, await Client.StringAppendAsync(key, " World"));
                Assert.Equal(11, await Client.StringLengthAsync(key));
                Assert.Equal("Hello", await Client.StringGetRangeAsync(key, 0, 4));
                await Client.StringSetRangeAsync(key, 6, "Redis");
                Assert.Equal("Hello Redis", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task MGetMSetMSetNx()
        {
            RequireServer();
            string k1 = UniqueKey("str-m1");
            string k2 = UniqueKey("str-m2");
            string k3 = UniqueKey("str-m3");
            try
            {
                await Client.StringSetMultipleAsync(new Dictionary<string, string> { [k1] = "a", [k2] = "b" });
                var values = await Client.StringGetMultipleAsync(new[] { k1, k2, k3 });
                Assert.Equal("a", values[0]);
                Assert.Equal("b", values[1]);
                Assert.Null(values[2]);

                bool allNew = await Client.StringSetMultipleIfNoneExistAsync(new Dictionary<string, string> { [k3] = "c" });
                Assert.True(allNew);
                bool notAllNew = await Client.StringSetMultipleIfNoneExistAsync(new Dictionary<string, string> { [k1] = "z", [k3] = "z" });
                Assert.False(notAllNew);
                Assert.Equal("a", await Client.StringGetAsync(k1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2, k3 });
            }
        }

        [SkippableFact]
        public async Task GetDelAndGetEx()
        {
            RequireServer();
            string key = UniqueKey("str-getdel");
            try
            {
                await Client.StringSetAsync(key, "v");
                Assert.Equal("v", await Client.StringGetDeleteAsync(key));
                Assert.False(await Client.ExistsAsync(key));

                await Client.StringSetAsync(key, "v2");
                await Client.StringGetExAsync(key, expiry: TimeSpan.FromSeconds(30));
                Assert.InRange(await Client.TtlAsync(key), 1, 30);
                await Client.StringGetExAsync(key, persist: true);
                Assert.Equal(-1, await Client.TtlAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
