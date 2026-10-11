// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>Stats shared by every keyed cache family, including savings attributed from usage recorded on SET.</summary>
    public sealed record KeyedCacheStats(long Entries, long Hits, long Misses, long Evictions, long MemoryBytes, UsageSavings UsageSaved)
    {
        internal static KeyedCacheStats FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new KeyedCacheStats(d.GetInt64("entries"), d.GetInt64("hits"), d.GetInt64("misses"), d.GetInt64("evictions"), d.GetInt64("memory_bytes"), UsageSavings.FromDictionary(d));
        }
    }

    /// <summary>
    /// The identical command shape shared by TOOL.CACHE.*, NCACHE.*,
    /// GUARD.* and RERANK.*: same engine, different
    /// namespace prefix and license feature. Reachable as <c>client.ToolCache</c>,
    /// <c>client.NegativeCache</c>, <c>client.GuardCache</c> and
    /// <c>client.RerankCache</c>.
    /// </summary>
    public sealed class KeyedCacheCommands
    {
        private readonly ICommandExecutor _executor;
        private readonly string _prefix;

        internal KeyedCacheCommands(ICommandExecutor executor, string prefix)
        {
            _executor = executor;
            _prefix = prefix;
        }

        /// <summary>&lt;PREFIX&gt;.CREATE ns [MAX_ENTRIES n] [TTL seconds].</summary>
        public Task CreateAsync(string ns, long? maxEntries = null, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { _prefix + ".CREATE", ns };
            if (maxEntries.HasValue)
            {
                args.Add("MAX_ENTRIES");
                args.Add(maxEntries.Value);
            }
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>
        /// &lt;PREFIX&gt;.SET ns key value [TTL seconds] [META json] [usage
        /// group]. <paramref name="usage"/> records the original
        /// (uncached) model call's token usage with this entry, so a
        /// later &lt;PREFIX&gt;.GET hit attributes the tokens (and, once
        /// the model is priced, the cost) to <see cref="KeyedCacheStats.UsageSaved"/>.
        /// </summary>
        public Task SetAsync(string ns, string key, string value, TimeSpan? ttl = null, string? meta = null, Usage? usage = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { _prefix + ".SET", ns, key, value };
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            if (meta is not null)
            {
                args.Add("META");
                args.Add(meta);
            }
            Usage.AppendIfPresent(args, usage);
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>&lt;PREFIX&gt;.GET ns key -> value, or null on miss.</summary>
        public async Task<string?> GetAsync(string ns, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".GET", ns, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>&lt;PREFIX&gt;.DEL ns key.</summary>
        public async Task<bool> DeleteAsync(string ns, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".DEL", ns, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>&lt;PREFIX&gt;.STATS ns.</summary>
        public async Task<KeyedCacheStats> StatsAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".STATS", ns }, cancellationToken).ConfigureAwait(false);
            return KeyedCacheStats.FromReply(reply);
        }

        /// <summary>&lt;PREFIX&gt;.FLUSH ns -> number of entries flushed.</summary>
        public async Task<long> FlushAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".FLUSH", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>&lt;PREFIX&gt;.DROP ns.</summary>
        public async Task<bool> DropAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".DROP", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>&lt;PREFIX&gt;.LIST -> namespace names.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { _prefix + ".LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
