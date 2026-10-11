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
    /// <summary>Condition for <see cref="KeyCommands.ExpireAsync"/> and friends: NX/XX/GT/LT.</summary>
    public enum ExpireCondition
    {
        None,
        OnlyIfNoExpiry,
        OnlyIfHasExpiry,
        OnlyIfGreater,
        OnlyIfLess,
    }

    /// <summary>Typed wrappers for the generic key and expiry command family, plus HotKV's nanosecond resolution TTL and hot key admin commands.</summary>
    public static class KeyCommands
    {
        private static string? ConditionFlag(ExpireCondition condition) => condition switch
        {
            ExpireCondition.OnlyIfNoExpiry => "NX",
            ExpireCondition.OnlyIfHasExpiry => "XX",
            ExpireCondition.OnlyIfGreater => "GT",
            ExpireCondition.OnlyIfLess => "LT",
            _ => null,
        };

        /// <summary>DEL key [key ...] -> number of keys removed.</summary>
        public static async Task<long> DeleteAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "DEL" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>DEL key.</summary>
        public static Task<long> DeleteAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default) =>
            executor.DeleteAsync(new[] { key }, cancellationToken);

        /// <summary>UNLINK key [key ...] -> number of keys removed (non-blocking reclaim).</summary>
        public static async Task<long> UnlinkAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "UNLINK" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>EXISTS key [key ...] -> count of how many of the given keys exist (a repeated key counts twice).</summary>
        public static async Task<long> ExistsAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "EXISTS" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>EXISTS key.</summary>
        public static async Task<bool> ExistsAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default) =>
            await executor.ExistsAsync(new[] { key }, cancellationToken).ConfigureAwait(false) > 0;

        /// <summary>TYPE key.</summary>
        public static async Task<string?> TypeAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "TYPE", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>EXPIRE key seconds [NX|XX|GT|LT].</summary>
        public static async Task<bool> ExpireAsync(this ICommandExecutor executor, string key, TimeSpan ttl, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "EXPIRE", key, (long)ttl.TotalSeconds };
            var flag = ConditionFlag(condition);
            if (flag is not null)
            {
                args.Add(flag);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PEXPIRE key milliseconds [NX|XX|GT|LT].</summary>
        public static async Task<bool> PExpireAsync(this ICommandExecutor executor, string key, TimeSpan ttl, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "PEXPIRE", key, (long)ttl.TotalMilliseconds };
            var flag = ConditionFlag(condition);
            if (flag is not null)
            {
                args.Add(flag);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>EXPIREAT key unix-seconds [NX|XX|GT|LT].</summary>
        public static async Task<bool> ExpireAtAsync(this ICommandExecutor executor, string key, DateTimeOffset at, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "EXPIREAT", key, at.ToUnixTimeSeconds() };
            var flag = ConditionFlag(condition);
            if (flag is not null)
            {
                args.Add(flag);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PEXPIREAT key unix-milliseconds [NX|XX|GT|LT].</summary>
        public static async Task<bool> PExpireAtAsync(this ICommandExecutor executor, string key, DateTimeOffset at, ExpireCondition condition = ExpireCondition.None, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "PEXPIREAT", key, at.ToUnixTimeMilliseconds() };
            var flag = ConditionFlag(condition);
            if (flag is not null)
            {
                args.Add(flag);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>TTL key -> seconds remaining, -2 if the key does not exist, -1 if it has no expiry.</summary>
        public static async Task<long> TtlAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "TTL", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>PTTL key -> milliseconds remaining, -2 if the key does not exist, -1 if it has no expiry.</summary>
        public static async Task<long> PTtlAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "PTTL", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>PERSIST key -> true if a TTL was removed.</summary>
        public static async Task<bool> PersistAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "PERSIST", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>RENAME key newkey.</summary>
        public static Task RenameAsync(this ICommandExecutor executor, string key, string newKey, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "RENAME", key, newKey }, cancellationToken).AsTask();

        /// <summary>RENAMENX key newkey -> true if renamed (newkey did not already exist).</summary>
        public static async Task<bool> RenameIfNotExistsAsync(this ICommandExecutor executor, string key, string newKey, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "RENAMENX", key, newKey }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>KEYS pattern. Prefer <see cref="ScanCommands.ScanAsync"/> on a production keyspace: KEYS is O(N) and blocking.</summary>
        public static async Task<IReadOnlyList<string>> KeysAsync(this ICommandExecutor executor, string pattern, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "KEYS", pattern }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>RANDOMKEY -> a random key, or null if the keyspace is empty.</summary>
        public static async Task<string?> RandomKeyAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "RANDOMKEY" }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>TOUCH key [key ...] -> number of keys that exist.</summary>
        public static async Task<long> TouchAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "TOUCH" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>COPY source destination [DB db] [REPLACE] -> true if copied.</summary>
        public static async Task<bool> CopyAsync(this ICommandExecutor executor, string source, string destination, bool replace = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "COPY", source, destination };
            if (replace)
            {
                args.Add("REPLACE");
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>DUMP key -> the serialized value, or null if the key does not exist.</summary>
        public static async Task<byte[]?> DumpAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "DUMP", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsBytes();
        }

        /// <summary>RESTORE key ttl-ms serialized-value [REPLACE].</summary>
        public static Task RestoreAsync(this ICommandExecutor executor, string key, TimeSpan ttl, byte[] serializedValue, bool replace = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "RESTORE", key, (long)ttl.TotalMilliseconds, serializedValue };
            if (replace)
            {
                args.Add("REPLACE");
            }
            return executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        // -- HotKV exclusive: nanosecond resolution TTL --

        /// <summary>NEXPIRE key nanoseconds -> true if the TTL was set.</summary>
        public static async Task<bool> NExpireAsync(this ICommandExecutor executor, string key, long relativeNanoseconds, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "NEXPIRE", key, relativeNanoseconds }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>NTTL key -> remaining TTL in nanoseconds, -2 if the key does not exist, -1 if it has no expiry.</summary>
        public static async Task<long> NTtlAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "NTTL", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>NEXPIREAT key unix-nanoseconds -> true if the TTL was set.</summary>
        public static async Task<bool> NExpireAtAsync(this ICommandExecutor executor, string key, long unixNanoseconds, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "NEXPIREAT", key, unixNanoseconds }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        // -- HotKV exclusive: hot key admin --

        /// <summary>HOTKEYS TOP [count] -> the most frequently accessed keys, most accessed first.</summary>
        public static async Task<IReadOnlyList<(string Key, long Count)>> HotKeysTopAsync(this ICommandExecutor executor, int count = 10, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "HOTKEYS", "TOP", count }, cancellationToken).ConfigureAwait(false);
            return reply.AsArray()
                .Select(pair => pair.AsArray())
                .Select(pair => (pair[0].AsString() ?? string.Empty, pair[1].AsInt64()))
                .ToList();
        }

        /// <summary>HOTKEYS RESET.</summary>
        public static Task HotKeysResetAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "HOTKEYS", "RESET" }, cancellationToken).AsTask();
    }
}
