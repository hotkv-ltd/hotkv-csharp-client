// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.AI
{
    /// <summary>PCACHE.STATS reply, including savings attributed from usage recorded on SET.</summary>
    public sealed record PromptCacheStats(
        long Entries,
        long Hits,
        long Misses,
        long Evictions,
        long MemoryBytes,
        double HitRate,
        long P50Nanoseconds,
        long P95Nanoseconds,
        long P99Nanoseconds,
        double CostSavedUsd,
        UsageSavings UsageSaved)
    {
        internal static PromptCacheStats FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new PromptCacheStats(
                d.GetInt64("entries"), d.GetInt64("hits"), d.GetInt64("misses"), d.GetInt64("evictions"), d.GetInt64("memory_bytes"),
                d.GetDouble("hit_rate"), d.GetInt64("p50_ns"), d.GetInt64("p95_ns"), d.GetInt64("p99_ns"), d.GetDouble("cost_saved_usd"),
                UsageSavings.FromDictionary(d));
        }
    }

    /// <summary>PCACHE.GETEX reply.</summary>
    public sealed record PromptCacheEntry(string Response, string Metadata, long HitCount);

    /// <summary>Result of <see cref="PromptCache.GetOrCallAsync"/>.</summary>
    public sealed record PromptCacheResult(string Response, bool Hit);

    /// <summary>
    /// Token/cost accounting for one LLM call, returned by the caller's
    /// model function on a <see cref="PromptCache"/>/<see cref="SemanticCache"/>
    /// miss. <see cref="Model"/>, when non-empty, is attached to the
    /// stored cache entry (the PCACHE.SET/SCACHE.SET usage group, see
    /// <see cref="Usage"/>) so a later hit attributes the tokens (and
    /// cost) that hit did not have to spend.
    /// </summary>
    public sealed record LlmUsage(string Model = "", long InputTokens = 0, long OutputTokens = 0, long CacheReadTokens = 0, long CacheWrite5mTokens = 0, long CacheWrite1hTokens = 0)
    {
        /// <summary>No usage to record (the default for a plain string response).</summary>
        public static readonly LlmUsage None = new LlmUsage();

        /// <summary>This usage as a cache-entry <see cref="Usage"/> group, or null when <see cref="Model"/> is empty (<see cref="Usage"/> requires one).</summary>
        internal Usage? ToUsage() => string.IsNullOrEmpty(Model)
            ? null
            : new Usage(Model) { Input = InputTokens, Output = OutputTokens, CacheRead = CacheReadTokens, CacheWrite5m = CacheWrite5mTokens, CacheWrite1h = CacheWrite1hTokens };
    }

    /// <summary>What the caller supplied function passed to <see cref="PromptCache.GetOrCallAsync"/>/<c>SemanticCache.GetOrComputeAsync</c> must return on a cache miss.</summary>
    public sealed record LlmCallResult(string Response, LlmUsage? Usage = null);

    /// <summary>
    /// PCACHE.* prompt/LLM response cache, plus the
    /// idiomatic get-or-call helper described in section 4: on a cache hit
    /// <see cref="GetOrCallAsync"/> returns the stored response without
    /// invoking the LLM; on a miss it calls the caller's function, stores
    /// the result and returns it. The cache key is built with
    /// <see cref="CacheKeyBuilder"/>, so it is byte-identical to the key
    /// every other official HotKV SDK derives from the same logical
    /// request. Needs the PromptCache license feature. Reachable as
    /// <c>client.PromptCache</c>.
    /// </summary>
    public sealed class PromptCache
    {
        private readonly ICommandExecutor _executor;

        internal PromptCache(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>PCACHE.CREATE ns [MAX_ENTRIES n] [TTL seconds] [COST_PER_HIT usd].</summary>
        public Task CreateNamespaceAsync(string ns, long? maxEntries = null, TimeSpan? ttl = null, double? costPerHit = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "PCACHE.CREATE", ns };
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
            if (costPerHit.HasValue)
            {
                args.Add("COST_PER_HIT");
                args.Add(costPerHit.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>
        /// PCACHE.SET ns prompt response [TTL seconds] [META json] [usage
        /// group]. <paramref name="usage"/> records the original
        /// (uncached) model call's token usage with this entry, so a
        /// later GET/GETEX hit attributes the tokens (and, once the
        /// model is priced, the cost) to <see cref="PromptCacheStats.UsageSaved"/>.
        /// </summary>
        public Task SetAsync(string ns, string prompt, string response, TimeSpan? ttl = null, string? meta = null, Usage? usage = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "PCACHE.SET", ns, prompt, response };
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
            HotKV.Usage.AppendIfPresent(args, usage);
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>PCACHE.GET ns prompt -> the response, or null on a miss.</summary>
        public async Task<string?> GetAsync(string ns, string prompt, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.GET", ns, prompt }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>PCACHE.GETEX ns prompt -> response, metadata and hit count, or null on a miss.</summary>
        public async Task<PromptCacheEntry?> GetExtendedAsync(string ns, string prompt, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.GETEX", ns, prompt }, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return new PromptCacheEntry(items[0].AsString() ?? string.Empty, items[1].AsString() ?? string.Empty, items[2].AsInt64());
        }

        /// <summary>PCACHE.DEL ns prompt.</summary>
        public async Task<bool> DeleteAsync(string ns, string prompt, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.DEL", ns, prompt }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PCACHE.STATS ns, including how many tokens and how much cost recorded usage has saved on hits.</summary>
        public async Task<PromptCacheStats> StatsAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.STATS", ns }, cancellationToken).ConfigureAwait(false);
            return PromptCacheStats.FromReply(reply);
        }

        /// <summary>PCACHE.FLUSH ns -> number of entries flushed.</summary>
        public async Task<long> FlushAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.FLUSH", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>PCACHE.DROP ns.</summary>
        public async Task<bool> DropAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.DROP", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PCACHE.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PCACHE.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>PCACHE.CONFIG ns SET MAX_ENTRIES|TTL|COST_PER_HIT value.</summary>
        public Task ConfigSetAsync(string ns, string parameter, string value, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "PCACHE.CONFIG", ns, "SET", parameter, value }, cancellationToken).AsTask();

        /// <summary>
        /// Returns the cached response for <paramref name="request"/> if
        /// present; otherwise calls <paramref name="onMiss"/>, caches its
        /// result (together with any usage it reports; see
        /// <see cref="LlmUsage"/>) under the request's canonical key (see
        /// <see cref="CacheKeyBuilder"/>), and returns it. The LLM is only
        /// ever called on a miss. The namespace must already exist (see
        /// <see cref="CreateNamespaceAsync"/>): on an unknown namespace the server
        /// answers <c>ERR namespace not found</c>, thrown as <see cref="HotKVServerException"/>.
        /// </summary>
        public async Task<PromptCacheResult> GetOrCallAsync(string ns, PromptCacheKeyRequest request, Func<CancellationToken, Task<LlmCallResult>> onMiss, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            if (onMiss is null)
            {
                throw new ArgumentNullException(nameof(onMiss));
            }
            string key = CacheKeyBuilder.BuildPromptCacheKey(request);
            string? cached = await GetAsync(ns, key, cancellationToken).ConfigureAwait(false);
            if (cached is not null)
            {
                return new PromptCacheResult(cached, true);
            }
            LlmCallResult result = await onMiss(cancellationToken).ConfigureAwait(false);
            await SetAsync(ns, key, result.Response, ttl, null, result.Usage?.ToUsage(), cancellationToken).ConfigureAwait(false);
            return new PromptCacheResult(result.Response, false);
        }
    }
}
