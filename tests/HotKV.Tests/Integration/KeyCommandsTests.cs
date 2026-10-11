// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class KeyCommandsTests : IntegrationTestBase
    {
        public KeyCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task DeleteExistsType()
        {
            RequireServer();
            string key = UniqueKey("key-basic");
            await Client.StringSetAsync(key, "v");
            Assert.True(await Client.ExistsAsync(key));
            Assert.Equal("string", await Client.TypeAsync(key));
            Assert.Equal(1, await Client.DeleteAsync(key));
            Assert.False(await Client.ExistsAsync(key));
        }

        [SkippableFact]
        public async Task ExistsCountsRepeatedKeys()
        {
            RequireServer();
            string key = UniqueKey("key-exists");
            try
            {
                await Client.StringSetAsync(key, "v");
                long count = await Client.ExistsAsync(new[] { key, key, UniqueKey("missing") });
                Assert.Equal(2, count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ExpireTtlPersist()
        {
            RequireServer();
            string key = UniqueKey("key-expire");
            try
            {
                await Client.StringSetAsync(key, "v");
                Assert.Equal(-1, await Client.TtlAsync(key));
                Assert.True(await Client.ExpireAsync(key, TimeSpan.FromSeconds(100)));
                Assert.InRange(await Client.TtlAsync(key), 1, 100);
                Assert.True(await Client.PersistAsync(key));
                Assert.Equal(-1, await Client.TtlAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task TtlOfMissingKeyIsMinusTwo()
        {
            RequireServer();
            Assert.Equal(-2, await Client.TtlAsync(UniqueKey("nope")));
        }

        [SkippableFact]
        public async Task PExpireAtAndPTtl()
        {
            RequireServer();
            string key = UniqueKey("key-pexpireat");
            try
            {
                await Client.StringSetAsync(key, "v");
                Assert.True(await Client.PExpireAtAsync(key, DateTimeOffset.UtcNow.AddMinutes(5)));
                Assert.InRange(await Client.PTtlAsync(key), 1, 5 * 60_000);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RenameAndRenameNx()
        {
            RequireServer();
            string a = UniqueKey("key-rename-a");
            string b = UniqueKey("key-rename-b");
            string c = UniqueKey("key-rename-c");
            try
            {
                await Client.StringSetAsync(a, "v");
                await Client.RenameAsync(a, b);
                Assert.Equal("v", await Client.StringGetAsync(b));
                Assert.False(await Client.ExistsAsync(a));

                await Client.StringSetAsync(a, "other");
                Assert.False(await Client.RenameIfNotExistsAsync(a, b));
                await Client.DeleteAsync(b);
                Assert.True(await Client.RenameIfNotExistsAsync(a, c));
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b, c });
            }
        }

        [SkippableFact]
        public async Task CopyDuplicatesAValue()
        {
            RequireServer();
            string source = UniqueKey("key-copy-src");
            string destination = UniqueKey("key-copy-dst");
            try
            {
                await Client.StringSetAsync(source, "v");
                Assert.True(await Client.CopyAsync(source, destination));
                Assert.Equal("v", await Client.StringGetAsync(destination));
            }
            finally
            {
                await Client.DeleteAsync(new[] { source, destination });
            }
        }

        [SkippableFact]
        public async Task DumpAndRestoreRoundTrip()
        {
            RequireServer();
            string source = UniqueKey("key-dump-src");
            string destination = UniqueKey("key-dump-dst");
            try
            {
                await Client.StringSetAsync(source, "dump-me");
                byte[]? dump = await Client.DumpAsync(source);
                Assert.NotNull(dump);
                await Client.RestoreAsync(destination, TimeSpan.Zero, dump!);
                Assert.Equal("dump-me", await Client.StringGetAsync(destination));
            }
            finally
            {
                await Client.DeleteAsync(new[] { source, destination });
            }
        }

        [SkippableFact]
        public async Task TouchReportsHowManyKeysExist()
        {
            RequireServer();
            string a = UniqueKey("key-touch-a");
            string b = UniqueKey("key-touch-b");
            try
            {
                await Client.StringSetAsync(a, "v");
                long touched = await Client.TouchAsync(new[] { a, b });
                Assert.Equal(1, touched);
            }
            finally
            {
                await Client.DeleteAsync(a);
            }
        }

        [SkippableFact]
        public async Task KeysMatchesPattern()
        {
            RequireServer();
            string prefix = UniqueKey("key-pattern");
            string k1 = prefix + ":1";
            string k2 = prefix + ":2";
            try
            {
                await Client.StringSetAsync(k1, "v");
                await Client.StringSetAsync(k2, "v");
                var found = await Client.KeysAsync(prefix + ":*");
                Assert.Contains(k1, found);
                Assert.Contains(k2, found);
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2 });
            }
        }

        [SkippableFact]
        public async Task NanosecondExpiry()
        {
            RequireServer();
            string key = UniqueKey("key-nexpire");
            try
            {
                await Client.StringSetAsync(key, "v");
                Assert.Equal(-1, await Client.NTtlAsync(key));
                Assert.True(await Client.NExpireAsync(key, 60_000_000_000L));
                long remaining = await Client.NTtlAsync(key);
                Assert.InRange(remaining, 1, 60_000_000_000L);

                Assert.True(await Client.NExpireAtAsync(key, DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeMilliseconds() * 1_000_000L));
                Assert.True(await Client.NTtlAsync(key) > 0);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task NExpireOnMissingKeyReturnsFalse()
        {
            RequireServer();
            Assert.False(await Client.NExpireAsync(UniqueKey("nexpire-missing"), 1_000_000));
        }

        [SkippableFact]
        public async Task HotKeysTopAndReset()
        {
            RequireServer();
            string key = UniqueKey("key-hot");
            try
            {
                await Client.StringSetAsync(key, "v");
                for (int i = 0; i < 5; i++)
                {
                    await Client.StringGetAsync(key);
                }
                var top = await Client.HotKeysTopAsync(5);
                Assert.NotNull(top);
                await Client.HotKeysResetAsync();
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
