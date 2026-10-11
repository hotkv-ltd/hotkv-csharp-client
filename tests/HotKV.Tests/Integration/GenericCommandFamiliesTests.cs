// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// The command families the SDK has no typed wrappers for (probabilistic structures, the newer
    /// hash/string additions, CLIENT TRACKING) are reachable through the generic
    /// <see cref="HotKVClient.CommandAsync(string, object?[])"/> method. Each test drives the family against the
    /// live server and checks the reply values.
    /// </summary>
    public class GenericCommandFamiliesTests : IntegrationTestBase
    {
        public GenericCommandFamiliesTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task BloomFilterThroughGenericCommand()
        {
            RequireServer();
            string key = UniqueKey("bf");
            try
            {
                Assert.Equal("OK", (await Client.CommandAsync("BF.RESERVE", key, "0.01", 100)).AsString());
                Assert.Equal(1, (await Client.CommandAsync("BF.ADD", key, "a")).AsInt64());
                Assert.Equal(0, (await Client.CommandAsync("BF.ADD", key, "a")).AsInt64());
                Assert.Equal(1, (await Client.CommandAsync("BF.EXISTS", key, "a")).AsInt64());
                var madd = (await Client.CommandAsync("BF.MADD", key, "b", "c")).AsArray();
                Assert.Equal(new long[] { 1, 1 }, madd.Select(v => v.AsInt64()).ToArray());
                var mex = (await Client.CommandAsync("BF.MEXISTS", key, "a", "zzz")).AsArray();
                Assert.Equal(new long[] { 1, 0 }, mex.Select(v => v.AsInt64()).ToArray());
                Assert.Equal(3, (await Client.CommandAsync("BF.CARD", key)).AsInt64());
                var info = (await Client.CommandAsync("BF.INFO", key)).AsArray().Select(v => v.AsString()).ToArray();
                Assert.Equal("Capacity", info[0]);
                Assert.Equal("100", info[1]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task CountMinSketchAndTopKThroughGenericCommand()
        {
            RequireServer();
            string cms = UniqueKey("cms");
            string topk = UniqueKey("topk");
            try
            {
                Assert.Equal("OK", (await Client.CommandAsync("CMS.INITBYDIM", cms, 100, 5)).AsString());
                var incr = (await Client.CommandAsync("CMS.INCRBY", cms, "a", 3, "b", 2)).AsArray();
                Assert.Equal(new long[] { 3, 2 }, incr.Select(v => v.AsInt64()).ToArray());
                var q = (await Client.CommandAsync("CMS.QUERY", cms, "a", "b", "never")).AsArray();
                Assert.Equal(new long[] { 3, 2, 0 }, q.Select(v => v.AsInt64()).ToArray());

                Assert.Equal("OK", (await Client.CommandAsync("TOPK.RESERVE", topk, 3)).AsString());
                await Client.CommandAsync("TOPK.ADD", topk, "a", "a", "b", "c");
                var present = (await Client.CommandAsync("TOPK.QUERY", topk, "a", "zzz")).AsArray();
                Assert.Equal(new long[] { 1, 0 }, present.Select(v => v.AsInt64()).ToArray());
                var list = (await Client.CommandAsync("TOPK.LIST", topk)).AsArray().Select(v => v.AsString()).ToArray();
                Assert.Equal("a", list[0]);
                Assert.Equal(3, list.Length);
            }
            finally
            {
                await Client.DeleteAsync(new[] { cms, topk });
            }
        }

        [SkippableFact]
        public async Task NewerHashAndStringAdditionsThroughGenericCommand()
        {
            RequireServer();
            string h = UniqueKey("r8-hash");
            string s1 = UniqueKey("r8-s1");
            string s2 = UniqueKey("r8-s2");
            string d = UniqueKey("r8-delex");
            try
            {
                await Client.HashSetAsync(h, new System.Collections.Generic.Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3" });

                // HGETDEL key FIELDS n f... -> values (nil for absent fields), fields removed.
                var del = (await Client.CommandAsync("HGETDEL", h, "FIELDS", 2, "a", "nope")).AsArray();
                Assert.Equal("1", del[0].AsString());
                Assert.True(del[1].IsNull);
                Assert.False(await Client.HashExistsAsync(h, "a"));

                // HGETEX key EX s FIELDS n f... -> values, and the field TTL is set.
                var getex = (await Client.CommandAsync("HGETEX", h, "EX", 100, "FIELDS", 1, "b")).AsArray();
                Assert.Equal("2", getex[0].AsString());
                Assert.InRange((await Client.HashFieldTtlAsync(h, new[] { "b" }))[0], 90, 100);

                // HSETEX key EX s FIELDS n f v... -> 1 when all set.
                Assert.Equal(1, (await Client.CommandAsync("HSETEX", h, "EX", 50, "FIELDS", 1, "d", "4")).AsInt64());
                Assert.Equal("4", await Client.HashGetAsync(h, "d"));
                Assert.InRange((await Client.HashFieldTtlAsync(h, new[] { "d" }))[0], 40, 50);

                // MSETEX numkeys k v... EX s.
                Assert.Equal(1, (await Client.CommandAsync("MSETEX", 2, s1, "v1", s2, "v2", "EX", 100)).AsInt64());
                Assert.Equal("v2", await Client.StringGetAsync(s2));
                Assert.InRange(await Client.TtlAsync(s1), 90, 100);

                // DELEX key IFEQ value.
                await Client.StringSetAsync(d, "keep");
                Assert.Equal(0, (await Client.CommandAsync("DELEX", d, "IFEQ", "other")).AsInt64());
                Assert.True(await Client.ExistsAsync(d));
                Assert.Equal(1, (await Client.CommandAsync("DELEX", d, "IFEQ", "keep")).AsInt64());
                Assert.False(await Client.ExistsAsync(d));
            }
            finally
            {
                await Client.DeleteAsync(new[] { h, s1, s2, d });
            }
        }

        [SkippableFact]
        public async Task ClientTrackingFamilyThroughGenericCommandOnOneConnection()
        {
            RequireServer();
            // CLIENT TRACKING state is per connection, so pin a single connection.
            var options = HotKVClientOptions.Parse(Fixture.Url!);
            options.PreferResp3 = false;
            options.MaxPoolSize = 1;
            await using var single = await HotKVClient.ConnectAsync(options);

            var before = (await single.CommandAsync("CLIENT", "TRACKINGINFO")).AsArray();
            Assert.Equal("flags", before[0].AsString());
            Assert.Equal("off", before[1].AsArray()[0].AsString());

            Assert.Equal("OK", (await single.CommandAsync("CLIENT", "TRACKING", "on")).AsString());
            var on = (await single.CommandAsync("CLIENT", "TRACKINGINFO")).AsArray();
            Assert.Equal("on", on[1].AsArray()[0].AsString());
            // tracking on without REDIRECT replies 0 (standard semantics); -1 means tracking is off.
            Assert.Equal(0, (await single.CommandAsync("CLIENT", "GETREDIR")).AsInt64());

            // CLIENT CACHING is only valid in OPTIN/OPTOUT mode: a typed server error surfaces as an exception.
            var ex = await Assert.ThrowsAsync<HotKVServerException>(async () => await single.CommandAsync("CLIENT", "CACHING", "yes"));
            Assert.Equal("ERR", ex.Prefix);

            Assert.Equal("OK", (await single.CommandAsync("CLIENT", "TRACKING", "off")).AsString());
            Assert.Equal(-1, (await single.CommandAsync("CLIENT", "GETREDIR")).AsInt64());
        }

        [SkippableFact]
        public async Task ClusterOnlyCommandsReturnTheServersErrorOnAStandaloneInstance()
        {
            RequireServer();
            // SFLUSH / CLUSTERSCAN / TRIMSLOTS are cluster-mode commands; the generic method must surface the server's reply faithfully.
            var sflush = await Assert.ThrowsAsync<HotKVServerException>(async () => await Client.CommandAsync("SFLUSH", 0, 100));
            Assert.Contains("cluster support disabled", sflush.RawMessage);
            var scan = await Assert.ThrowsAsync<HotKVServerException>(async () => await Client.CommandAsync("CLUSTERSCAN", 0));
            Assert.Contains("cluster support disabled", scan.RawMessage);
        }

        [SkippableFact]
        public async Task FeatureSetAtThroughGenericCommandMatchesTheTypedMethod()
        {
            RequireServer();
            string group = UniqueKey("feature-setat");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.FeatureStore.CreateAsync(group, "history", new (string, FeatureType)[] { ("score", FeatureType.Int64) });
                    long at = System.DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
                    await Client.CommandAsync("FEATURE.SETAT", group, "u1", at, "score", "1");
                    await Client.FeatureStore.SetAtAsync(group, "u2", System.DateTimeOffset.FromUnixTimeMilliseconds(at), new System.Collections.Generic.Dictionary<string, string> { ["score"] = "2" });
                    var r1 = await Client.CommandAsync("FEATURE.GET", group, "u1");
                    Assert.Equal(at, r1.AsArray()[1].AsInt64());
                    var r2 = await Client.FeatureStore.GetAsync(group, "u2");
                    Assert.Equal("2", r2!.Values["score"]);
                    Assert.Equal(at, r2.UpdatedAtMs);
                    var bad = await Assert.ThrowsAsync<HotKVServerException>(async () => await Client.CommandAsync("FEATURE.SETAT", group, "u3", -5, "score", "1"));
                    Assert.Contains("invalid timestamp", bad.RawMessage);
                }
                finally
                {
                    await Client.FeatureStore.DropAsync(group);
                }
            });
        }

        [SkippableFact]
        public async Task BinarySafeValuesRoundTripThroughTypedAndGenericMethods()
        {
            RequireServer();
            string key = UniqueKey("binary");
            try
            {
                var bytes = Enumerable.Range(0, 256).Select(i => (byte)i).Concat(Encoding.UTF8.GetBytes("\r\n$5\r\nhello\r\n*1\r\n")).ToArray();
                await Client.CommandAsync("SET", key, bytes);
                var back = (await Client.CommandAsync("GET", key)).AsBytes();
                Assert.Equal(bytes, back);
                Assert.Equal(bytes, await Client.StringGetBytesAsync(key));
                Assert.Equal(bytes.Length, (await Client.CommandAsync("STRLEN", key)).AsInt64());
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
