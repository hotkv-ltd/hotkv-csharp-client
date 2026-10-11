// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;
using HotKV.Internal;

namespace HotKV
{
    /// <summary>LLMBUDGET.STATUS reply.</summary>
    public sealed record LlmBudgetStatus(
        long TokensUsed,
        long TokenLimit,
        double CostUsedUsd,
        double CostLimitUsd,
        long WindowSeconds,
        long WindowRemaining,
        long RequestsAllowed,
        long RequestsRejected,
        long UnpricedRequests)
    {
        internal static LlmBudgetStatus FromReply(RespValue reply)
        {
            var d = reply.AsDictionary();
            return new LlmBudgetStatus(
                d.GetInt64("tokens_used"), d.GetInt64("token_limit"), d.GetDouble("cost_used_usd"), d.GetDouble("cost_limit_usd"),
                d.GetInt64("window_seconds"), d.GetInt64("window_remaining"), d.GetInt64("requests_allowed"), d.GetInt64("requests_rejected"),
                d.GetInt64("unpriced_requests"));
        }
    }

    /// <summary>
    /// LLMBUDGET.* spend and token caps. Needs
    /// the LLMBudget license feature. Reachable as <c>client.LlmBudget</c>.
    /// </summary>
    public sealed class LlmBudgetCommands
    {
        private readonly ICommandExecutor _executor;

        internal LlmBudgetCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>LLMBUDGET.SET name token_limit cost_limit_usd window_seconds.</summary>
        public Task SetAsync(string name, long tokenLimit, double costLimitUsd, long windowSeconds, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "LLMBUDGET.SET", name, tokenLimit, NumberFormat.Usd(costLimitUsd), windowSeconds }, cancellationToken).AsTask();

        /// <summary>
        /// LLMBUDGET.CONSUME name tokens cost_usd [usage group] -> true if
        /// allowed, false if it would exceed the cap for the current
        /// window. When <paramref name="usage"/> is given,
        /// <paramref name="tokens"/>/<paramref name="costUsd"/> are
        /// ignored server side (pass 0/0.0): the token count is the sum
        /// of <paramref name="usage"/>'s counts, and the cost is computed
        /// from the price table (see <see cref="LlmStatsCommands.PriceSetAsync"/>);
        /// an unpriced model still consumes the token budget but costs
        /// $0, counted in <see cref="LlmBudgetStatus.UnpricedRequests"/>.
        /// </summary>
        public async Task<bool> ConsumeAsync(string name, long tokens, double costUsd, Usage? usage = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "LLMBUDGET.CONSUME", name, tokens, costUsd };
            Usage.AppendIfPresent(args, usage);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>LLMBUDGET.STATUS name.</summary>
        public async Task<LlmBudgetStatus> StatusAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMBUDGET.STATUS", name }, cancellationToken).ConfigureAwait(false);
            return LlmBudgetStatus.FromReply(reply);
        }

        /// <summary>LLMBUDGET.RESET name.</summary>
        public async Task<bool> ResetAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMBUDGET.RESET", name }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>LLMBUDGET.DROP name.</summary>
        public async Task<bool> DropAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMBUDGET.DROP", name }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>LLMBUDGET.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "LLMBUDGET.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
