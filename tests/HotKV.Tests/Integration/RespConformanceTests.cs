// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Checks that HGETALL, HRANDFIELD WITHVALUES, SMEMBERS, ZSCORE, ZRANGE WITHSCORES,
    /// ZPOPMIN with a count, ZRANDMEMBER WITHSCORES, XREAD, XINFO STREAM, CONFIG GET and INFO
    /// decode to the same shape whether the connection negotiated RESP3 (the default) or RESP2.
    /// </summary>
    [Collection("HotKV Server")]
    public class RespConformanceTests : IntegrationTestBase
    {
        public RespConformanceTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task Resp3() => await RunChecksAsync(preferResp3: true, "resp3");

        [SkippableFact]
        public async Task Resp2() => await RunChecksAsync(preferResp3: false, "resp2");

        private async Task RunChecksAsync(bool preferResp3, string label)
        {
            RequireServer();
            var options = HotKVClientOptions.Parse(Fixture.Url!);
            options.PreferResp3 = preferResp3;
            await using var client = await HotKVClient.ConnectAsync(options);

            string hkey = UniqueKey($"conform-{label}-hash");
            string skey = UniqueKey($"conform-{label}-set");
            string zkey = UniqueKey($"conform-{label}-zset");
            string xkey = UniqueKey($"conform-{label}-stream");
            try
            {
                await client.HashSetAsync(hkey, new Dictionary<string, string> { ["f1"] = "v1", ["f2"] = "v2" });
                var hgetAll = await client.HashGetAllAsync(hkey);
                Assert.Equal(2, hgetAll.Count);
                Assert.Equal("v1", hgetAll["f1"]);
                Assert.Equal("v2", hgetAll["f2"]);

                var fieldValues = await client.HashRandomFieldWithValuesAsync(hkey, 2);
                Assert.Equal(2, fieldValues.Count);
                foreach (var (field, value) in fieldValues)
                {
                    Assert.Equal(field == "f1" ? "v1" : "v2", value);
                }

                await client.SetAddAsync(skey, new[] { "a", "b", "c" });
                var members = await client.SetMembersAsync(skey);
                Assert.Equal(new HashSet<string> { "a", "b", "c" }, new HashSet<string>(members));

                await client.SortedSetAddAsync(zkey, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                Assert.Equal(2.0, await client.SortedSetScoreAsync(zkey, "b"));

                var withScores = await client.SortedSetRangeWithScoresAsync(zkey, "0", "-1");
                Assert.Equal(
                    new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) },
                    withScores.Select(p => (p.Member, p.Score)));

                var popped = await client.SortedSetPopMinAsync(zkey, 2);
                Assert.Equal(new (string, double)[] { ("a", 1), ("b", 2) }, popped.Select(p => (p.Member, p.Score)));

                await client.SortedSetAddAsync(zkey, new (string, double)[] { ("a", 1), ("b", 2) });
                var randScored = await client.SortedSetRandomMemberWithScoresAsync(zkey, 3);
                Assert.Equal(3, randScored.Count);

                string id1 = await client.StreamAddAsync(xkey, new Dictionary<string, string> { ["field1"] = "v1" });
                string id2 = await client.StreamAddAsync(xkey, new Dictionary<string, string> { ["field2"] = "v2" });
                var streams = await client.StreamReadAsync(new Dictionary<string, string> { [xkey] = "0" });
                Assert.Single(streams);
                var entries = streams[xkey];
                Assert.Equal(2, entries.Count);
                Assert.Equal(id1, entries[0].Id);
                Assert.Equal("v1", entries[0].Fields["field1"]);
                Assert.Equal(id2, entries[1].Id);

                var info = await client.StreamInfoAsync(xkey);
                Assert.Equal(2L, info["length"].AsInt64());
                Assert.Equal(id2, info["last-generated-id"].AsString());

                var cfg = await client.ConfigGetAsync("maxmemory");
                Assert.True(cfg.ContainsKey("maxmemory"));

                string infoStr = await client.InfoAsync();
                Assert.False(infoStr.StartsWith("txt:") || infoStr.StartsWith("mkd:"),
                    "INFO leaked a verbatim string encoding prefix");
                Assert.Contains("hotkv_version", infoStr);
            }
            finally
            {
                await client.DeleteAsync(hkey);
                await client.DeleteAsync(skey);
                await client.DeleteAsync(zkey);
                await client.DeleteAsync(xkey);
            }
        }
    }
}
