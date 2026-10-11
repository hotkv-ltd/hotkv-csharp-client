// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>Value type of one feature in a FEATURE.CREATE schema.</summary>
    public enum FeatureType
    {
        Int64,
        Double,
        String,
        IntArray,
        DoubleArray,
    }

    /// <summary>FEATURE.GET / one element of FEATURE.BATCH.</summary>
    public sealed record FeatureRecord(IReadOnlyDictionary<string, string> Values, long UpdatedAtMs, long FreshnessMs);

    /// <summary>FEATURE.STATS reply.</summary>
    public sealed record FeatureStoreStats(long Entities, long TotalReads, long TotalWrites, long MemoryBytes);

    /// <summary>
    /// FEATURE.* online feature store. Needs
    /// the FeatureStore license feature. Reachable as
    /// <c>client.FeatureStore</c>.
    /// </summary>
    public sealed class FeatureStoreCommands
    {
        private readonly ICommandExecutor _executor;

        internal FeatureStoreCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string TypeToken(FeatureType type) => type switch
        {
            FeatureType.Double => "double",
            FeatureType.String => "string",
            FeatureType.IntArray => "int_array",
            FeatureType.DoubleArray => "double_array",
            _ => "int64",
        };

        /// <summary>FEATURE.CREATE group description SCHEMA name type [name type ...].</summary>
        public Task CreateAsync(string group, string description, IReadOnlyList<(string Name, FeatureType Type)> schema, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "FEATURE.CREATE", group, description, "SCHEMA" };
            foreach (var (name, type) in schema)
            {
                args.Add(name);
                args.Add(TypeToken(type));
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>FEATURE.SET group entity feature value [feature value ...]. Each value is parsed per that feature's schema type; invalid numeric text silently becomes 0/0.0 server side.</summary>
        public Task SetAsync(string group, string entity, IReadOnlyDictionary<string, string> features, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "FEATURE.SET", group, entity };
            foreach (var kv in features)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>FEATURE.SETAT group entity unix-ms feature value [feature value ...]: like <see cref="SetAsync"/> but stamps the entity with <paramref name="at"/> (positive and within 100 years of now, else the server replies ERR invalid timestamp). The way to load history.</summary>
        public Task SetAtAsync(string group, string entity, DateTimeOffset at, IReadOnlyDictionary<string, string> features, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "FEATURE.SETAT", group, entity, at.ToUnixTimeMilliseconds() };
            foreach (var kv in features)
            {
                args.Add(kv.Key);
                args.Add(kv.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>FEATURE.GET group entity [feature ...]. Omit the feature list to get every feature for the entity.</summary>
        public async Task<FeatureRecord?> GetAsync(string group, string entity, IReadOnlyList<string>? features = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "FEATURE.GET", group, entity };
            if (features is { Count: > 0 })
            {
                args.AddRange(features);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseRecord(reply);
        }

        /// <summary>FEATURE.BATCH group entity [entity ...] [FEATURES f ...] -> one result per entity, in order.</summary>
        public async Task<IReadOnlyList<FeatureRecord?>> BatchGetAsync(string group, IReadOnlyList<string> entities, IReadOnlyList<string>? features = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "FEATURE.BATCH", group };
            args.AddRange(entities);
            if (features is { Count: > 0 })
            {
                args.Add("FEATURES");
                args.AddRange(features);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(ParseRecord).ToList();
        }

        private static FeatureRecord? ParseRecord(RespValue item)
        {
            if (item.IsNull)
            {
                return null;
            }
            var parts = item.AsArray();
            var values = new Dictionary<string, string>();
            foreach (var (k, v) in parts[0].AsPairs())
            {
                values[k.AsString() ?? string.Empty] = v.AsString() ?? string.Empty;
            }
            return new FeatureRecord(values, parts[1].AsInt64(), parts[2].AsInt64());
        }

        /// <summary>FEATURE.SCHEMA group -> (name, type) per feature.</summary>
        public async Task<IReadOnlyList<(string Name, string Type)>> SchemaAsync(string group, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "FEATURE.SCHEMA", group }, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(item =>
            {
                var pair = item.AsArray();
                return (pair[0].AsString() ?? string.Empty, pair[1].AsString() ?? string.Empty);
            }).ToList();
        }

        /// <summary>FEATURE.LIST -> group names.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "FEATURE.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>FEATURE.DROP group.</summary>
        public Task DropAsync(string group, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "FEATURE.DROP", group }, cancellationToken).AsTask();

        /// <summary>FEATURE.STATS group.</summary>
        public async Task<FeatureStoreStats> StatsAsync(string group, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "FEATURE.STATS", group }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new FeatureStoreStats(d.GetInt64("entities"), d.GetInt64("totalReads"), d.GetInt64("totalWrites"), d.GetInt64("memoryBytes"));
        }
    }
}
