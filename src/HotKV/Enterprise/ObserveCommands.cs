// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV
{
    /// <summary>OBSERVE INFO reply.</summary>
    public sealed record ObserveInfo(long UptimeSeconds, long ConnectedClients, long TotalCommands, long UsedMemoryBytes, long PeakMemoryBytes, long TotalConnections, string EnterpriseFeatures);

    /// <summary>OBSERVE METRICS reply.</summary>
    public sealed record ObserveMetrics(long UptimeSeconds, long ConnectedClients, long TotalCommands, long TotalConnections, long UsedMemoryBytes, long PeakMemoryBytes, long SlowlogLength, long AlertsSent, long AlertsFailed, long NumWorkers);

    /// <summary>ALERTS INFO reply.</summary>
    public sealed record AlertsInfo(bool Enabled, bool FlagEnabled, string WebhookUrl, string Provider, string SupportedProviders, string MinSeverity, long AlertsSent, long AlertsFailed, long AlertsDropped, long AlertsRateLimited, long AlertsDeduped, long AlertsFiltered, long AlertsQueued);

    /// <summary>
    /// OBSERVE and ALERTS operational metrics and alerting. Needs the Observability license feature; both are admin
    /// operations. Reachable as <c>client.Observe</c>.
    /// </summary>
    public sealed class ObserveCommands
    {
        private readonly ICommandExecutor _executor;

        internal ObserveCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static long L(IReadOnlyDictionary<string, HotKV.Connection.RespValue> d, string key) => d.TryGetValue(key, out var v) ? v.AsInt64() : 0;
        private static string S(IReadOnlyDictionary<string, HotKV.Connection.RespValue> d, string key) => d.TryGetValue(key, out var v) ? v.AsString() ?? string.Empty : string.Empty;
        private static bool B(IReadOnlyDictionary<string, HotKV.Connection.RespValue> d, string key) => L(d, key) == 1;

        /// <summary>OBSERVE INFO.</summary>
        public async Task<ObserveInfo> InfoAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "OBSERVE", "INFO" }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new ObserveInfo(L(d, "uptime_seconds"), L(d, "connected_clients"), L(d, "total_commands"), L(d, "used_memory_bytes"), L(d, "peak_memory_bytes"), L(d, "total_connections"), S(d, "enterprise_features"));
        }

        /// <summary>OBSERVE METRICS.</summary>
        public async Task<ObserveMetrics> MetricsAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "OBSERVE", "METRICS" }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new ObserveMetrics(L(d, "uptime_seconds"), L(d, "connected_clients"), L(d, "total_commands"), L(d, "total_connections"), L(d, "used_memory_bytes"), L(d, "peak_memory_bytes"), L(d, "slowlog_length"), L(d, "alerts_sent"), L(d, "alerts_failed"), L(d, "num_workers"));
        }

        /// <summary>ALERTS INFO.</summary>
        public async Task<AlertsInfo> AlertsInfoAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "ALERTS", "INFO" }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new AlertsInfo(B(d, "enabled"), B(d, "flag_enabled"), S(d, "webhook_url"), S(d, "provider"), S(d, "supported_providers"), S(d, "min_severity"), L(d, "alerts_sent"), L(d, "alerts_failed"), L(d, "alerts_dropped"), L(d, "alerts_rate_limited"), L(d, "alerts_deduped"), L(d, "alerts_filtered"), L(d, "alerts_queued"));
        }

        /// <summary>ALERTS TEST [info|warning|critical] [message ...] -> a simple string starting "OK" on success; throws otherwise (filtered/rate-limited/deduped/queue full).</summary>
        public async Task<string> AlertsTestAsync(string? severity = null, string? message = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "ALERTS", "TEST" };
            if (severity is not null)
            {
                args.Add(severity);
            }
            if (message is not null)
            {
                args.Add(message);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>ALERTS SETURL url. Empty string disables alerting.</summary>
        public async Task<string> SetWebhookUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "ALERTS", "SETURL", url }, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>ALERTS SETSEVERITY info|warning|critical.</summary>
        public async Task<string> SetMinSeverityAsync(string severity, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "ALERTS", "SETSEVERITY", severity }, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>ALERTS ON / ALERTS OFF: toggles the master flag without clearing the webhook URL.</summary>
        public async Task<string> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "ALERTS", enabled ? "ON" : "OFF" }, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }
    }
}
