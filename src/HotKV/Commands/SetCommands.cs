// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Typed wrappers for the set command family.</summary>
    public static class SetCommands
    {
        /// <summary>SADD key member [member ...] -> number newly added.</summary>
        public static async Task<long> SetAddAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "SADD", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SADD key member.</summary>
        public static Task<long> SetAddAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default) =>
            executor.SetAddAsync(key, new[] { member }, cancellationToken);

        /// <summary>SREM key member [member ...] -> number removed.</summary>
        public static async Task<long> SetRemoveAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "SREM", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SISMEMBER key member.</summary>
        public static async Task<bool> SetIsMemberAsync(this ICommandExecutor executor, string key, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SISMEMBER", key, member }, cancellationToken).ConfigureAwait(false);
            return reply.AsBoolean();
        }

        /// <summary>SMISMEMBER key member [member ...] -> membership flags in the same order as the input.</summary>
        public static async Task<IReadOnlyList<bool>> SetMultiIsMemberAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> members, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(members.Count + 2) { "SMISMEMBER", key };
            args.AddRange(members);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsBoolean()).ToList();
        }

        /// <summary>SCARD key.</summary>
        public static async Task<long> SetCardinalityAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SCARD", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SMEMBERS key.</summary>
        public static async Task<IReadOnlyList<string>> SetMembersAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SMEMBERS", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>SPOP key [count]. Without count, pops one member (null if empty). With count, pops up to count members.</summary>
        public static async Task<IReadOnlyList<string>> SetPopAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "SPOP", key, count.Value } : new object?[] { "SPOP", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return System.Array.Empty<string>();
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>SRANDMEMBER key [count]. Positive count: up to count distinct members. Negative count: exactly abs(count) members, possibly repeated.</summary>
        public static async Task<IReadOnlyList<string>> SetRandomMemberAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "SRANDMEMBER", key, count.Value } : new object?[] { "SRANDMEMBER", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return System.Array.Empty<string>();
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>SMOVE source destination member -> true if moved.</summary>
        public static async Task<bool> SetMoveAsync(this ICommandExecutor executor, string source, string destination, string member, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SMOVE", source, destination, member }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>SINTER key [key ...].</summary>
        public static async Task<IReadOnlyList<string>> SetIntersectAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "SINTER" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>SINTERSTORE destination key [key ...] -> size of the stored result.</summary>
        public static async Task<long> SetIntersectStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 2) { "SINTERSTORE", destination };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SINTERCARD numkeys key [key ...] [LIMIT limit] -> size of the intersection without materializing it.</summary>
        public static async Task<long> SetIntersectCardinalityAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, long? limit = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 4) { "SINTERCARD", keys.Count };
            args.AddRange(keys);
            if (limit.HasValue)
            {
                args.Add("LIMIT");
                args.Add(limit.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SUNION key [key ...].</summary>
        public static async Task<IReadOnlyList<string>> SetUnionAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "SUNION" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>SUNIONSTORE destination key [key ...] -> size of the stored result.</summary>
        public static async Task<long> SetUnionStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 2) { "SUNIONSTORE", destination };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SDIFF key [key ...].</summary>
        public static async Task<IReadOnlyList<string>> SetDifferenceAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "SDIFF" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>SDIFFSTORE destination key [key ...] -> size of the stored result.</summary>
        public static async Task<long> SetDifferenceStoreAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 2) { "SDIFFSTORE", destination };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }
    }
}
