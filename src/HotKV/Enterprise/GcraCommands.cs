// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV
{
    /// <summary>Result of <see cref="GcraCommands.LimitAsync"/>.</summary>
    public sealed record GcraResult(bool Denied, long Limit, long Remaining, long RetryAfterMs, long ResetAfterMs);

    /// <summary>
    /// GCRA.* generic cell rate limiter primitive.
    /// Core: no license feature is required. Distinct from the named
    /// <c>client.RateLimit</c> (RATELIMIT.*) family. Reachable as
    /// <c>client.Gcra</c>.
    /// </summary>
    public sealed class GcraCommands
    {
        private readonly ICommandExecutor _executor;

        internal GcraCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>GCRA.LIMIT key period_ms burst [count].</summary>
        public async Task<GcraResult> LimitAsync(string key, long periodMs, long burst, long count = 1, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "GCRA.LIMIT", key, periodMs, burst, count }, cancellationToken).ConfigureAwait(false);
            var items = reply.AsArray();
            return new GcraResult(items[0].AsInt64() == 1, items[1].AsInt64(), items[2].AsInt64(), items[3].AsInt64(), items[4].AsInt64());
        }

        /// <summary>GCRA.RESET key [key ...] -> number of keys that had state and were reset.</summary>
        public async Task<long> ResetAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "GCRA.RESET" };
            args.AddRange(keys);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>GCRA.RESET key.</summary>
        public Task<long> ResetAsync(string key, CancellationToken cancellationToken = default) => ResetAsync(new[] { key }, cancellationToken);
    }
}
