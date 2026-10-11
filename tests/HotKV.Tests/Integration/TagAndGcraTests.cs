// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Linq;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class TagCommandsTests : IntegrationTestBase
    {
        public TagCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task SetGetRemExistsCount()
        {
            RequireServer();
            string key = UniqueKey("tag-key");
            try
            {
                await Client.StringSetAsync(key, "v");
                Assert.Equal(2, await Client.Tags.SetAsync(key, new[] { "red", "urgent" }));
                Assert.True(await Client.Tags.ExistsAsync(key, "red"));
                Assert.Equal(2, await Client.Tags.CountAsync(key));

                var tags = await Client.Tags.GetAsync(key);
                Assert.Equal(new[] { "red", "urgent" }, tags.OrderBy(t => t));

                Assert.Equal(1, await Client.Tags.RemoveAsync(key, new[] { "red" }));
                Assert.False(await Client.Tags.ExistsAsync(key, "red"));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task MembersAndIntersect()
        {
            RequireServer();
            string keyA = UniqueKey("tag-a");
            string keyB = UniqueKey("tag-b");
            // Tag values are a global namespace on the server (not scoped by
            // key prefix), so derive them from the same unique run id too;
            // otherwise a fixed name like "shared" would accumulate members
            // left behind by every previous run against this server.
            string shared = UniqueKey("shared-tag");
            string onlyA = UniqueKey("onlyA-tag");
            string onlyB = UniqueKey("onlyB-tag");
            try
            {
                await Client.StringSetAsync(keyA, "v");
                await Client.StringSetAsync(keyB, "v");
                await Client.Tags.SetAsync(keyA, new[] { shared, onlyA });
                await Client.Tags.SetAsync(keyB, new[] { shared, onlyB });

                var sharedMembers = await Client.Tags.MembersAsync(shared);
                Assert.Equal(new[] { keyA, keyB }, sharedMembers.OrderBy(x => x));

                var intersection = await Client.Tags.IntersectAsync(new[] { shared, onlyA });
                Assert.Equal(new[] { keyA }, intersection);
            }
            finally
            {
                await Client.Tags.RemoveAsync(keyA, new[] { shared, onlyA });
                await Client.Tags.RemoveAsync(keyB, new[] { shared, onlyB });
                await Client.DeleteAsync(new[] { keyA, keyB });
            }
        }

        [SkippableFact]
        public async Task SetOnMissingKeyErrors()
        {
            RequireServer();
            await Assert.ThrowsAsync<HotKVServerException>(() => Client.Tags.SetAsync(UniqueKey("tag-missing"), "x"));
        }
    }

    public class GcraCommandsTests : IntegrationTestBase
    {
        public GcraCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task LimitAllowsThenDeniesBurst()
        {
            RequireServer();
            string key = UniqueKey("gcra");
            try
            {
                var first = await Client.Gcra.LimitAsync(key, periodMs: 60_000, burst: 2);
                Assert.False(first.Denied);
                Assert.Equal(-1, first.RetryAfterMs);

                await Client.Gcra.LimitAsync(key, periodMs: 60_000, burst: 2);
                var third = await Client.Gcra.LimitAsync(key, periodMs: 60_000, burst: 2);
                Assert.True(third.Denied);
                Assert.True(third.RetryAfterMs >= 0);
            }
            finally
            {
                await Client.Gcra.ResetAsync(key);
            }
        }

        [SkippableFact]
        public async Task ResetClearsState()
        {
            RequireServer();
            string key = UniqueKey("gcra-reset");
            await Client.Gcra.LimitAsync(key, periodMs: 60_000, burst: 1);
            Assert.Equal(1, await Client.Gcra.ResetAsync(key));
            Assert.Equal(0, await Client.Gcra.ResetAsync(key));
        }
    }
}
