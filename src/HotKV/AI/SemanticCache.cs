// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.AI
{
    /// <summary>SCACHE.STATS reply, including savings attributed from usage recorded on SET.</summary>
    public sealed record SemanticCacheStats(long Entries, long Hits, long Misses, double HitRate, long P50Microseconds, UsageSavings UsageSaved)
    {
        internal static SemanticCacheStats FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new SemanticCacheStats(
                d.GetInt64("entries"), d.GetInt64("hits"), d.GetInt64("misses"), d.GetDouble("hit_rate"), d.GetInt64("p50_us"),
                UsageSavings.FromDictionary(d));
        }
    }

    /// <summary>A SCACHE.GET hit.</summary>
    public sealed record SemanticCacheHit(string Response, double Similarity);

    /// <summary>Result of <c>SemanticCache.GetOrComputeAsync</c>.</summary>
    public sealed record SemanticCacheResult(string Response, bool Hit, double? Similarity);

    /// <summary>
    /// SCACHE.* semantic (embedding similarity) response cache, plus the idiomatic get-or-compute helper described in
    /// section 4: it looks up the nearest cached answer above the
    /// namespace's similarity threshold, calling the caller's function only
    /// on a miss. This class never computes embeddings itself; callers
    /// supply vectors from whatever embedding provider they use. Needs
    /// the SemanticCache license feature. Reachable as <c>client.SemanticCache</c>.
    /// </summary>
    public sealed class SemanticCache
    {
        private readonly ICommandExecutor _executor;

        internal SemanticCache(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string MetricToken(VectorMetric metric) => metric switch
        {
            VectorMetric.L2 => "l2",
            VectorMetric.Ip => "ip",
            _ => "cosine",
        };

        /// <summary>SCACHE.CREATE ns dims [METRIC cosine|l2|ip] [THRESHOLD 0.0-1.0] [TTL seconds]. Defaults: cosine metric, 0.85 threshold, no expiry.</summary>
        public Task CreateNamespaceAsync(string ns, int dimensions, VectorMetric metric = VectorMetric.Cosine, double? threshold = null, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "SCACHE.CREATE", ns, dimensions, "METRIC", MetricToken(metric) };
            if (threshold.HasValue)
            {
                args.Add("THRESHOLD");
                args.Add(threshold.Value);
            }
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>
        /// SCACHE.SET ns query_text response val1 .. valN [TTL seconds]
        /// [usage group]. <paramref name="usage"/> records the original
        /// (uncached) model call's token usage with this entry, so a
        /// later GET hit attributes the tokens (and, once the model is
        /// priced, the cost) to <see cref="SemanticCacheStats.UsageSaved"/>.
        /// </summary>
        public Task SetAsync(string ns, string queryText, string response, IReadOnlyList<double> vector, TimeSpan? ttl = null, Usage? usage = null, CancellationToken cancellationToken = default) =>
            SetCoreAsync(ns, queryText, response, vector.Cast<object?>(), ttl, usage, cancellationToken);

        /// <summary>SCACHE.SET with a float32 vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task SetAsync(string ns, string queryText, string response, IReadOnlyList<float> vector, TimeSpan? ttl = null, Usage? usage = null, CancellationToken cancellationToken = default) =>
            SetCoreAsync(ns, queryText, response, vector.Cast<object?>(), ttl, usage, cancellationToken);

        private Task SetCoreAsync(string ns, string queryText, string response, IEnumerable<object?> vector, TimeSpan? ttl, Usage? usage, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "SCACHE.SET", ns, queryText, response };
            args.AddRange(vector);
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            Usage.AppendIfPresent(args, usage);
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>SCACHE.GET ns val1 .. valN [THRESHOLD override] -> the nearest cached answer above threshold, or null.</summary>
        public Task<SemanticCacheHit?> GetAsync(string ns, IReadOnlyList<double> vector, double? thresholdOverride = null, CancellationToken cancellationToken = default) =>
            GetCoreAsync(ns, vector.Cast<object?>(), thresholdOverride, cancellationToken);

        /// <summary>SCACHE.GET with a float32 query vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<SemanticCacheHit?> GetAsync(string ns, IReadOnlyList<float> vector, double? thresholdOverride = null, CancellationToken cancellationToken = default) =>
            GetCoreAsync(ns, vector.Cast<object?>(), thresholdOverride, cancellationToken);

        private async Task<SemanticCacheHit?> GetCoreAsync(string ns, IEnumerable<object?> vector, double? thresholdOverride, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "SCACHE.GET", ns };
            args.AddRange(vector);
            if (thresholdOverride.HasValue)
            {
                args.Add("THRESHOLD");
                args.Add(thresholdOverride.Value);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return new SemanticCacheHit(items[0].AsString() ?? string.Empty, double.Parse(items[1].AsString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        /// <summary>SCACHE.STATS ns, including how many tokens and how much cost recorded usage has saved on hits.</summary>
        public async Task<SemanticCacheStats> StatsAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "SCACHE.STATS", ns }, cancellationToken).ConfigureAwait(false);
            return SemanticCacheStats.FromReply(reply);
        }

        /// <summary>SCACHE.FLUSH ns -> true if the namespace existed.</summary>
        public async Task<bool> FlushAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "SCACHE.FLUSH", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>SCACHE.DROP ns.</summary>
        public async Task<bool> DropAsync(string ns, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "SCACHE.DROP", ns }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>SCACHE.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "SCACHE.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>SCACHE.THRESHOLD ns value.</summary>
        public Task SetThresholdAsync(string ns, double value, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "SCACHE.THRESHOLD", ns, value }, cancellationToken).AsTask();

        /// <summary>
        /// Returns the cached answer for the nearest neighbor of
        /// <paramref name="vector"/> above the namespace's similarity
        /// threshold, if any; otherwise calls <paramref name="onMiss"/> and
        /// caches (queryText, response, vector) together with any usage it
        /// reports (model required for it to be attached; see
        /// <see cref="LlmUsage"/>), and returns the fresh answer.
        /// </summary>
        public Task<SemanticCacheResult> GetOrComputeAsync(string ns, string queryText, IReadOnlyList<double> vector, Func<CancellationToken, Task<LlmCallResult>> onMiss, TimeSpan? ttl = null, double? thresholdOverride = null, CancellationToken cancellationToken = default) =>
            GetOrComputeCoreAsync(onMiss, cancellationToken,
                (ct) => GetAsync(ns, vector, thresholdOverride, ct),
                (response, usage, ct) => SetAsync(ns, queryText, response, vector, ttl, usage, ct));

        /// <summary><see cref="GetOrComputeAsync(string, string, IReadOnlyList{double}, Func{CancellationToken, Task{LlmCallResult}}, TimeSpan?, double?, CancellationToken)"/> for a float32 embedding (what most embedding models return): the values are sent as their shortest float32 decimals.</summary>
        public Task<SemanticCacheResult> GetOrComputeAsync(string ns, string queryText, IReadOnlyList<float> vector, Func<CancellationToken, Task<LlmCallResult>> onMiss, TimeSpan? ttl = null, double? thresholdOverride = null, CancellationToken cancellationToken = default) =>
            GetOrComputeCoreAsync(onMiss, cancellationToken,
                (ct) => GetAsync(ns, vector, thresholdOverride, ct),
                (response, usage, ct) => SetAsync(ns, queryText, response, vector, ttl, usage, ct));

        private static async Task<SemanticCacheResult> GetOrComputeCoreAsync(Func<CancellationToken, Task<LlmCallResult>> onMiss, CancellationToken cancellationToken,
            Func<CancellationToken, Task<SemanticCacheHit?>> get, Func<string, Usage?, CancellationToken, Task> set)
        {
            if (onMiss is null)
            {
                throw new ArgumentNullException(nameof(onMiss));
            }
            var hit = await get(cancellationToken).ConfigureAwait(false);
            if (hit is not null)
            {
                return new SemanticCacheResult(hit.Response, true, hit.Similarity);
            }
            LlmCallResult result = await onMiss(cancellationToken).ConfigureAwait(false);
            await set(result.Response, result.Usage?.ToUsage(), cancellationToken).ConfigureAwait(false);
            return new SemanticCacheResult(result.Response, false, null);
        }
    }
}
