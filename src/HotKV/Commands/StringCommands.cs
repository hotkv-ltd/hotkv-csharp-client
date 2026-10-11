// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Options for <see cref="StringCommands.StringSetAsync(ICommandExecutor, string, string, SetOptions?, CancellationToken)"/>.</summary>
    public sealed class SetOptions
    {
        /// <summary>Relative expiry (sent as SET ... EX/PX, using whole seconds when possible).</summary>
        public TimeSpan? Expiry { get; set; }

        /// <summary>Absolute expiry (sent as SET ... EXAT/PXAT).</summary>
        public DateTimeOffset? ExpireAt { get; set; }

        /// <summary>Keeps the key's current TTL instead of clearing it (SET ... KEEPTTL).</summary>
        public bool KeepTtl { get; set; }

        /// <summary>true = only set if the key already exists (XX); false = only set if it does not (NX); null = unconditional.</summary>
        public bool? OnlyIfExists { get; set; }
    }

    /// <summary>Typed wrappers for the string command family.</summary>
    public static class StringCommands
    {
        private static List<object?> BuildSetArgs(string key, object value, SetOptions? o, bool wantGet)
        {
            var args = new List<object?> { "SET", key, value };
            if (o is not null)
            {
                if (o.Expiry.HasValue)
                {
                    args.Add("PX");
                    args.Add((long)o.Expiry.Value.TotalMilliseconds);
                }
                else if (o.ExpireAt.HasValue)
                {
                    args.Add("PXAT");
                    args.Add(o.ExpireAt.Value.ToUnixTimeMilliseconds());
                }
                if (o.KeepTtl)
                {
                    args.Add("KEEPTTL");
                }
                if (o.OnlyIfExists == true)
                {
                    args.Add("XX");
                }
                else if (o.OnlyIfExists == false)
                {
                    args.Add("NX");
                }
            }
            if (wantGet)
            {
                args.Add("GET");
            }
            return args;
        }

        /// <summary>SET key value [NX|XX] [EX|PX|EXAT|PXAT] [KEEPTTL]. Returns true when the value was set, false when a NX/XX condition prevented it.</summary>
        public static async Task<bool> StringSetAsync(this ICommandExecutor executor, string key, string value, SetOptions? options = null, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildSetArgs(key, value, options, wantGet: false), cancellationToken).ConfigureAwait(false);
            return !reply.IsNull;
        }

        /// <summary>SET key value ... with the binary safe byte[] overload.</summary>
        public static async Task<bool> StringSetAsync(this ICommandExecutor executor, string key, byte[] value, SetOptions? options = null, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildSetArgs(key, value, options, wantGet: false), cancellationToken).ConfigureAwait(false);
            return !reply.IsNull;
        }

        /// <summary>SET key value ... GET: sets the key and atomically returns its previous value (or null if it had none / the NX condition blocked the set).</summary>
        public static async Task<string?> StringSetAndGetAsync(this ICommandExecutor executor, string key, string value, SetOptions? options = null, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(BuildSetArgs(key, value, options, wantGet: true), cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>GET key.</summary>
        public static async Task<string?> StringGetAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GET", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>GET key, returning the raw bytes (binary safe).</summary>
        public static async Task<byte[]?> StringGetBytesAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GET", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsBytes();
        }

        /// <summary>SETNX key value.</summary>
        public static async Task<bool> StringSetIfNotExistsAsync(this ICommandExecutor executor, string key, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SETNX", key, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>SETEX key seconds value.</summary>
        public static Task StringSetExAsync(this ICommandExecutor executor, string key, TimeSpan expiry, string value, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "SETEX", key, (long)expiry.TotalSeconds, value }, cancellationToken).AsTask();

        /// <summary>PSETEX key milliseconds value.</summary>
        public static Task StringSetExMillisecondsAsync(this ICommandExecutor executor, string key, TimeSpan expiry, string value, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "PSETEX", key, (long)expiry.TotalMilliseconds, value }, cancellationToken).AsTask();

        /// <summary>GETSET key value (deprecated by SET ... GET upstream, kept for compatibility).</summary>
        public static async Task<string?> StringGetSetAsync(this ICommandExecutor executor, string key, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GETSET", key, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>GETDEL key.</summary>
        public static async Task<string?> StringGetDeleteAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GETDEL", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>GETEX key [EX|PX|EXAT|PXAT seconds|PERSIST]. Pass options null and persist=false to peek the TTL unchanged.</summary>
        public static async Task<string?> StringGetExAsync(this ICommandExecutor executor, string key, TimeSpan? expiry = null, DateTimeOffset? expireAt = null, bool persist = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "GETEX", key };
            if (persist)
            {
                args.Add("PERSIST");
            }
            else if (expiry.HasValue)
            {
                args.Add("PX");
                args.Add((long)expiry.Value.TotalMilliseconds);
            }
            else if (expireAt.HasValue)
            {
                args.Add("PXAT");
                args.Add(expireAt.Value.ToUnixTimeMilliseconds());
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>APPEND key value -> new length.</summary>
        public static async Task<long> StringAppendAsync(this ICommandExecutor executor, string key, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "APPEND", key, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>STRLEN key.</summary>
        public static async Task<long> StringLengthAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "STRLEN", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>GETRANGE key start end.</summary>
        public static async Task<string?> StringGetRangeAsync(this ICommandExecutor executor, string key, long start, long end, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GETRANGE", key, start, end }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>SETRANGE key offset value -> new length.</summary>
        public static async Task<long> StringSetRangeAsync(this ICommandExecutor executor, string key, long offset, string value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SETRANGE", key, offset, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>MGET key [key ...].</summary>
        public static async Task<IReadOnlyList<string?>> StringGetMultipleAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "MGET" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsStringArray();
        }

        /// <summary>MSET key value [key value ...].</summary>
        public static Task StringSetMultipleAsync(this ICommandExecutor executor, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count * 2 + 1) { "MSET" };
            foreach (var kv in values)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            return executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>MSETNX key value [key value ...] -> true only if every key was newly set.</summary>
        public static async Task<bool> StringSetMultipleIfNoneExistAsync(this ICommandExecutor executor, IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(values.Count * 2 + 1) { "MSETNX" };
            foreach (var kv in values)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>INCR key.</summary>
        public static async Task<long> IncrementAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "INCR", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>INCRBY key increment.</summary>
        public static async Task<long> IncrementByAsync(this ICommandExecutor executor, string key, long increment, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "INCRBY", key, increment }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>INCRBYFLOAT key increment.</summary>
        public static async Task<double> IncrementByFloatAsync(this ICommandExecutor executor, string key, double increment, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "INCRBYFLOAT", key, increment }, cancellationToken).ConfigureAwait(false);
            return reply.AsDouble();
        }

        /// <summary>DECR key.</summary>
        public static async Task<long> DecrementAsync(this ICommandExecutor executor, string key, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "DECR", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>DECRBY key decrement.</summary>
        public static async Task<long> DecrementByAsync(this ICommandExecutor executor, string key, long decrement, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "DECRBY", key, decrement }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }
    }
}
