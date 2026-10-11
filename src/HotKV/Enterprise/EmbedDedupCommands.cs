// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>EMBED.DEDUP.STATS reply.</summary>
    public sealed record EmbedDedupStats(long Hits, long Misses, long Entries);

    /// <summary>
    /// EMBED.DEDUP.* embedding dedupe/refcount store. Needs the EmbedDedup license feature. Reachable as
    /// <c>client.EmbedDedup</c>.
    /// </summary>
    public sealed class EmbedDedupCommands
    {
        private readonly ICommandExecutor _executor;

        internal EmbedDedupCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>EMBED.DEDUP.PUT ns hash model embedding -> true if newly stored, false if hash already existed (its refcount is incremented instead).</summary>
        public async Task<bool> PutAsync(string ns, string hash, string model, string embedding, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.PUT", ns, hash, model, embedding }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>EMBED.DEDUP.GET ns hash -> the embedding, or null.</summary>
        public async Task<string?> GetAsync(string ns, string hash, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.GET", ns, hash }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>EMBED.DEDUP.RELEASE ns hash: decrements the refcount, freeing the entry at 0.</summary>
        public async Task<bool> ReleaseAsync(string ns, string hash, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.RELEASE", ns, hash }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>EMBED.DEDUP.STATS ns.</summary>
        public async Task<EmbedDedupStats> StatsAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.STATS", ns }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new EmbedDedupStats(d.GetInt64("hits"), d.GetInt64("misses"), d.GetInt64("entries"));
        }

        /// <summary>EMBED.DEDUP.DROP ns: drops the whole namespace.</summary>
        public async Task<bool> DropAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.DROP", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>EMBED.DEDUP.PURGE ns: purges expired/unreferenced entries in the namespace.</summary>
        public async Task<bool> PurgeAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.PURGE", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>EMBED.DEDUP.LIST -> namespace names.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "EMBED.DEDUP.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
