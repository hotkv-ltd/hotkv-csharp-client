// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;
using HotKV.Internal;

namespace HotKV
{
    /// <summary>LLMSTATS.GET reply: usage accounting for one bucket, plus a trailing 1-minute rolling window.</summary>
    public sealed record LlmStatsSnapshot(
        long Requests,
        long Errors,
        long InputTokens,
        long OutputTokens,
        long CachedTokens,
        double CostUsd,
        double AvgLatencyMs,
        long Requests1m,
        long Tokens1m,
        double CostUsd1m,
        long CacheWrite5mTokens,
        long CacheWrite1hTokens,
        long UnpricedRequests)
    {
        internal static LlmStatsSnapshot FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new LlmStatsSnapshot(
                d.GetInt64("requests"), d.GetInt64("errors"), d.GetInt64("input_tokens"), d.GetInt64("output_tokens"),
                d.GetInt64("cached_tokens"), d.GetDouble("cost_usd"), d.GetDouble("avg_latency_ms"),
                d.GetInt64("requests_1m"), d.GetInt64("tokens_1m"), d.GetDouble("cost_usd_1m"),
                d.GetInt64("cache_write_5m_tokens"), d.GetInt64("cache_write_1h_tokens"), d.GetInt64("unpriced_requests"));
        }
    }

    /// <summary>
    /// One model's decoded price sheet, as returned by
    /// <see cref="LlmStatsCommands.PriceGetAsync"/>/<see cref="LlmStatsCommands.PriceListAsync"/>.
    /// <see cref="Source"/> is "default", "custom" or "unpriced".
    /// </summary>
    public sealed record LlmPrice(
        string Model,
        string Source,
        bool Priced,
        bool Retired,
        double InputUsdPerMtok,
        double OutputUsdPerMtok,
        double CacheReadUsdPerMtok,
        double CacheWrite5mUsdPerMtok,
        double CacheWrite1hUsdPerMtok)
    {
        internal static LlmPrice FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new LlmPrice(
                d.GetString("model"), d.GetString("source"), d.GetBool("priced"), d.GetBool("retired"),
                d.GetDouble("input_usd_per_mtok"), d.GetDouble("output_usd_per_mtok"), d.GetDouble("cache_read_usd_per_mtok"),
                d.GetDouble("cache_write_5m_usd_per_mtok"), d.GetDouble("cache_write_1h_usd_per_mtok"));
        }
    }

    /// <summary>
    /// LLMSTATS.* usage accounting buckets and the runtime-overridable LLM
    /// price table. Needs the LLMStats license feature.
    /// Reachable as <c>client.LlmStats</c>.
    /// </summary>
    public sealed class LlmStatsCommands
    {
        private readonly ICommandExecutor _executor;

        internal LlmStatsCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>LLMSTATS.BUCKET name. Idempotent create.</summary>
        public Task CreateBucketAsync(string bucket, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "LLMSTATS.BUCKET", bucket }, cancellationToken).AsTask();

        /// <summary>
        /// LLMSTATS.RECORD bucket input_tokens output_tokens cached_tokens
        /// cost_usd latency_ms [ERROR 0|1] [MODEL model] [CACHEWRITE5M n]
        /// [CACHEWRITE1H n]. <paramref name="cachedTokens"/> is cache-READ
        /// tokens. When <paramref name="model"/> is given, the server
        /// computes cost from its price table (see
        /// <see cref="PriceSetAsync"/>) and ignores
        /// <paramref name="costUsd"/> (pass 0); for OpenAI, pass
        /// <c>prompt_tokens - cached_tokens</c> as
        /// <paramref name="inputTokens"/>.
        /// </summary>
        public Task RecordAsync(
            string bucket,
            long inputTokens,
            long outputTokens,
            long cachedTokens,
            double costUsd,
            long latencyMs,
            bool? isError = null,
            string? model = null,
            long cacheWrite5m = 0,
            long cacheWrite1h = 0,
            CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "LLMSTATS.RECORD", bucket, inputTokens, outputTokens, cachedTokens, costUsd, latencyMs };
            if (isError.HasValue)
            {
                args.Add("ERROR");
                args.Add(isError.Value ? 1 : 0);
            }
            if (model is not null)
            {
                args.Add("MODEL");
                args.Add(model);
            }
            if (cacheWrite5m > 0)
            {
                args.Add("CACHEWRITE5M");
                args.Add(cacheWrite5m);
            }
            if (cacheWrite1h > 0)
            {
                args.Add("CACHEWRITE1H");
                args.Add(cacheWrite1h);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>LLMSTATS.GET bucket.</summary>
        public async Task<LlmStatsSnapshot> GetAsync(string bucket, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.GET", bucket }, cancellationToken).ConfigureAwait(false);
            return LlmStatsSnapshot.FromReply(reply);
        }

        /// <summary>LLMSTATS.RESET bucket.</summary>
        public async Task<bool> ResetAsync(string bucket, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.RESET", bucket }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>LLMSTATS.DROP bucket.</summary>
        public async Task<bool> DropAsync(string bucket, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.DROP", bucket }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>LLMSTATS.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>
        /// LLMSTATS.PRICE SET model INPUT usd OUTPUT usd [CACHEREAD usd]
        /// [CACHEWRITE5M usd] [CACHEWRITE1H usd]. Sets (or replaces) a
        /// custom price override for <paramref name="model"/>, shared
        /// with <see cref="RecordAsync"/>'s and
        /// <see cref="LlmBudgetCommands.ConsumeAsync"/>'s MODEL option.
        /// Prices are USD per 1,000,000 tokens. Omitted cache prices
        /// default (server side) to 0.1x/1.25x/2x the input price.
        /// </summary>
        public Task PriceSetAsync(
            string model,
            double inputUsdPerMtok,
            double outputUsdPerMtok,
            double? cacheReadUsdPerMtok = null,
            double? cacheWrite5mUsdPerMtok = null,
            double? cacheWrite1hUsdPerMtok = null,
            CancellationToken cancellationToken = default)
        {
            var args = new List<object?>
            {
                "LLMSTATS.PRICE", "SET", model,
                "INPUT", FormatUsdPerMtok(inputUsdPerMtok),
                "OUTPUT", FormatUsdPerMtok(outputUsdPerMtok),
            };
            if (cacheReadUsdPerMtok.HasValue)
            {
                args.Add("CACHEREAD");
                args.Add(FormatUsdPerMtok(cacheReadUsdPerMtok.Value));
            }
            if (cacheWrite5mUsdPerMtok.HasValue)
            {
                args.Add("CACHEWRITE5M");
                args.Add(FormatUsdPerMtok(cacheWrite5mUsdPerMtok.Value));
            }
            if (cacheWrite1hUsdPerMtok.HasValue)
            {
                args.Add("CACHEWRITE1H");
                args.Add(FormatUsdPerMtok(cacheWrite1hUsdPerMtok.Value));
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>LLMSTATS.PRICE GET model. An unknown model replies with source "unpriced", priced false, not an error.</summary>
        public async Task<LlmPrice> PriceGetAsync(string model, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.PRICE", "GET", model }, cancellationToken).ConfigureAwait(false);
            return LlmPrice.FromReply(reply);
        }

        /// <summary>LLMSTATS.PRICE LIST -> every known model's price sheet (built-in and custom).</summary>
        public async Task<IReadOnlyList<LlmPrice>> PriceListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.PRICE", "LIST" }, cancellationToken).ConfigureAwait(false);
            var list = new List<LlmPrice>();
            foreach (var item in reply.AsArray())
            {
                list.Add(LlmPrice.FromReply(item));
            }
            return list;
        }

        /// <summary>LLMSTATS.PRICE DEL model -> whether a custom override existed and was removed (the model then falls back to its built-in price, if any).</summary>
        public async Task<bool> PriceDelAsync(string model, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMSTATS.PRICE", "DEL", model }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>Formats a USD-per-1,000,000-token price as a plain decimal string, never an exponent, up to 6 fractional digits.</summary>
        internal static string FormatUsdPerMtok(double usdPerMtok) => NumberFormat.Usd(usdPerMtok);
    }
}
