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
    /// <summary>Typed wrappers for the list command family.</summary>
    public static class ListCommands
    {
        /// <summary>LPUSH key value [value ...] -> new length.</summary>
        public static async Task<long> ListLeftPushAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count + 2) { "LPUSH", key };
            args.AddRange(values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>LPUSH key value.</summary>
        public static Task<long> ListLeftPushAsync(this ICommandExecutor executor, string key, string value, CancellationToken cancellationToken = default) =>
            executor.ListLeftPushAsync(key, new[] { value }, cancellationToken);

        /// <summary>LPUSHX key value [value ...] -> new length, 0 if the key does not exist.</summary>
        public static async Task<long> ListLeftPushIfExistsAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count + 2) { "LPUSHX", key };
            args.AddRange(values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>RPUSH key value [value ...] -> new length.</summary>
        public static async Task<long> ListRightPushAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count + 2) { "RPUSH", key };
            args.AddRange(values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>RPUSH key value.</summary>
        public static Task<long> ListRightPushAsync(this ICommandExecutor executor, string key, string value, CancellationToken cancellationToken = default) =>
            executor.ListRightPushAsync(key, new[] { value }, cancellationToken);

        /// <summary>RPUSHX key value [value ...] -> new length, 0 if the key does not exist.</summary>
        public static async Task<long> ListRightPushIfExistsAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count + 2) { "RPUSHX", key };
            args.AddRange(values);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>LPOP key [count]. Without count, returns one element or null. With count, returns up to count elements (empty when the key does not exist), null distinguishes a missing key from an empty result only when count is given.</summary>
        public static async Task<IReadOnlyList<string>?> ListLeftPopAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "LPOP", key, count.Value } : new object?[] { "LPOP", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>RPOP key [count]. See <see cref="ListLeftPopAsync"/> for the count semantics.</summary>
        public static async Task<IReadOnlyList<string>?> ListRightPopAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "RPOP", key, count.Value } : new object?[] { "RPOP", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>LLEN key.</summary>
        public static async Task<long> ListLengthAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LLEN", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>LRANGE key start stop.</summary>
        public static async Task<IReadOnlyList<string>> ListRangeAsync(this ICommandExecutor executor, string key, long start, long stop, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LRANGE", key, start, stop }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>LINDEX key index.</summary>
        public static async Task<string?> ListIndexAsync(this ICommandExecutor executor, string key, long index, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LINDEX", key, index }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>LSET key index value.</summary>
        public static Task ListSetAsync(this ICommandExecutor executor, string key, long index, string value, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "LSET", key, index, value }, cancellationToken).AsTask();

        /// <summary>LINSERT key BEFORE|AFTER pivot value -> new length, 0 if pivot not found, -1 if the key does not exist.</summary>
        public static async Task<long> ListInsertAsync(this ICommandExecutor executor, string key, bool before, string pivot, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LINSERT", key, before ? "BEFORE" : "AFTER", pivot, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>LREM key count value -> number removed.</summary>
        public static async Task<long> ListRemoveAsync(this ICommandExecutor executor, string key, long count, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LREM", key, count, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>LTRIM key start stop.</summary>
        public static Task ListTrimAsync(this ICommandExecutor executor, string key, long start, long stop, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "LTRIM", key, start, stop }, cancellationToken).AsTask();

        /// <summary>LPOS key element [RANK rank] [COUNT count]. count=0 returns every match; null returns the first match only.</summary>
        public static async Task<IReadOnlyList<long>> ListPositionAsync(this ICommandExecutor executor, string key, string element, long? rank = null, long? count = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "LPOS", key, element };
            if (rank.HasValue)
            {
                args.Add("RANK");
                args.Add(rank.Value);
            }
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return Array.Empty<long>();
            }
            return count.HasValue ? reply.AsArray().Select(v => v.AsInt64()).ToList() : new List<long> { reply.AsInt64() };
        }

        /// <summary>LMOVE source destination LEFT|RIGHT LEFT|RIGHT -> the moved element, or null if source is empty.</summary>
        public static async Task<string?> ListMoveAsync(this ICommandExecutor executor, string source, string destination, bool fromLeft, bool toLeft, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "LMOVE", source, destination, fromLeft ? "LEFT" : "RIGHT", toLeft ? "LEFT" : "RIGHT" }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>BLPOP key [key ...] timeout -> (key, value), or null on timeout.</summary>
        public static async Task<(string Key, string Value)?> ListBlockingLeftPopAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 2) { "BLPOP" };
            args.AddRange(keys);
            args.Add(timeout.TotalSeconds);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return (items[0].AsString() ?? string.Empty, items[1].AsString() ?? string.Empty);
        }

        /// <summary>BRPOP key [key ...] timeout -> (key, value), or null on timeout.</summary>
        public static async Task<(string Key, string Value)?> ListBlockingRightPopAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 2) { "BRPOP" };
            args.AddRange(keys);
            args.Add(timeout.TotalSeconds);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return (items[0].AsString() ?? string.Empty, items[1].AsString() ?? string.Empty);
        }
    }
}
