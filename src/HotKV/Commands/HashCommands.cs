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
    /// <summary>Typed wrappers for the hash command family, including the per-field expiry (HEXPIRE/HTTL) family where the server supports it.</summary>
    public static class HashCommands
    {
        /// <summary>HSET key field value [field value ...] -> number of fields newly created.</summary>
        public static async Task<long> HashSetAsync(this ICommandExecutor executor, string key, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(fields.Count * 2 + 2) { "HSET", key };
            foreach (var kv in fields)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>HSET key field value.</summary>
        public static Task<long> HashSetAsync(this ICommandExecutor executor, string key, string field, string value, CancellationToken cancellationToken = default) =>
            executor.HashSetAsync(key, new Dictionary<string, string> { [field] = value }, cancellationToken);

        /// <summary>HSETNX key field value -> true if set.</summary>
        public static async Task<bool> HashSetIfNotExistsAsync(this ICommandExecutor executor, string key, string field, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HSETNX", key, field, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>HGET key field.</summary>
        public static async Task<string?> HashGetAsync(this ICommandExecutor executor, string key, string field, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HGET", key, field }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>HMGET key field [field ...].</summary>
        public static async Task<IReadOnlyList<string?>> HashGetMultipleAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(fields.Count + 2) { "HMGET", key };
            args.AddRange(fields);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsStringArray();
        }

        /// <summary>HGETALL key.</summary>
        public static async Task<IReadOnlyDictionary<string, string>> HashGetAllAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HGETALL", key }, cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, string>();
            foreach (var (k, v) in reply.AsPairs())
            {
                result[k.AsString() ?? string.Empty] = v.AsString() ?? string.Empty;
            }
            return result;
        }

        /// <summary>HDEL key field [field ...] -> number removed.</summary>
        public static async Task<long> HashDeleteAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(fields.Count + 2) { "HDEL", key };
            args.AddRange(fields);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>HEXISTS key field.</summary>
        public static async Task<bool> HashExistsAsync(this ICommandExecutor executor, string key, string field, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HEXISTS", key, field }, cancellationToken).ConfigureAwait(false);
            return reply.AsBoolean();
        }

        /// <summary>HLEN key.</summary>
        public static async Task<long> HashLengthAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HLEN", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>HKEYS key.</summary>
        public static async Task<IReadOnlyList<string>> HashKeysAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HKEYS", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>HVALS key.</summary>
        public static async Task<IReadOnlyList<string>> HashValuesAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HVALS", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>HINCRBY key field increment.</summary>
        public static async Task<long> HashIncrementByAsync(this ICommandExecutor executor, string key, string field, long increment, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HINCRBY", key, field, increment }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>HINCRBYFLOAT key field increment.</summary>
        public static async Task<double> HashIncrementByFloatAsync(this ICommandExecutor executor, string key, string field, double increment, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HINCRBYFLOAT", key, field, increment }, cancellationToken).ConfigureAwait(false);
            return reply.AsDouble();
        }

        /// <summary>HSTRLEN key field.</summary>
        public static async Task<long> HashStringLengthAsync(this ICommandExecutor executor, string key, string field, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HSTRLEN", key, field }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>HRANDFIELD key [count [WITHVALUES]].</summary>
        public static async Task<IReadOnlyList<string>> HashRandomFieldAsync(this ICommandExecutor executor, string key, int? count = null, CancellationToken cancellationToken = default)
        {
            var args = count.HasValue ? new object?[] { "HRANDFIELD", key, count.Value } : new object?[] { "HRANDFIELD", key };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return Array.Empty<string>();
            }
            return count.HasValue ? reply.AsNonNullStringArray() : new List<string> { reply.AsString()! };
        }

        /// <summary>HRANDFIELD key count WITHVALUES.</summary>
        public static async Task<IReadOnlyList<(string Field, string Value)>> HashRandomFieldWithValuesAsync(this ICommandExecutor executor, string key, int count, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HRANDFIELD", key, count, "WITHVALUES" }, cancellationToken).ConfigureAwait(false);
            var result = new List<(string, string)>();
            foreach (var (field, value) in reply.AsPairedArray())
            {
                result.Add((field.AsString() ?? string.Empty, value.AsString() ?? string.Empty));
            }
            return result;
        }

        private static List<object?> BuildFieldTtlArgs(string command, string key, long amount, ExpireCondition condition, IReadOnlyList<string> fields)
        {
            var args = new List<object?> { command, key, amount };
            var conditionFlag = condition switch
            {
                ExpireCondition.OnlyIfNoExpiry => "NX",
                ExpireCondition.OnlyIfHasExpiry => "XX",
                ExpireCondition.OnlyIfGreater => "GT",
                ExpireCondition.OnlyIfLess => "LT",
                _ => null,
            };
            if (conditionFlag is not null)
            {
                args.Add(conditionFlag);
            }
            args.Add("FIELDS");
            args.Add(fields.Count);
            args.AddRange(fields);
            return args;
        }

        /// <summary>HEXPIRE key seconds [NX|XX|GT|LT] FIELDS numfields field [field ...] -> per-field result codes (1 set, 0 condition not met, -2 no such field/key).</summary>
        public static async Task<IReadOnlyList<long>> HashFieldExpireAsync(this ICommandExecutor executor, string key, TimeSpan ttl, IReadOnlyList<string> fields, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildFieldTtlArgs("HEXPIRE", key, (long)ttl.TotalSeconds, condition, fields), cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HPEXPIRE key milliseconds [NX|XX|GT|LT] FIELDS numfields field [field ...].</summary>
        public static async Task<IReadOnlyList<long>> HashFieldPExpireAsync(this ICommandExecutor executor, string key, TimeSpan ttl, IReadOnlyList<string> fields, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildFieldTtlArgs("HPEXPIRE", key, (long)ttl.TotalMilliseconds, condition, fields), cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HEXPIREAT key unix-seconds [NX|XX|GT|LT] FIELDS numfields field [field ...].</summary>
        public static async Task<IReadOnlyList<long>> HashFieldExpireAtAsync(this ICommandExecutor executor, string key, DateTimeOffset at, IReadOnlyList<string> fields, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildFieldTtlArgs("HEXPIREAT", key, at.ToUnixTimeSeconds(), condition, fields), cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HPEXPIREAT key unix-milliseconds [NX|XX|GT|LT] FIELDS numfields field [field ...].</summary>
        public static async Task<IReadOnlyList<long>> HashFieldPExpireAtAsync(this ICommandExecutor executor, string key, DateTimeOffset at, IReadOnlyList<string> fields, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildFieldTtlArgs("HPEXPIREAT", key, at.ToUnixTimeMilliseconds(), condition, fields), cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HTTL key FIELDS numfields field [field ...] -> seconds remaining per field (-1 no expiry, -2 no such field/key).</summary>
        public static async Task<IReadOnlyList<long>> HashFieldTtlAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "HTTL", key, "FIELDS", fields.Count };
            args.AddRange(fields);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HPTTL key FIELDS numfields field [field ...] -> milliseconds remaining per field.</summary>
        public static async Task<IReadOnlyList<long>> HashFieldPTtlAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "HPTTL", key, "FIELDS", fields.Count };
            args.AddRange(fields);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }

        /// <summary>HPERSIST key FIELDS numfields field [field ...] -> 1 per field whose TTL was removed.</summary>
        public static async Task<IReadOnlyList<long>> HashFieldPersistAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "HPERSIST", key, "FIELDS", fields.Count };
            args.AddRange(fields);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsInt64()).ToList();
        }
    }
}
