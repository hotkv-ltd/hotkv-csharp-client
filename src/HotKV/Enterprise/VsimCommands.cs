// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV
{
    /// <summary>Distance metric for a VSIM index.</summary>
    public enum VectorMetric
    {
        Cosine,
        L2,
        Ip,
    }

    /// <summary>One VSIM.SEARCH result.</summary>
    public sealed record VsimSearchResult(string Key, double Distance);

    /// <summary>VSIM.INFO for one index.</summary>
    public sealed record VsimIndexInfo(string Name, long Dimensions, string Metric, long Vectors, long M, long EfConstruction, long EfSearch);

    /// <summary>
    /// VSIM.* vector similarity index commands.
    /// Core except persistence (SAVE/LOAD/LIST/DROP, which needs
    /// the VSIMPersist license feature). Reachable as <c>client.Vsim</c>.
    /// </summary>
    public sealed class VsimCommands
    {
        private readonly ICommandExecutor _executor;

        internal VsimCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string MetricToken(VectorMetric metric) => metric switch
        {
            VectorMetric.L2 => "l2",
            VectorMetric.Ip => "ip",
            _ => "cosine",
        };

        /// <summary>VSIM.CREATEINDEX name dims [METRIC cosine|l2|ip] [M n] [EF_CONSTRUCTION n] [EF_SEARCH n]. dims must be in 1..65536.</summary>
        public Task CreateIndexAsync(string name, int dimensions, VectorMetric metric = VectorMetric.Cosine, int? m = null, int? efConstruction = null, int? efSearch = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "VSIM.CREATEINDEX", name, dimensions, "METRIC", MetricToken(metric) };
            if (m.HasValue)
            {
                args.Add("M");
                args.Add(m.Value);
            }
            if (efConstruction.HasValue)
            {
                args.Add("EF_CONSTRUCTION");
                args.Add(efConstruction.Value);
            }
            if (efSearch.HasValue)
            {
                args.Add("EF_SEARCH");
                args.Add(efSearch.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>VSIM.DROPINDEX name.</summary>
        public Task DropIndexAsync(string name, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "VSIM.DROPINDEX", name }, cancellationToken).AsTask();

        /// <summary>VSIM.SET index key val1 .. valN -> true if newly inserted, false if it replaced an existing vector.</summary>
        public Task<bool> SetAsync(string index, string key, IReadOnlyList<double> vector, CancellationToken cancellationToken = default) =>
            SetCoreAsync(index, key, vector.Cast<object?>(), cancellationToken);

        /// <summary>VSIM.SET index key val1 .. valN with a float32 vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<bool> SetAsync(string index, string key, IReadOnlyList<float> vector, CancellationToken cancellationToken = default) =>
            SetCoreAsync(index, key, vector.Cast<object?>(), cancellationToken);

        private async Task<bool> SetCoreAsync(string index, string key, IEnumerable<object?> vector, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "VSIM.SET", index, key };
            args.AddRange(vector);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>VSIM.GET index key -> the stored vector, or null if key is not in the index.</summary>
        public async Task<IReadOnlyList<double>?> GetAsync(string index, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.GET", index, key }, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            return reply.AsArray().Select(v => double.Parse(v.AsString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture)).ToList();
        }

        /// <summary>VSIM.DEL index key.</summary>
        public async Task<bool> DeleteAsync(string index, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.DEL", index, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>VSIM.SEARCH index k val1 .. valN [FILTER tag ...] [EF ef] -> up to k nearest neighbors, nearest first.</summary>
        public Task<IReadOnlyList<VsimSearchResult>> SearchAsync(string index, int k, IReadOnlyList<double> vector, IReadOnlyList<string>? filterTags = null, int? ef = null, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(index, k, vector.Cast<object?>(), filterTags, ef, cancellationToken);

        /// <summary>VSIM.SEARCH with a float32 query vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<IReadOnlyList<VsimSearchResult>> SearchAsync(string index, int k, IReadOnlyList<float> vector, IReadOnlyList<string>? filterTags = null, int? ef = null, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(index, k, vector.Cast<object?>(), filterTags, ef, cancellationToken);

        private async Task<IReadOnlyList<VsimSearchResult>> SearchCoreAsync(string index, int k, IEnumerable<object?> vector, IReadOnlyList<string>? filterTags, int? ef, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "VSIM.SEARCH", index, k };
            args.AddRange(vector);
            if (filterTags is { Count: > 0 })
            {
                args.Add("FILTER");
                args.AddRange(filterTags);
            }
            if (ef.HasValue)
            {
                args.Add("EF");
                args.Add(ef.Value);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            var items = reply.AsArray();
            var results = new List<VsimSearchResult>(items.Count / 2);
            for (int i = 0; i + 1 < items.Count; i += 2)
            {
                results.Add(new VsimSearchResult(items[i].AsString() ?? string.Empty, double.Parse(items[i + 1].AsString() ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture)));
            }
            return results;
        }

        /// <summary>VSIM.INFO (no index) -> every index name.</summary>
        public async Task<IReadOnlyList<string>> ListIndexesAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.INFO" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>VSIM.INFO index.</summary>
        public async Task<VsimIndexInfo> InfoAsync(string index, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.INFO", index }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new VsimIndexInfo(
                d.TryGetValue("name", out var n) ? n.AsString() ?? index : index,
                d.TryGetValue("dimensions", out var dim) ? dim.AsInt64() : 0,
                d.TryGetValue("metric", out var met) ? met.AsString() ?? "cosine" : "cosine",
                d.TryGetValue("vectors", out var vc) ? vc.AsInt64() : 0,
                d.TryGetValue("M", out var mm) ? mm.AsInt64() : 0,
                d.TryGetValue("ef_construction", out var efc) ? efc.AsInt64() : 0,
                d.TryGetValue("ef_search", out var efs) ? efs.AsInt64() : 0);
        }

        // -- persistence: the VSIMPersist license feature --

        /// <summary>VSIM.SAVE snapshot_name blob -> bytes written. Needs the VSIMPersist license feature.</summary>
        public async Task<long> SaveSnapshotAsync(string snapshotName, byte[] blob, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.SAVE", snapshotName, blob }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>VSIM.LOAD snapshot_name -> the blob, or null if not found. Needs the VSIMPersist license feature.</summary>
        public async Task<byte[]?> LoadSnapshotAsync(string snapshotName, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.LOAD", snapshotName }, cancellationToken).ConfigureAwait(false);
            return reply.AsBytes();
        }

        /// <summary>VSIM.LIST -> snapshot names. Needs the VSIMPersist license feature.</summary>
        public async Task<IReadOnlyList<string>> ListSnapshotsAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>VSIM.DROP snapshot_name. Needs the VSIMPersist license feature.</summary>
        public async Task<bool> DropSnapshotAsync(string snapshotName, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "VSIM.DROP", snapshotName }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }
    }
}
