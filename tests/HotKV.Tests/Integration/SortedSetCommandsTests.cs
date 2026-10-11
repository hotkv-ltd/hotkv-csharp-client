// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Linq;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class SortedSetCommandsTests : IntegrationTestBase
    {
        public SortedSetCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task AddScoreCardRange()
        {
            RequireServer();
            string key = UniqueKey("zset-basic");
            try
            {
                Assert.Equal(3, await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) }));
                Assert.Equal(3, await Client.SortedSetLengthAsync(key));
                Assert.Equal(2.0, await Client.SortedSetScoreAsync(key, "b"));
                Assert.Null(await Client.SortedSetScoreAsync(key, "zzz"));

                var range = await Client.SortedSetRangeByRankAsync(key, 0, -1);
                Assert.Equal(new[] { "a", "b", "c" }, range);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ZAddNxXxGtLtCh()
        {
            RequireServer();
            string key = UniqueKey("zset-conditions");
            try
            {
                await Client.SortedSetAddAsync(key, "a", 5);

                // NX: should not touch an existing member.
                await Client.SortedSetAddAsync(key, "a", 100, new ZAddOptions { Condition = ZAddCondition.OnlyIfNew });
                Assert.Equal(5.0, await Client.SortedSetScoreAsync(key, "a"));

                // XX: only update, never add a brand new member.
                long added = await Client.SortedSetAddAsync(key, "brand-new", 1, new ZAddOptions { Condition = ZAddCondition.OnlyIfExists });
                Assert.Equal(0, added);
                Assert.Null(await Client.SortedSetScoreAsync(key, "brand-new"));

                // GT: only update when the new score is greater.
                await Client.SortedSetAddAsync(key, "a", 3, new ZAddOptions { Condition = ZAddCondition.OnlyIfGreater });
                Assert.Equal(5.0, await Client.SortedSetScoreAsync(key, "a"));
                await Client.SortedSetAddAsync(key, "a", 10, new ZAddOptions { Condition = ZAddCondition.OnlyIfGreater });
                Assert.Equal(10.0, await Client.SortedSetScoreAsync(key, "a"));

                // CH: reports members changed, not just added.
                long changed = await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 20), ("brand-new", 1) }, new ZAddOptions { ReturnChanged = true });
                Assert.Equal(2, changed);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ZAddIncr()
        {
            RequireServer();
            string key = UniqueKey("zset-incr");
            try
            {
                double? result = await Client.SortedSetIncrementAsync(key, "a", 5);
                Assert.Equal(5.0, result);
                result = await Client.SortedSetIncrementAsync(key, "a", 2.5);
                Assert.Equal(7.5, result);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RangeByScoreAndByLex()
        {
            RequireServer();
            string key = UniqueKey("zset-byscore");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3), ("d", 4) });
                var byScore = await Client.SortedSetRangeAsync(key, "2", "3", SortedSetRangeBy.Score);
                Assert.Equal(new[] { "b", "c" }, byScore);

                var exclusive = await Client.SortedSetRangeAsync(key, "(2", "4", SortedSetRangeBy.Score);
                Assert.Equal(new[] { "c", "d" }, exclusive);

                string lexKey = UniqueKey("zset-bylex");
                await Client.SortedSetAddAsync(lexKey, new (string, double)[] { ("a", 0), ("b", 0), ("c", 0) });
                var byLex = await Client.SortedSetRangeAsync(lexKey, "[a", "[b", SortedSetRangeBy.Lex);
                Assert.Equal(new[] { "a", "b" }, byLex);
                await Client.DeleteAsync(lexKey);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RangeReverse()
        {
            RequireServer();
            string key = UniqueKey("zset-rev");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                var rev = await Client.SortedSetRangeAsync(key, "0", "-1", SortedSetRangeBy.Rank, reverse: true);
                Assert.Equal(new[] { "c", "b", "a" }, rev);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RangeByScoreReverseWithLimit()
        {
            RequireServer();
            string key = UniqueKey("zset-byscore-rev-limit");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3), ("d", 4) });
                var page = await Client.SortedSetRangeAsync(key, "+inf", "-inf", SortedSetRangeBy.Score, reverse: true, limit: (1, 2));
                Assert.Equal(new[] { "c", "b" }, page);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RangeStoreByScoreAndByLex()
        {
            RequireServer();
            string src = UniqueKey("zrangestore-src");
            string dst = UniqueKey("zrangestore-dst");
            string lex = UniqueKey("zrangestore-lex");
            try
            {
                await Client.SortedSetAddAsync(src, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3), ("d", 4) });

                long stored = await Client.SortedSetRangeStoreAsync(dst, src, "2", "3", SortedSetRangeBy.Score);
                Assert.Equal(2, stored);
                Assert.Equal(new[] { "b", "c" }, await Client.SortedSetRangeAsync(dst, "0", "-1"));

                stored = await Client.SortedSetRangeStoreAsync(dst, src, "4", "1", SortedSetRangeBy.Score, reverse: true, limit: (0, 2));
                Assert.Equal(2, stored);
                Assert.Equal(new[] { "c", "d" }, await Client.SortedSetRangeAsync(dst, "0", "-1"));

                await Client.SortedSetAddAsync(lex, new (string, double)[] { ("a", 0), ("b", 0), ("c", 0) });
                stored = await Client.SortedSetRangeStoreAsync(dst, lex, "-", "+", SortedSetRangeBy.Lex);
                Assert.Equal(3, stored);
                Assert.Equal(new[] { "a", "b", "c" }, await Client.SortedSetRangeAsync(dst, "0", "-1"));
            }
            finally
            {
                await Client.DeleteAsync(src);
                await Client.DeleteAsync(dst);
                await Client.DeleteAsync(lex);
            }
        }

        [SkippableFact]
        public async Task RankAndRevRankWithScore()
        {
            RequireServer();
            string key = UniqueKey("zset-rank");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                Assert.Equal(0, await Client.SortedSetRankAsync(key, "a"));
                Assert.Equal(2, await Client.SortedSetRevRankAsync(key, "a"));
                Assert.Null(await Client.SortedSetRankAsync(key, "zzz"));

                var withScore = await Client.SortedSetRankWithScoreAsync(key, "b");
                Assert.Equal(1, withScore!.Value.Rank);
                Assert.Equal(2.0, withScore.Value.Score);

                var revWithScore = await Client.SortedSetRevRankWithScoreAsync(key, "b");
                Assert.Equal(1, revWithScore!.Value.Rank);
                Assert.Equal(2.0, revWithScore.Value.Score);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task CountAndLexCount()
        {
            RequireServer();
            string key = UniqueKey("zset-count");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                Assert.Equal(2, await Client.SortedSetCountAsync(key, "2", "3"));

                string lexKey = UniqueKey("zset-lexcount");
                await Client.SortedSetAddAsync(lexKey, new (string, double)[] { ("a", 0), ("b", 0), ("c", 0) });
                Assert.Equal(3, await Client.SortedSetLexCountAsync(lexKey, "-", "+"));
                await Client.DeleteAsync(lexKey);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task RemoveRangeVariants()
        {
            RequireServer();
            string key = UniqueKey("zset-remrange");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3), ("d", 4) });
                Assert.Equal(1, await Client.SortedSetRemoveRangeByRankAsync(key, 0, 0));
                Assert.Equal(1, await Client.SortedSetRemoveRangeByScoreAsync(key, "4", "4"));
                Assert.Equal(new[] { "b", "c" }, await Client.SortedSetRangeByRankAsync(key, 0, -1));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task PopMinAndMaxWithCount()
        {
            RequireServer();
            string key = UniqueKey("zset-pop");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                var min = await Client.SortedSetPopMinAsync(key, 2);
                Assert.Equal(new[] { "a", "b" }, min.Select(x => x.Member));
                var max = await Client.SortedSetPopMaxAsync(key);
                Assert.Equal("c", max[0].Member);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task UnionInterStoreWithWeightsAndAggregate()
        {
            RequireServer();
            string a = UniqueKey("zset-u-a");
            string b = UniqueKey("zset-u-b");
            string dest = UniqueKey("zset-u-dest");
            try
            {
                await Client.SortedSetAddAsync(a, new (string, double)[] { ("x", 1), ("y", 2) });
                await Client.SortedSetAddAsync(b, new (string, double)[] { ("x", 10) });

                long stored = await Client.SortedSetUnionStoreAsync(dest, new[] { a, b }, weights: new double[] { 1, 2 }, aggregate: SortedSetAggregate.Sum);
                Assert.Equal(2, stored);
                Assert.Equal(21.0, await Client.SortedSetScoreAsync(dest, "x")); // 1*1 + 10*2

                long interStored = await Client.SortedSetInterStoreAsync(dest, new[] { a, b }, aggregate: SortedSetAggregate.Max);
                Assert.Equal(1, interStored);
                Assert.Equal(10.0, await Client.SortedSetScoreAsync(dest, "x"));
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b, dest });
            }
        }

        [SkippableFact]
        public async Task UnionInterDiffLiveQueries()
        {
            RequireServer();
            string a = UniqueKey("zset-q-a");
            string b = UniqueKey("zset-q-b");
            try
            {
                await Client.SortedSetAddAsync(a, new (string, double)[] { ("x", 1), ("y", 2) });
                await Client.SortedSetAddAsync(b, new (string, double)[] { ("x", 1) });

                var union = await Client.SortedSetUnionAsync(new[] { a, b });
                Assert.Equal(2, union.Count);

                var inter = await Client.SortedSetInterAsync(new[] { a, b });
                Assert.Single(inter);
                Assert.Equal("x", inter[0].Member);

                var diff = await Client.SortedSetDiffAsync(new[] { a, b });
                Assert.Single(diff);
                Assert.Equal("y", diff[0].Member);
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b });
            }
        }

        [SkippableFact]
        public async Task RandomMember()
        {
            RequireServer();
            string key = UniqueKey("zset-random");
            try
            {
                await Client.SortedSetAddAsync(key, new (string, double)[] { ("a", 1), ("b", 2), ("c", 3) });
                var one = await Client.SortedSetRandomMemberAsync(key);
                Assert.Single(one);
                var many = await Client.SortedSetRandomMemberAsync(key, 2);
                Assert.Equal(2, many.Count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
