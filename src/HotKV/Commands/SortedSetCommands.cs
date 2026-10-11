// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;
using HotKV.Internal;

namespace HotKV.Commands
{
    /// <summary>Condition for ZADD via <see cref="SortedSetCommands"/>: NX/XX/GT/LT.</summary>
    public enum ZAddCondition
    {
        None,
        OnlyIfNew,
        OnlyIfExists,
        OnlyIfGreater,
        OnlyIfLess,
    }

    /// <summary>Options for ZADD.</summary>
    public sealed class ZAddOptions
    {
        public ZAddCondition Condition { get; set; } = ZAddCondition.None;

        /// <summary>CH: return the number of elements changed (added or whose score changed) instead of just added.</summary>
        public bool ReturnChanged { get; set; }
    }

    /// <summary>How ZRANGE (and friends) interpret <c>start</c>/<c>stop</c>.</summary>
    public enum SortedSetRangeBy
    {
        /// <summary>start/stop are 0-based ranks (may be negative, counting from the end).</summary>
        Rank,
        /// <summary>start/stop are scores; use "-inf"/"+inf" and a leading "(" for an exclusive bound.</summary>
        Score,
        /// <summary>start/stop are lexicographic bounds: "[value", "(value", "-" or "+".</summary>
        Lex,
    }

    /// <summary>Aggregation for ZUNIONSTORE/ZINTERSTORE/ZUNION/ZINTER.</summary>
    public enum SortedSetAggregate
    {
        Sum,
        Min,
        Max,
    }

    /// <summary>Typed wrappers for the sorted set command family, exposing the full modern ZADD/ZRANGE/ZRANK option surface per the command reference.</summary>
    public static class SortedSetCommands
    {
        private static void AppendWeightsAndAggregate(List<object?> args, IReadOnlyList<double>? weights, SortedSetAggregate? aggregate)
        {
            if (weights is { Count: > 0 })
            {
                args.Add("WEIGHTS");
                foreach (var w in weights)
                {
                    args.Add(NumberFormat.Score(w));
                }
            }
            if (aggregate.HasValue)
            {
                args.Add("AGGREGATE");
                args.Add(aggregate.Value.ToString().ToUpperInvariant());
            }
        }

        /// <summary>ZADD key [NX|XX|GT|LT] [CH] score member [score member ...] -> number added (or changed, with CH).</summary>
        public static async Task<long> SortedSetAddAsync(this ICommandExecutor executor, string key, IReadOnlyList<(string Member, double Score)> entries, ZAddOptions? options = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZADD", key };
            if (options is not null)
            {
                var flag = options.Condition switch
                {
                    ZAddCondition.OnlyIfNew => "NX",
                    ZAddCondition.OnlyIfExists => "XX",
                    ZAddCondition.OnlyIfGreater => "GT",
                    ZAddCondition.OnlyIfLess => "LT",
                    _ => null,
                };
                if (flag is not null)
                {
                    args.Add(flag);
                }
                if (options.ReturnChanged)
                {
                    args.Add("CH");
                }
            }
            foreach (var (member, score) in entries)
            {
                args.Add(NumberFormat.Score(score));
                args.Add(member);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZADD key member score.</summary>
        public static Task<long> SortedSetAddAsync(this ICommandExecutor executor, string key, string member, double score, ZAddOptions? options = null, CancellationToken cancellationToken = default) =>
            executor.SortedSetAddAsync(key, new[] { (member, score) }, options, cancellationToken);

        /// <summary>ZADD key [NX|XX|GT|LT] INCR increment member -> the new score, or null if a NX/XX/GT/LT condition blocked the update.</summary>
        public static async Task<double?> SortedSetIncrementAsync(this ICommandExecutor executor, string key, string member, double increment, ZAddCondition condition = ZAddCondition.None, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZADD", key };
            var flag = condition switch
            {
                ZAddCondition.OnlyIfNew => "NX",
                ZAddCondition.OnlyIfExists => "XX",
                ZAddCondition.OnlyIfGreater => "GT",
                ZAddCondition.OnlyIfLess => "LT",
                _ => null,
            };
            if (flag is not null)
            {
                args.Add(flag);
            }
            args.Add("INCR");
            args.Add(NumberFormat.Score(increment));
            args.Add(member);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNullableDouble();
        }

        /// <summary>ZREM key member [member ...] -> number removed.</summary>
        public static async Task<long> SortedSetRemoveAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "ZREM", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZSCORE key member.</summary>
        public static async Task<double?> SortedSetScoreAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZSCORE", key, member }, cancellationToken).ConfigureAwait(false);
            return reply.AsNullableDouble();
        }

        /// <summary>ZMSCORE key member [member ...].</summary>
        public static async Task<IReadOnlyList<double?>> SortedSetScoresAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "ZMSCORE", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsNullableDouble()).ToList();
        }

