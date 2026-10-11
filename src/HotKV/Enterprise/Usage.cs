// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>
    /// The shared "token usage group" wire option:
    /// <c>MODEL model [INPUT n] [OUTPUT n] [CACHEREAD n] [CACHEWRITE5M n]
    /// [CACHEWRITE1H n]</c>, accepted by <see cref="HotKV.AI.PromptCache.SetAsync"/>,
    /// <c>SemanticCache.SetAsync</c>, every keyed cache
    /// <see cref="KeyedCacheCommands.SetAsync"/>, and
    /// <see cref="LlmBudgetCommands.ConsumeAsync"/>.
    ///
    /// <see cref="Model"/> is required; the token counts default to 0 and
    /// are omitted from the wire when zero, but MODEL is always sent once
    /// a <see cref="Usage"/> is attached, so the server can price the call
    /// and, on a later cache hit, attribute the tokens (and cost) that hit
    /// did not have to spend.
    /// </summary>
    /// <param name="Model">The model id, e.g. "claude-sonnet-5".</param>
    public sealed record Usage(string Model)
    {
        /// <summary>Uncached prompt tokens.</summary>
        public long Input { get; init; }

        /// <summary>Output/completion tokens.</summary>
        public long Output { get; init; }

        /// <summary>Provider prompt-cache hit tokens.</summary>
        public long CacheRead { get; init; }

        /// <summary>Anthropic cache-write tokens, 5-minute TTL tier.</summary>
        public long CacheWrite5m { get; init; }

        /// <summary>Anthropic cache-write tokens, 1-hour TTL tier.</summary>
        public long CacheWrite1h { get; init; }

        /// <summary>Appends this usage's wire options to <paramref name="args"/> (MODEL always first).</summary>
        internal void AppendTo(List<object?> args)
        {
            args.Add("MODEL");
            args.Add(Model);
            if (Input > 0)
            {
                args.Add("INPUT");
                args.Add(Input);
            }
            if (Output > 0)
            {
                args.Add("OUTPUT");
                args.Add(Output);
            }
            if (CacheRead > 0)
            {
                args.Add("CACHEREAD");
                args.Add(CacheRead);
            }
            if (CacheWrite5m > 0)
            {
                args.Add("CACHEWRITE5M");
                args.Add(CacheWrite5m);
            }
            if (CacheWrite1h > 0)
            {
                args.Add("CACHEWRITE1H");
                args.Add(CacheWrite1h);
            }
        }

        /// <summary>Appends <paramref name="usage"/>'s wire options to <paramref name="args"/> when non-null; a no-op otherwise.</summary>
        internal static void AppendIfPresent(List<object?> args, Usage? usage) => usage?.AppendTo(args);
    }

    /// <summary>
    /// The tokens (and, once the model is priced, the cost) a cache's
    /// hits did not have to spend, accumulated from usage recorded on a
    /// SET call (see <see cref="Usage"/>). Every field is zero when no
    /// SET ever carried a usage value. Reported by
    /// <see cref="HotKV.AI.SemanticCache.StatsAsync"/>,
    /// <see cref="HotKV.AI.PromptCache.StatsAsync"/> and
    /// <see cref="KeyedCacheCommands.StatsAsync"/>.
    /// </summary>
    public sealed record UsageSavings(long Requests, long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens, double CostUsd)
    {
        internal static UsageSavings FromDictionary(IReadOnlyDictionary<string, RespValue> d) => new UsageSavings(
            d.GetInt64("usage_saved_requests"),
            d.GetInt64("usage_saved_input_tokens"),
            d.GetInt64("usage_saved_output_tokens"),
            d.GetInt64("usage_saved_cache_read_tokens"),
            d.GetInt64("usage_saved_cache_write_tokens"),
            d.GetDouble("usage_saved_cost_usd"));
    }
}
