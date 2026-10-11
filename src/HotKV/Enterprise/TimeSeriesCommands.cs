// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>Aggregation function for TS.AGG/TS.DOWNSAMPLE.</summary>
    public enum TimeSeriesAggregation
    {
        Avg,
        Min,
        Max,
        Sum,
        Count,
    }

    /// <summary>One TS.RANGE/TS.AGG sample.</summary>
    public sealed record TimeSeriesSample(long TimestampMs, double Value);

    /// <summary>TS.INFO reply.</summary>
    public sealed record TimeSeriesInfo(string Key, long TotalSamples, long FirstTimestamp, long LastTimestamp, double LastValue, long RetentionMs, long MemoryBytes, IReadOnlyList<(string Label, string Value)> Labels, long Rules);

    /// <summary>
    /// TS.* time series. Needs
    /// the TimeSeries license feature. Reachable as
    /// <c>client.TimeSeries</c>.
    /// </summary>
    public sealed class TimeSeriesCommands
    {
        private readonly ICommandExecutor _executor;

        internal TimeSeriesCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string AggToken(TimeSeriesAggregation aggregation) => aggregation switch
        {
            TimeSeriesAggregation.Min => "min",
            TimeSeriesAggregation.Max => "max",
            TimeSeriesAggregation.Sum => "sum",
            TimeSeriesAggregation.Count => "count",
            _ => "avg",
        };

        /// <summary>TS.CREATE key [RETENTION ms] [LABELS l v ...]. RETENTION is sent before LABELS: the server reads label values until the RETENTION keyword or end of args.</summary>
        public Task CreateAsync(string key, long? retentionMs = null, IReadOnlyDictionary<string, string>? labels = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "TS.CREATE", key };
            if (retentionMs.HasValue)
            {
                args.Add("RETENTION");
                args.Add(retentionMs.Value);
            }
            if (labels is { Count: > 0 })
            {
                args.Add("LABELS");
                foreach (var kv in labels)
                {
                    args.Add(kv.Key);
                    args.Add(kv.Value);
                }
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>TS.ADD key timestamp-or-* value -> the timestamp (ms) actually used.</summary>
        public async Task<long> AddAsync(string key, string timestampMsOrStar, double value, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TS.ADD", key, timestampMsOrStar, value }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>TS.ADD key timestamp_ms value.</summary>
        public Task<long> AddAsync(string key, long timestampMs, double value, CancellationToken cancellationToken = default) =>
            AddAsync(key, timestampMs.ToString(CultureInfo.InvariantCulture), value, cancellationToken);

        /// <summary>TS.ADD key * value: lets the server assign the current time.</summary>
        public Task<long> AddNowAsync(string key, double value, CancellationToken cancellationToken = default) =>
            AddAsync(key, "*", value, cancellationToken);

        /// <summary>TS.RANGE key from_ms to_ms [COUNT n]. "-"/"+" mean the minimum/maximum timestamp.</summary>
        public async Task<IReadOnlyList<TimeSeriesSample>> RangeAsync(string key, string fromMs = "-", string toMs = "+", long? count = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "TS.RANGE", key, fromMs, toMs };
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseSamples(reply);
        }

        /// <summary>TS.AGG key from_ms to_ms bucket_ms agg_type -> bucketed samples.</summary>
        public async Task<IReadOnlyList<TimeSeriesSample>> AggregateAsync(string key, string fromMs, string toMs, long bucketMs, TimeSeriesAggregation aggregation, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TS.AGG", key, fromMs, toMs, bucketMs, AggToken(aggregation) }, cancellationToken).ConfigureAwait(false);
            return ParseSamples(reply);
        }

        private static IReadOnlyList<TimeSeriesSample> ParseSamples(RespValue reply) =>
            reply.AsArray().Select(item =>
            {
                var pair = item.AsArray();
                return new TimeSeriesSample(pair[0].AsInt64(), pair[1].AsDouble());
            }).ToList();

        /// <summary>TS.INFO key.</summary>
        public async Task<TimeSeriesInfo> InfoAsync(string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TS.INFO", key }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            var labels = new List<(string, string)>();
            if (d.TryGetValue("labels", out var labelsValue) && !labelsValue.IsNull)
            {
                foreach (var pairValue in labelsValue.AsArray())
                {
                    var pair = pairValue.AsArray();
                    labels.Add((pair[0].AsString() ?? string.Empty, pair[1].AsString() ?? string.Empty));
                }
            }
            return new TimeSeriesInfo(d.GetString("key", key), d.GetInt64("totalSamples"), d.GetInt64("firstTimestamp"), d.GetInt64("lastTimestamp"), d.GetDouble("lastValue"), d.GetInt64("retentionMs"), d.GetInt64("memoryBytes"), labels, d.GetInt64("rules"));
        }

        /// <summary>TS.DELETE key.</summary>
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "TS.DELETE", key }, cancellationToken).AsTask();

        /// <summary>TS.RETENTION key retention_ms.</summary>
        public Task SetRetentionAsync(string key, long retentionMs, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "TS.RETENTION", key, retentionMs }, cancellationToken).AsTask();

        /// <summary>TS.DOWNSAMPLE src_key dest_key agg_type bucket_ms: adds a live downsample rule. Both keys must already exist.</summary>
        public Task DownsampleAsync(string sourceKey, string destinationKey, TimeSeriesAggregation aggregation, long bucketMs, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "TS.DOWNSAMPLE", sourceKey, destinationKey, AggToken(aggregation), bucketMs }, cancellationToken).AsTask();

        /// <summary>TS.LIST [LABEL key value].</summary>
        public async Task<IReadOnlyList<string>> ListAsync(string? labelKey = null, string? labelValue = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "TS.LIST" };
            if (labelKey is not null && labelValue is not null)
            {
                args.Add("LABEL");
                args.Add(labelKey);
                args.Add(labelValue);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
