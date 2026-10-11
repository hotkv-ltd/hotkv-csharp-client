// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>Algorithm for a RATELIMIT.* limiter.</summary>
    public enum RateLimitAlgorithm
    {
        SlidingWindow,
        TokenBucket,
        LeakyBucket,
    }

    /// <summary>RATELIMIT.CHECK / RATELIMIT.PEEK reply.</summary>
    public sealed record RateLimitCheckResult(bool Allowed, long Remaining, long RetryAfterMs, long Limit, long Current);

    /// <summary>RATELIMIT.STATS reply.</summary>
    public sealed record RateLimitStats(string Name, string Algorithm, long Limit, long WindowMs, long UniqueKeys, long TotalAllowed, long TotalDenied);

    /// <summary>
    /// RATELIMIT.* named rate limiters. Needs
    /// the RateLimiting license feature. Distinct from the
    /// lower level <c>client.Gcra</c> (GCRA.*) primitive. Reachable as
    /// <c>client.RateLimit</c>.
    /// </summary>
    public sealed class RateLimitCommands
    {
        private readonly ICommandExecutor _executor;

        internal RateLimitCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string AlgoToken(RateLimitAlgorithm algorithm) => algorithm switch
        {
            RateLimitAlgorithm.TokenBucket => "token_bucket",
            RateLimitAlgorithm.LeakyBucket => "leaky_bucket",
            _ => "sliding_window",
        };

        /// <summary>RATELIMIT.CREATE name limit window_ms [ALGO ...] [REFILL rate] [DRAIN rate]. REFILL/DRAIN are algorithm-specific tuning and must be greater than 0 when given.</summary>
        public Task CreateAsync(string name, long limit, long windowMs, RateLimitAlgorithm algorithm = RateLimitAlgorithm.SlidingWindow, double? refillRate = null, double? drainRate = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "RATELIMIT.CREATE", name, limit, windowMs, "ALGO", AlgoToken(algorithm) };
            if (refillRate.HasValue)
            {
                args.Add("REFILL");
                args.Add(refillRate.Value);
            }
            if (drainRate.HasValue)
            {
                args.Add("DRAIN");
                args.Add(drainRate.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>RATELIMIT.CHECK name key [COST n]. Mutating: advances limiter state.</summary>
        public async Task<RateLimitCheckResult> CheckAsync(string name, string key, long? cost = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "RATELIMIT.CHECK", name, key };
            if (cost.HasValue)
            {
                args.Add("COST");
                args.Add(cost.Value);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseCheck(reply);
        }

        /// <summary>RATELIMIT.PEEK name key: same shape as CHECK but does not consume.</summary>
        public async Task<RateLimitCheckResult> PeekAsync(string name, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "RATELIMIT.PEEK", name, key }, cancellationToken).ConfigureAwait(false);
            return ParseCheck(reply);
        }

        private static RateLimitCheckResult ParseCheck(RespValue reply)
        {
            var items = reply.AsArray();
            return new RateLimitCheckResult(items[0].AsInt64() == 1, items[1].AsInt64(), items[2].AsInt64(), items[3].AsInt64(), items[4].AsInt64());
        }

        /// <summary>RATELIMIT.RESET name key.</summary>
        public Task ResetAsync(string name, string key, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "RATELIMIT.RESET", name, key }, cancellationToken).AsTask();

        /// <summary>RATELIMIT.STATS name.</summary>
        public async Task<RateLimitStats> StatsAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "RATELIMIT.STATS", name }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new RateLimitStats(d.GetString("name"), d.GetString("algo"), d.GetInt64("limit"), d.GetInt64("window_ms"), d.GetInt64("unique_keys"), d.GetInt64("total_allowed"), d.GetInt64("total_denied"));
        }

        /// <summary>RATELIMIT.DROP name.</summary>
        public Task DropAsync(string name, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "RATELIMIT.DROP", name }, cancellationToken).AsTask();

        /// <summary>RATELIMIT.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "RATELIMIT.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
