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
    public class StreamCommandsTests : IntegrationTestBase
    {
        public StreamCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task AddRangeLenDelete()
        {
            RequireServer();
            string key = UniqueKey("stream-basic");
            try
            {
                string id1 = await Client.StreamAddAsync(key, new Dictionary<string, string> { ["field1"] = "value1" });
                string id2 = await Client.StreamAddAsync(key, new Dictionary<string, string> { ["field1"] = "value2" });
                Assert.Equal(2, await Client.StreamLengthAsync(key));

                var range = await Client.StreamRangeAsync(key, "-", "+");
                Assert.Equal(2, range.Count);
                Assert.Equal(id1, range[0].Id);
                Assert.Equal("value1", range[0].Fields["field1"]);

                var rev = await Client.StreamRevRangeAsync(key, "+", "-");
                Assert.Equal(id2, rev[0].Id);

                Assert.Equal(1, await Client.StreamDeleteAsync(key, new[] { id1 }));
                Assert.Equal(1, await Client.StreamLengthAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ReadAndTrim()
        {
            RequireServer();
            string key = UniqueKey("stream-read");
            try
            {
                await Client.StreamAddAsync(key, new Dictionary<string, string> { ["a"] = "1" });
                var result = await Client.StreamReadAsync(new Dictionary<string, string> { [key] = "0" });
                Assert.True(result.ContainsKey(key));
                Assert.Single(result[key]);

                for (int i = 0; i < 5; i++)
                {
                    await Client.StreamAddAsync(key, new Dictionary<string, string> { ["n"] = i.ToString() });
                }
                long removed = await Client.StreamTrimAsync(key, StreamTrimStrategy.MaxLen, "2");
                Assert.True(removed >= 0);
                Assert.Equal(2, await Client.StreamLengthAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ConsumerGroupsAckPendingClaim()
        {
            RequireServer();
            string key = UniqueKey("stream-group");
            string group = "g1";
            string consumerA = "consumerA";
            string consumerB = "consumerB";
            try
            {
                await Client.StreamAddAsync(key, new Dictionary<string, string> { ["a"] = "1" }, options: new StreamAddOptions { NoMkStream = false });
                await Client.StreamGroupCreateAsync(key, group, "0");

                var delivered = await Client.StreamReadGroupAsync(group, consumerA, new Dictionary<string, string> { [key] = ">" });
                Assert.Single(delivered[key]);
                string entryId = delivered[key][0].Id;

                var summary = await Client.StreamPendingSummaryAsync(key, group);
                Assert.Equal(1, summary.Count);

                var extended = await Client.StreamPendingRangeAsync(key, group, "-", "+", 10);
                Assert.Single(extended);
                Assert.Equal(entryId, extended[0].Id);

                var claimed = await Client.StreamClaimAsync(key, group, consumerB, TimeSpan.Zero, new[] { entryId });
                Assert.Single(claimed);

                Assert.Equal(1, await Client.StreamAckAsync(key, group, new[] { entryId }));

                var groups = await Client.StreamInfoGroupsAsync(key);
                Assert.Single(groups);

                await Client.StreamGroupCreateConsumerAsync(key, group, "consumerC");
                await Client.StreamGroupDeleteConsumerAsync(key, group, "consumerC");
                Assert.True(await Client.StreamGroupDestroyAsync(key, group));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task AutoClaim()
        {
            RequireServer();
            string key = UniqueKey("stream-autoclaim");
            string group = "g-auto";
            try
            {
                await Client.StreamAddAsync(key, new Dictionary<string, string> { ["a"] = "1" });
                await Client.StreamGroupCreateAsync(key, group, "0");
                await Client.StreamReadGroupAsync(group, "c1", new Dictionary<string, string> { [key] = ">" });

                var (next, entries, deleted) = await Client.StreamAutoClaimAsync(key, group, "c2", TimeSpan.Zero, "0-0");
                Assert.NotNull(next);
                Assert.Single(entries);
                Assert.Empty(deleted);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task InfoStream()
        {
            RequireServer();
            string key = UniqueKey("stream-info");
            try
            {
                await Client.StreamAddAsync(key, new Dictionary<string, string> { ["a"] = "1" });
                var info = await Client.StreamInfoAsync(key);
                Assert.True(info.ContainsKey("length") || info.Count > 0);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
