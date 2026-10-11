// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>One stream entry: its ID and its flat field/value map.</summary>
    public sealed record StreamEntry(string Id, IReadOnlyDictionary<string, string> Fields);

    /// <summary>One pending-entries-list record from the extended form of XPENDING.</summary>
    public sealed record StreamPendingEntry(string Id, string Consumer, long IdleMilliseconds, long DeliveryCount);

    /// <summary>Summary reply from the no-range form of XPENDING.</summary>
    public sealed record StreamPendingSummary(long Count, string? MinId, string? MaxId, IReadOnlyList<(string Consumer, long Count)> PerConsumer);

    /// <summary>How XADD/XTRIM trim the stream.</summary>
    public enum StreamTrimStrategy
    {
        None,
        MaxLen,
        MinId,
    }

    /// <summary>Options for <see cref="StreamCommands.StreamAddAsync"/>.</summary>
    public sealed class StreamAddOptions
    {
        public bool NoMkStream { get; set; }
        public StreamTrimStrategy TrimStrategy { get; set; } = StreamTrimStrategy.None;
        public string? TrimThreshold { get; set; }
        public bool ApproximateTrim { get; set; }
        public long? TrimLimit { get; set; }
    }

    /// <summary>Typed wrappers for the stream command family.</summary>
    public static class StreamCommands
    {
        private static void AppendTrim(List<object?> args, StreamTrimStrategy strategy, string? threshold, bool approximate, long? limit)
        {
            if (strategy == StreamTrimStrategy.None)
            {
                return;
            }
            args.Add(strategy == StreamTrimStrategy.MaxLen ? "MAXLEN" : "MINID");
            args.Add(approximate ? "~" : "=");
            args.Add(threshold ?? throw new ArgumentException("a trim threshold is required when a trim strategy is set"));
            if (limit.HasValue)
            {
                args.Add("LIMIT");
                args.Add(limit.Value);
            }
        }

        /// <summary>XADD key [NOMKSTREAM] [MAXLEN|MINID ...] id-or-* field value [field value ...] -> the entry ID.</summary>
        public static async Task<string> StreamAddAsync(this ICommandExecutor executor, string key, IReadOnlyDictionary<string, string> fields, string id = "*", StreamAddOptions? options = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XADD", key };
            if (options is not null)
            {
                if (options.NoMkStream)
                {
                    args.Add("NOMKSTREAM");
                }
                AppendTrim(args, options.TrimStrategy, options.TrimThreshold, options.ApproximateTrim, options.TrimLimit);
            }
            args.Add(id);
            foreach (var kv in fields)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? throw new HotKVProtocolException("XADD returned a null entry id");
        }

        private static StreamEntry ParseEntry(RespValue item)
        {
            var pair = item.AsArray();
            string id = pair[0].AsString() ?? string.Empty;
            var fields = new Dictionary<string, string>();
            foreach (var (k, v) in pair[1].AsPairs())
            {
                fields[k.AsString() ?? string.Empty] = v.AsString() ?? string.Empty;
            }
            return new StreamEntry(id, fields);
        }

        private static IReadOnlyList<StreamEntry> ParseEntries(RespValue reply) =>
            reply.IsNull ? Array.Empty<StreamEntry>() : reply.AsArray().Select(ParseEntry).ToList();

        /// <summary>XRANGE key start end [COUNT count]. Use "-"/"+" for start/end to cover the whole stream.</summary>
        public static async Task<IReadOnlyList<StreamEntry>> StreamRangeAsync(this ICommandExecutor executor, string key, string start, string end, long? count = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XRANGE", key, start, end };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseEntries(reply);
        }

        /// <summary>XREVRANGE key end start [COUNT count].</summary>
        public static async Task<IReadOnlyList<StreamEntry>> StreamRevRangeAsync(this ICommandExecutor executor, string key, string end, string start, long? count = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XREVRANGE", key, end, start };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseEntries(reply);
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<StreamEntry>> ParseStreamsReply(RespValue reply)
        {
            var result = new Dictionary<string, IReadOnlyList<StreamEntry>>();
            if (reply.IsNull)
            {
                return result;
            }
            // RESP3 replies with a true map of stream name -> entries;
            // RESP2 replies with an array of [name, entries] pairs instead.
            foreach (var (nameValue, entriesValue) in reply.AsMapOrPairArray())
            {
                string name = nameValue.AsString() ?? string.Empty;
                result[name] = entriesValue.AsArray().Select(ParseEntry).ToList();
            }
            return result;
        }

        /// <summary>XREAD [COUNT count] [BLOCK ms] STREAMS key [key ...] id [id ...] -> entries per stream, empty if BLOCK timed out.</summary>
        public static async Task<IReadOnlyDictionary<string, IReadOnlyList<StreamEntry>>> StreamReadAsync(this ICommandExecutor executor, IReadOnlyDictionary<string, string> streamsToIds, long? count = null, TimeSpan? block = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XREAD" };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            if (block.HasValue)
            {
                args.Add("BLOCK");
                args.Add((long)block.Value.TotalMilliseconds);
            }
            args.Add("STREAMS");
            args.AddRange(streamsToIds.Keys);
            args.AddRange(streamsToIds.Values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseStreamsReply(reply);
        }

        /// <summary>XREADGROUP GROUP group consumer [COUNT count] [BLOCK ms] [NOACK] STREAMS key [key ...] id [id ...] (id is usually "&gt;" for new messages).</summary>
        public static async Task<IReadOnlyDictionary<string, IReadOnlyList<StreamEntry>>> StreamReadGroupAsync(this ICommandExecutor executor, string group, string consumer, IReadOnlyDictionary<string, string> streamsToIds, long? count = null, TimeSpan? block = null, bool noAck = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XREADGROUP", "GROUP", group, consumer };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            if (block.HasValue)
            {
                args.Add("BLOCK");
                args.Add((long)block.Value.TotalMilliseconds);
            }
            if (noAck)
            {
                args.Add("NOACK");
            }
            args.Add("STREAMS");
            args.AddRange(streamsToIds.Keys);
            args.AddRange(streamsToIds.Values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseStreamsReply(reply);
        }

        /// <summary>XACK key group id [id ...] -> number acknowledged.</summary>
        public static async Task<long> StreamAckAsync(this ICommandExecutor executor, string key, string group, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(ids.Count + 3) { "XACK", key, group };
            args.AddRange(ids);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>XGROUP CREATE key group id-or-$ [MKSTREAM].</summary>
        public static Task StreamGroupCreateAsync(this ICommandExecutor executor, string key, string group, string id = "$", bool makeStream = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XGROUP", "CREATE", key, group, id };
            if (makeStream)
            {
                args.Add("MKSTREAM");
            }
            return executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>XGROUP SETID key group id-or-$.</summary>
        public static Task StreamGroupSetIdAsync(this ICommandExecutor executor, string key, string group, string id = "$", CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "XGROUP", "SETID", key, group, id }, cancellationToken).AsTask();

        /// <summary>XGROUP DESTROY key group -> true if the group existed.</summary>
        public static async Task<bool> StreamGroupDestroyAsync(this ICommandExecutor executor, string key, string group, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XGROUP", "DESTROY", key, group }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>XGROUP CREATECONSUMER key group consumer -> true if newly created.</summary>
        public static async Task<bool> StreamGroupCreateConsumerAsync(this ICommandExecutor executor, string key, string group, string consumer, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XGROUP", "CREATECONSUMER", key, group, consumer }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>XGROUP DELCONSUMER key group consumer -> number of pending entries that consumer had.</summary>
        public static async Task<long> StreamGroupDeleteConsumerAsync(this ICommandExecutor executor, string key, string group, string consumer, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XGROUP", "DELCONSUMER", key, group, consumer }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>XPENDING key group -> the no-range summary form.</summary>
        public static async Task<StreamPendingSummary> StreamPendingSummaryAsync(this ICommandExecutor executor, string key, string group, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XPENDING", key, group }, cancellationToken).ConfigureAwait(false);
            var items = reply.AsArray();
            long count = items[0].AsInt64();
            string? minId = items[1].AsString();
            string? maxId = items[2].AsString();
            var perConsumer = new List<(string, long)>();
            if (!items[3].IsNull)
            {
                foreach (var entry in items[3].AsArray())
                {
                    var pair = entry.AsArray();
                    perConsumer.Add((pair[0].AsString() ?? string.Empty, long.Parse(pair[1].AsString() ?? "0", System.Globalization.CultureInfo.InvariantCulture)));
                }
            }
            return new StreamPendingSummary(count, minId, maxId, perConsumer);
        }

        /// <summary>XPENDING key group [IDLE min-idle-ms] start end count [consumer] -> the extended, per-entry form.</summary>
        public static async Task<IReadOnlyList<StreamPendingEntry>> StreamPendingRangeAsync(this ICommandExecutor executor, string key, string group, string start, string end, long count, string? consumer = null, TimeSpan? minIdle = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XPENDING", key, group };
            if (minIdle.HasValue)
            {
                args.Add("IDLE");
                args.Add((long)minIdle.Value.TotalMilliseconds);
            }
            args.Add(start);
            args.Add(end);
            args.Add(count);
            if (consumer is not null)
            {
                args.Add(consumer);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(item =>
            {
                var pair = item.AsArray();
                return new StreamPendingEntry(
                    pair[0].AsString() ?? string.Empty,
                    pair[1].AsString() ?? string.Empty,
                    pair[2].AsInt64(),
                    pair[3].AsInt64());
            }).ToList();
        }

        /// <summary>XCLAIM key group consumer min-idle-ms id [id ...] -> the claimed entries.</summary>
        public static async Task<IReadOnlyList<StreamEntry>> StreamClaimAsync(this ICommandExecutor executor, string key, string group, string consumer, TimeSpan minIdle, IReadOnlyList<string> ids, bool force = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XCLAIM", key, group, consumer, (long)minIdle.TotalMilliseconds };
            args.AddRange(ids);
            if (force)
            {
                args.Add("FORCE");
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseEntries(reply);
        }

        /// <summary>XCLAIM ... JUSTID -> only the claimed entry IDs.</summary>
        public static async Task<IReadOnlyList<string>> StreamClaimJustIdAsync(this ICommandExecutor executor, string key, string group, string consumer, TimeSpan minIdle, IReadOnlyList<string> ids, bool force = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XCLAIM", key, group, consumer, (long)minIdle.TotalMilliseconds };
            args.AddRange(ids);
            if (force)
            {
                args.Add("FORCE");
            }
            args.Add("JUSTID");
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>XAUTOCLAIM key group consumer min-idle-ms start [COUNT count] -> (next cursor, claimed entries, deleted entry ids).</summary>
        public static async Task<(string NextCursor, IReadOnlyList<StreamEntry> Entries, IReadOnlyList<string> DeletedIds)> StreamAutoClaimAsync(this ICommandExecutor executor, string key, string group, string consumer, TimeSpan minIdle, string start, long? count = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XAUTOCLAIM", key, group, consumer, (long)minIdle.TotalMilliseconds, start };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            var items = reply.AsArray();
            string next = items[0].AsString() ?? "0-0";
            var entries = items[1].AsArray().Select(ParseEntry).ToList();
            var deleted = items.Count > 2 ? items[2].AsNonNullStringArray() : Array.Empty<string>();
            return (next, entries, deleted);
        }

        /// <summary>XTRIM key MAXLEN|MINID [=|~] threshold [LIMIT count] -> number of entries removed.</summary>
        public static async Task<long> StreamTrimAsync(this ICommandExecutor executor, string key, StreamTrimStrategy strategy, string threshold, bool approximate = false, long? limit = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "XTRIM", key };
            AppendTrim(args, strategy, threshold, approximate, limit);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>XLEN key.</summary>
        public static async Task<long> StreamLengthAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XLEN", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>XDEL key id [id ...] -> number removed.</summary>
        public static async Task<long> StreamDeleteAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(ids.Count + 2) { "XDEL", key };
            args.AddRange(ids);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>XINFO STREAM key -> the server's field/value map, values left as RESP values since their shape (nested entries, etc.) varies by field.</summary>
        public static async Task<IReadOnlyDictionary<string, RespValue>> StreamInfoAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XINFO", "STREAM", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsDictionary();
        }

        /// <summary>XINFO GROUPS key -> one field/value map per consumer group.</summary>
        public static async Task<IReadOnlyList<IReadOnlyDictionary<string, RespValue>>> StreamInfoGroupsAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XINFO", "GROUPS", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(g => g.AsDictionary()).ToList();
        }

        /// <summary>XINFO CONSUMERS key group -> one field/value map per consumer.</summary>
        public static async Task<IReadOnlyList<IReadOnlyDictionary<string, RespValue>>> StreamInfoConsumersAsync(this ICommandExecutor executor, string key, string group, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "XINFO", "CONSUMERS", key, group }, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(c => c.AsDictionary()).ToList();
        }
    }
}
