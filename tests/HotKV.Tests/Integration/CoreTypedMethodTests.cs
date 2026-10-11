// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Live checks of the core typed methods that no other integration class calls, each asserting
    /// the reply value (not just "no exception"). Together with the per-family classes this makes
    /// every typed core method exercised against a real server.
    /// </summary>
    public class CoreTypedMethodTests : IntegrationTestBase
    {
        public CoreTypedMethodTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task StringSetNxSetExPsetexGetSet()
        {
            RequireServer();
            string k1 = UniqueKey("str-setnx");
            string k2 = UniqueKey("str-setex");
            string k3 = UniqueKey("str-psetex");
            string k4 = UniqueKey("str-getset");
            try
            {
                Assert.True(await Client.StringSetIfNotExistsAsync(k1, "first"));
                Assert.False(await Client.StringSetIfNotExistsAsync(k1, "second"));
                Assert.Equal("first", await Client.StringGetAsync(k1));

                await Client.StringSetExAsync(k2, TimeSpan.FromSeconds(120), "v2");
                Assert.Equal("v2", await Client.StringGetAsync(k2));
                Assert.InRange(await Client.TtlAsync(k2), 100, 120);

                await Client.StringSetExMillisecondsAsync(k3, TimeSpan.FromMilliseconds(90000), "v3");
                Assert.Equal("v3", await Client.StringGetAsync(k3));
                Assert.InRange(await Client.PTtlAsync(k3), 80000, 90000);

                Assert.Null(await Client.StringGetSetAsync(k4, "old"));
                Assert.Equal("old", await Client.StringGetSetAsync(k4, "new"));
                Assert.Equal("new", await Client.StringGetAsync(k4));
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2, k3, k4 });
            }
        }

        [SkippableFact]
        public async Task KeyExpireAtPExpireUnlinkRandomKey()
        {
            RequireServer();
            string k = UniqueKey("key-expireat");
            string k2 = UniqueKey("key-pexpire");
            string u1 = UniqueKey("key-unlink-1");
            string u2 = UniqueKey("key-unlink-2");
            try
            {
                await Client.StringSetAsync(k, "v");
                Assert.True(await Client.ExpireAtAsync(k, DateTimeOffset.UtcNow.AddSeconds(100)));
                Assert.InRange(await Client.TtlAsync(k), 90, 100);
                Assert.False(await Client.ExpireAtAsync(UniqueKey("absent"), DateTimeOffset.UtcNow.AddSeconds(100)));

                await Client.StringSetAsync(k2, "v");
                Assert.True(await Client.PExpireAsync(k2, TimeSpan.FromMilliseconds(50000)));
                Assert.InRange(await Client.PTtlAsync(k2), 40000, 50000);
                // NX: refuses because a TTL already exists.
                Assert.False(await Client.PExpireAsync(k2, TimeSpan.FromMilliseconds(70000), ExpireCondition.OnlyIfNoExpiry));

                await Client.StringSetAsync(u1, "a");
                await Client.StringSetAsync(u2, "b");
                Assert.Equal(2, await Client.UnlinkAsync(new[] { u1, u2, UniqueKey("unlink-absent") }));
                Assert.False(await Client.ExistsAsync(u1));

                // RANDOMKEY: with at least one key present it returns an existing key.
                string? rk = await Client.RandomKeyAsync();
                Assert.NotNull(rk);
                Assert.True(await Client.ExistsAsync(rk!));
            }
            finally
            {
                await Client.DeleteAsync(new[] { k, k2, u1, u2 });
            }
        }

        [SkippableFact]
        public async Task HashFieldExpiryVariants()
        {
            RequireServer();
            string key = UniqueKey("hash-fexp");
            try
            {
                await Client.HashSetAsync(key, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3", ["d"] = "4" });

                var px = await Client.HashFieldPExpireAsync(key, TimeSpan.FromMilliseconds(80000), new[] { "a" });
                Assert.Equal(new long[] { 1 }, px);
                var pttl = await Client.HashFieldPTtlAsync(key, new[] { "a", "b" });
                Assert.InRange(pttl[0], 60000, 80000);
                Assert.Equal(-1, pttl[1]);

                var at = await Client.HashFieldExpireAtAsync(key, DateTimeOffset.UtcNow.AddSeconds(200), new[] { "b" });
                Assert.Equal(new long[] { 1 }, at);
                Assert.InRange((await Client.HashFieldTtlAsync(key, new[] { "b" }))[0], 150, 200);

                var pat = await Client.HashFieldPExpireAtAsync(key, DateTimeOffset.UtcNow.AddSeconds(300), new[] { "c" });
                Assert.Equal(new long[] { 1 }, pat);
                Assert.InRange((await Client.HashFieldPTtlAsync(key, new[] { "c" }))[0], 250000, 300000);

                // field that does not exist reports -2
                var missing = await Client.HashFieldPExpireAsync(key, TimeSpan.FromMilliseconds(1000), new[] { "zzz" });
                Assert.Equal(new long[] { -2 }, missing);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ListRightPushIfExists()
        {
            RequireServer();
            string key = UniqueKey("list-rpushx");
            string absent = UniqueKey("list-rpushx-absent");
            try
            {
                Assert.Equal(0, await Client.ListRightPushIfExistsAsync(absent, new[] { "x" }));
                Assert.False(await Client.ExistsAsync(absent));
                await Client.ListRightPushAsync(key, new[] { "a" });
                Assert.Equal(3, await Client.ListRightPushIfExistsAsync(key, new[] { "b", "c" }));
                Assert.Equal(new[] { "a", "b", "c" }, await Client.ListRangeAsync(key, 0, -1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { key, absent });
            }
        }

        [SkippableFact]
        public async Task BlockingListPopsReturnImmediatelyWaitForPushAndTimeOut()
        {
            RequireServer();
            string k1 = UniqueKey("blpop-1");
            string k2 = UniqueKey("blpop-2");
            try
            {
                // Data already there: returned at once, from the first non-empty key.
                await Client.ListRightPushAsync(k2, new[] { "a", "b" });
                var left = await Client.ListBlockingLeftPopAsync(new[] { k1, k2 }, TimeSpan.FromSeconds(2));
                Assert.Equal((k2, "a"), left!.Value);
                var right = await Client.ListBlockingRightPopAsync(new[] { k1, k2 }, TimeSpan.FromSeconds(2));
                Assert.Equal((k2, "b"), right!.Value);

                // Timeout with nothing to pop: null after roughly the timeout (fractional seconds are sent as a plain decimal).
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Assert.Null(await Client.ListBlockingLeftPopAsync(new[] { k1 }, TimeSpan.FromMilliseconds(300)));
                Assert.Null(await Client.ListBlockingRightPopAsync(new[] { k1 }, TimeSpan.FromMilliseconds(300)));
                Assert.InRange(sw.ElapsedMilliseconds, 500, 5000);

                // A push from another task wakes a blocked pop.
                var blocked = Client.ListBlockingLeftPopAsync(new[] { k1 }, TimeSpan.FromSeconds(10));
                await Task.Delay(200);
                Assert.False(blocked.IsCompleted);
                await Client.ListLeftPushAsync(k1, new[] { "woken" });
                var woken = await blocked;
                Assert.Equal((k1, "woken"), woken!.Value);
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2 });
            }
        }

        [SkippableFact]
        public async Task SortedSetRemoveScoresIncrementByLexDiffStore()
        {
            RequireServer();
            string key = UniqueKey("zset-typed");
            string other = UniqueKey("zset-typed-other");
            string dest = UniqueKey("zset-typed-dest");
            string lex = UniqueKey("zset-typed-lex");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3), ("d", 4) });

                Assert.Equal(2, await Client.SortedSetRemoveAsync(key, new[] { "a", "zzz", "b" }));
                Assert.Equal(2, await Client.SortedSetLengthAsync(key));

                var scores = await Client.SortedSetScoresAsync(key, new[] { "c", "missing", "d" });
                Assert.Equal(new double?[] { 3.0, null, 4.0 }, scores);

                Assert.Equal(8.5, await Client.SortedSetIncrementByAsync(key, "c", 5.5));
                Assert.Equal(8.5, await Client.SortedSetScoreAsync(key, "c"));
                Assert.Equal(-1.0, await Client.SortedSetIncrementByAsync(key, "newbie", -1));

                // DIFFSTORE: members of key that are not in other.
                await Client.SortedSetAddAsync(other, new (string, double)[] { ("c", 1) });
                Assert.Equal(2, await Client.SortedSetDiffStoreAsync(dest, new[] { key, other }));
                Assert.Equal(new[] { "newbie", "d" }, await Client.SortedSetRangeByRankAsync(dest, 0, -1));

                // ZREMRANGEBYLEX on equal scores.
                await Client.SortedSetAddAsync(lex, new (string, double)[] { ("a", 0), ("b", 0), ("c", 0), ("d", 0), ("e", 0) });
                Assert.Equal(3, await Client.SortedSetRemoveRangeByLexAsync(lex, "[b", "[d"));
                Assert.Equal(new[] { "a", "e" }, await Client.SortedSetRangeByRankAsync(lex, 0, -1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { key, other, dest, lex });
            }
        }

        [SkippableFact]
        public async Task StreamSetIdClaimJustIdAndConsumersInfo()
        {
            RequireServer();
            string key = UniqueKey("stream-typed");
            try
            {
                string id1 = await Client.StreamAddAsync(key, new Dictionary<string, string> { ["f"] = "1" });
                string id2 = await Client.StreamAddAsync(key, new Dictionary<string, string> { ["f"] = "2" });
                await Client.StreamGroupCreateAsync(key, "g", "0");

                var read = await Client.StreamReadGroupAsync("g", "c1", new Dictionary<string, string> { [key] = ">" });
                Assert.Equal(2, read[key].Count);

                var consumers = await Client.StreamInfoConsumersAsync(key, "g");
                Assert.Single(consumers);
                Assert.Equal("c1", consumers[0]["name"].AsString());
                Assert.Equal(2, consumers[0]["pending"].AsInt64());

                // c2 takes both entries over (min idle 0), returning only their ids.
                var claimed = await Client.StreamClaimJustIdAsync(key, "g", "c2", TimeSpan.Zero, new[] { id1, id2 });
                Assert.Equal(new[] { id1, id2 }, claimed);
                var pending = await Client.StreamPendingRangeAsync(key, "g", "-", "+", 10, "c2");
                Assert.Equal(2, pending.Count);

                // XGROUP SETID rewinds the group so a new consumer sees both entries again.
                await Client.StreamGroupSetIdAsync(key, "g", "0");
                var again = await Client.StreamReadGroupAsync("g", "c3", new Dictionary<string, string> { [key] = ">" });
                Assert.Equal(2, again[key].Count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ScriptFlushForgetsLoadedScripts()
        {
            RequireServer();
            string script = "return 'flush-test-" + Guid.NewGuid().ToString("N") + "'";
            string sha = await Client.ScriptLoadAsync(script);
            Assert.True((await Client.ScriptExistsAsync(new[] { sha }))[0]);
            await Client.ScriptFlushAsync();
            Assert.False((await Client.ScriptExistsAsync(new[] { sha }))[0]);
            // EVALSHA-or-EVAL still works after the flush by falling back to EVAL.
            var r = await Client.EvalShaOrEvalAsync(script, Array.Empty<string>(), Array.Empty<string>());
            Assert.StartsWith("flush-test-", r.AsString());
        }

        [SkippableFact]
        public async Task ConfigSetRoundTripAndClientName()
        {
            RequireServer();
            var before = await Client.ConfigGetAsync("slowlog-max-len");
            Assert.True(before.ContainsKey("slowlog-max-len"));
            string old = before["slowlog-max-len"];
            string next = old == "200" ? "201" : "200";
            try
            {
                await Client.ConfigSetAsync("slowlog-max-len", next);
                Assert.Equal(next, (await Client.ConfigGetAsync("slowlog-max-len"))["slowlog-max-len"]);
            }
            finally
            {
                await Client.ConfigSetAsync("slowlog-max-len", old);
            }
            Assert.Equal(old, (await Client.ConfigGetAsync("slowlog-max-len"))["slowlog-max-len"]);

            // CLIENT GETNAME/SETNAME are connection scoped: a one-connection pool makes them deterministic.
            var options = HotKVClientOptions.Parse(Fixture.Url!);
            options.MaxPoolSize = 1;
            options.ClientName = "csharp-typed-test";
            await using var single = await HotKVClient.ConnectAsync(options);
            Assert.Equal("csharp-typed-test", await single.ClientGetNameAsync());
            await single.ClientSetNameAsync("renamed-client");
            Assert.Equal("renamed-client", await single.ClientGetNameAsync());
            Assert.True(await single.ClientIdAsync() > 0);
        }

        [SkippableFact]
        public async Task FlushDbEmptiesOnlyTheSelectedDatabase()
        {
            RequireServer();
            // Work on database 9 through a dedicated client so the shared database 0 is never touched.
            var options = HotKVClientOptions.Parse(Fixture.Url!);
            options.Database = 9;
            await using var db9 = await HotKVClient.ConnectAsync(options);
            string key = UniqueKey("flushdb");
            await db9.StringSetAsync(key, "v");
            await db9.StringSetAsync(key + "2", "v");
            Assert.True(await db9.DbSizeAsync() >= 2);
            await db9.FlushDbAsync();
            Assert.Equal(0, await db9.DbSizeAsync());
            await db9.StringSetAsync(key, "v");
            await db9.FlushDbAsync(async: true);
            // ASYNC flushing returns immediately; the database is empty once the call returned or momentarily after.
            for (int i = 0; i < 50 && await db9.DbSizeAsync() != 0; i++)
            {
                await Task.Delay(20);
            }
            Assert.Equal(0, await db9.DbSizeAsync());
        }
    }
}