        /// <summary>ZCARD key.</summary>
        public static async Task<long> SortedSetLengthAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZCARD", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZCOUNT key min max (score bounds, "-inf"/"+inf" and a leading "(" for exclusive supported).</summary>
        public static async Task<long> SortedSetCountAsync(this ICommandExecutor executor, string key, string min, string max, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZCOUNT", key, min, max }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZLEXCOUNT key min max (lex bounds: "[value", "(value", "-", "+").</summary>
        public static async Task<long> SortedSetLexCountAsync(this ICommandExecutor executor, string key, string min, string max, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZLEXCOUNT", key, min, max }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZINCRBY key increment member -> the new score.</summary>
        public static async Task<double> SortedSetIncrementByAsync(this ICommandExecutor executor, string key, string member, double increment, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZINCRBY", key, NumberFormat.Score(increment), member }, cancellationToken).ConfigureAwait(false);
            return reply.AsDouble();
        }

        /// <summary>
        /// ZRANGE key start stop [BYSCORE|BYLEX] [REV] [LIMIT offset count]. For
        /// <see cref="SortedSetRangeBy.Rank"/>, start/stop are integer ranks
        /// (e.g. "0", "-1"); for Score/Lex they follow ZRANGEBYSCORE/ZRANGEBYLEX
        /// bound syntax. <paramref name="limit"/> requires BYSCORE or BYLEX.
        /// </summary>
        public static async Task<IReadOnlyList<string>> SortedSetRangeAsync(this ICommandExecutor executor, string key, string start, string stop, SortedSetRangeBy by = SortedSetRangeBy.Rank, bool reverse = false, (long Offset, long Count)? limit = null, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildRangeArgs(key, start, stop, by, reverse, limit, withScores: false), cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>ZRANGE ... WITHSCORES. See <see cref="SortedSetRangeAsync"/> for the parameters.</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetRangeWithScoresAsync(this ICommandExecutor executor, string key, string start, string stop, SortedSetRangeBy by = SortedSetRangeBy.Rank, bool reverse = false, (long Offset, long Count)? limit = null, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildRangeArgs(key, start, stop, by, reverse, limit, withScores: true), cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZRANGE key 0 -1 by rank; a convenience for the common integer-rank case.</summary>
        public static Task<IReadOnlyList<string>> SortedSetRangeByRankAsync(this ICommandExecutor executor, string key, long start, long stop, bool reverse = false, CancellationToken cancellationToken = default) =>
            executor.SortedSetRangeAsync(key, start.ToString(System.Globalization.CultureInfo.InvariantCulture), stop.ToString(System.Globalization.CultureInfo.InvariantCulture), SortedSetRangeBy.Rank, reverse, null, cancellationToken);

        private static List<object?> BuildRangeArgs(string key, string start, string stop, SortedSetRangeBy by, bool reverse, (long Offset, long Count)? limit, bool withScores)
        {
            var args = new List<object?> { "ZRANGE", key, start, stop };
            if (by == SortedSetRangeBy.Score)
            {
                args.Add("BYSCORE");
            }
            else if (by == SortedSetRangeBy.Lex)
            {
                args.Add("BYLEX");
            }
            if (reverse)
            {
                args.Add("REV");
            }
            if (limit.HasValue)
            {
                args.Add("LIMIT");
                args.Add(limit.Value.Offset);
                args.Add(limit.Value.Count);
            }
            if (withScores)
            {
                args.Add("WITHSCORES");
            }
            return args;
        }

        /// <summary>
        /// Converts a member/score list reply to (member, score) pairs.
        /// Under RESP3 the server upgrades these replies from a flat
        /// "member, score, member, score, ..." array to an array of
        /// 2-element [member, score] pairs (with score as a native RESP3
        /// double); RESP2 keeps the flat shape. Both are handled here so
        /// callers never see the difference.
        /// </summary>
        private static IReadOnlyList<(string Member, double Score)> PairsToScored(RespValue reply)
        {
            var result = new List<(string, double)>();
            foreach (var (member, score) in reply.AsPairedArray())
            {
                result.Add((member.AsString() ?? string.Empty, score.AsDouble()));
            }
            return result;
        }

        /// <summary>ZRANGESTORE destination source start stop [BYSCORE|BYLEX] [REV] [LIMIT offset count] -> size of the stored result.</summary>
        public static async Task<long> SortedSetRangeStoreAsync(this ICommandExecutor executor, string destination, string source, string start, string stop, SortedSetRangeBy by = SortedSetRangeBy.Rank, bool reverse = false, (long Offset, long Count)? limit = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZRANGESTORE", destination };
            args.AddRange(BuildRangeArgs(source, start, stop, by, reverse, limit, withScores: false).Skip(1));
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZRANK key member [WITHSCORE] -> 0-based rank (ascending), or null if the member does not exist.</summary>
        public static async Task<long?> SortedSetRankAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZRANK", key, member }, cancellationToken).ConfigureAwait(false);
            return reply.AsNullableInt64();
        }

        /// <summary>ZRANK key member WITHSCORE -> (rank, score), or null if the member does not exist.</summary>
        public static async Task<(long Rank, double Score)?> SortedSetRankWithScoreAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZRANK", key, member, "WITHSCORE" }, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return (items[0].AsInt64(), items[1].AsDouble());
        }

        /// <summary>ZREVRANK key member [WITHSCORE] -> 0-based rank (descending), or null if the member does not exist.</summary>
        public static async Task<long?> SortedSetRevRankAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZREVRANK", key, member }, cancellationToken).ConfigureAwait(false);
            return reply.AsNullableInt64();
        }

        /// <summary>ZREVRANK key member WITHSCORE -> (rank, score), or null if the member does not exist.</summary>
        public static async Task<(long Rank, double Score)?> SortedSetRevRankWithScoreAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZREVRANK", key, member, "WITHSCORE" }, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return (items[0].AsInt64(), items[1].AsDouble());
        }

        /// <summary>ZREMRANGEBYRANK key start stop -> number removed.</summary>
        public static async Task<long> SortedSetRemoveRangeByRankAsync(this ICommandExecutor executor, string key, long start, long stop, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZREMRANGEBYRANK", key, start, stop }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZREMRANGEBYSCORE key min max -> number removed.</summary>
        public static async Task<long> SortedSetRemoveRangeByScoreAsync(this ICommandExecutor executor, string key, string min, string max, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZREMRANGEBYSCORE", key, min, max }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZREMRANGEBYLEX key min max -> number removed.</summary>
        public static async Task<long> SortedSetRemoveRangeByLexAsync(this ICommandExecutor executor, string key, string min, string max, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZREMRANGEBYLEX", key, min, max }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZPOPMIN key [count].</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetPopMinAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "ZPOPMIN", key, count.Value } : new object?[] { "ZPOPMIN", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZPOPMAX key [count].</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetPopMaxAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "ZPOPMAX", key, count.Value } : new object?[] { "ZPOPMAX", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZRANDMEMBER key [count [WITHSCORES]].</summary>
        public static async Task<IReadOnlyList<string>> SortedSetRandomMemberAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "ZRANDMEMBER", key, count.Value } : new object?[] { "ZRANDMEMBER", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return Array.Empty<string>();
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>ZRANDMEMBER key count WITHSCORES.</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetRandomMemberWithScoresAsync(this ICommandExecutor executor, string key, int count, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ZRANDMEMBER", key, count, "WITHSCORES" }, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZUNIONSTORE destination numkeys key [key ...] [WEIGHTS w ...] [AGGREGATE SUM|MIN|MAX] -> size of the stored result.</summary>
        public static async Task<long> SortedSetUnionStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, IReadOnlyList<double>? weights = null, SortedSetAggregate? aggregate = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZUNIONSTORE", destination, keys.Count };
            args.AddRange(keys);
            AppendWeightsAndAggregate(args, weights, aggregate);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZINTERSTORE destination numkeys key [key ...] [WEIGHTS w ...] [AGGREGATE SUM|MIN|MAX] -> size of the stored result.</summary>
        public static async Task<long> SortedSetInterStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, IReadOnlyList<double>? weights = null, SortedSetAggregate? aggregate = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZINTERSTORE", destination, keys.Count };
            args.AddRange(keys);
            AppendWeightsAndAggregate(args, weights, aggregate);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>ZUNION numkeys key [key ...] [WEIGHTS w ...] [AGGREGATE SUM|MIN|MAX] [WITHSCORES].</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetUnionAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, IReadOnlyList<double>? weights = null, SortedSetAggregate? aggregate = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZUNION", keys.Count };
            args.AddRange(keys);
            AppendWeightsAndAggregate(args, weights, aggregate);
            args.Add("WITHSCORES");
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZINTER numkeys key [key ...] [WEIGHTS w ...] [AGGREGATE SUM|MIN|MAX] [WITHSCORES].</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetInterAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, IReadOnlyList<double>? weights = null, SortedSetAggregate? aggregate = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZINTER", keys.Count };
            args.AddRange(keys);
            AppendWeightsAndAggregate(args, weights, aggregate);
            args.Add("WITHSCORES");
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZDIFF numkeys key [key ...] [WITHSCORES].</summary>
        public static async Task<IReadOnlyList<(string Member, double Score)>> SortedSetDiffAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZDIFF", keys.Count };
            args.AddRange(keys);
            args.Add("WITHSCORES");
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return PairsToScored(reply);
        }

        /// <summary>ZDIFFSTORE destination numkeys key [key ...] -> size of the stored result.</summary>
        public static async Task<long> SortedSetDiffStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ZDIFFSTORE", destination, keys.Count };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }
    }
}
