// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;
using HotKV.Internal;

namespace HotKV
{
    /// <summary>Chunking strategy for a RAG pipeline.</summary>
    public enum RagChunkStrategy
    {
        Fixed,
        Sentence,
        Paragraph,
    }

    /// <summary>One RAG.SEARCH result.</summary>
    public sealed record RagSearchResult(string ChunkId, string DocId, string Text, double Score, double VectorScore, double Bm25Score, IReadOnlyDictionary<string, string> Metadata);

    /// <summary>RAG.STATS reply.</summary>
    public sealed record RagStats(string Name, long Dimensions, long TotalDocs, long TotalChunks, long TotalIngested, long TotalQueries, long ChunkSize, string ChunkStrategy, long ChunkOverlap);

    /// <summary>
    /// RAG.* retrieval augmented generation pipelines. Needs the RAGPipeline license feature. Reachable
    /// as <c>client.Rag</c>.
    /// </summary>
    public sealed class RagCommands
    {
        private readonly ICommandExecutor _executor;

        internal RagCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        private static string StrategyToken(RagChunkStrategy strategy) => strategy switch
        {
            RagChunkStrategy.Sentence => "sentence",
            RagChunkStrategy.Paragraph => "paragraph",
            _ => "fixed",
        };

        /// <summary>RAG.CREATE name dimensions [STRATEGY fixed|sentence|paragraph] [CHUNK_SIZE n] [CHUNK_OVERLAP n].</summary>
        public Task CreateAsync(string name, int dimensions, RagChunkStrategy strategy = RagChunkStrategy.Fixed, int? chunkSize = null, int? chunkOverlap = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "RAG.CREATE", name, dimensions, "STRATEGY", StrategyToken(strategy) };
            if (chunkSize.HasValue)
            {
                args.Add("CHUNK_SIZE");
                args.Add(chunkSize.Value);
            }
            if (chunkOverlap.HasValue)
            {
                args.Add("CHUNK_OVERLAP");
                args.Add(chunkOverlap.Value);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>
        /// RAG.INGEST pipeline doc_id text [META k v ...] [VECTORS vec ...]
        /// -> chunks ingested. Each entry of <paramref name="vectors"/> is
        /// one chunk's embedding, sent as a comma-joined float list; give
        /// one per chunk <paramref name="text"/> produces, in order.
        /// </summary>
        public Task<long> IngestAsync(string pipeline, string docId, string text, IReadOnlyDictionary<string, string>? meta = null, IReadOnlyList<IReadOnlyList<double>>? vectors = null, CancellationToken cancellationToken = default) =>
            IngestCoreAsync(pipeline, docId, text, meta, vectors?.Select(v => v.Select(NumberFormat.Double)), cancellationToken);

        /// <summary>RAG.INGEST with float32 chunk embeddings: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<long> IngestAsync(string pipeline, string docId, string text, IReadOnlyDictionary<string, string>? meta, IReadOnlyList<IReadOnlyList<float>> vectors, CancellationToken cancellationToken = default) =>
            IngestCoreAsync(pipeline, docId, text, meta, vectors?.Select(v => v.Select(NumberFormat.Single)), cancellationToken);

        private async Task<long> IngestCoreAsync(string pipeline, string docId, string text, IReadOnlyDictionary<string, string>? meta, IEnumerable<IEnumerable<string>>? vectors, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "RAG.INGEST", pipeline, docId, text };
            if (meta is { Count: > 0 })
            {
                args.Add("META");
                foreach (var kv in meta)
                {
                    args.Add(kv.Key);
                    args.Add(kv.Value);
                }
            }
            var vectorList = vectors?.ToList();
            if (vectorList is { Count: > 0 })
            {
                args.Add("VECTORS");
                foreach (var vector in vectorList)
                {
                    args.Add(string.Join(",", vector));
                }
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>
        /// RAG.SEARCH pipeline query_text [K n] [ALPHA f] [VECTOR v,v,...]
        /// [FILTER k v ...]. K defaults to 5, ALPHA (vector-vs-BM25 blend)
        /// to 0.5.
        /// </summary>
        public Task<IReadOnlyList<RagSearchResult>> SearchAsync(string pipeline, string queryText, int? k = null, double? alpha = null, IReadOnlyList<double>? vector = null, IReadOnlyDictionary<string, string>? filter = null, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(pipeline, queryText, k, alpha, vector?.Select(NumberFormat.Double), filter, cancellationToken);

        /// <summary>RAG.SEARCH with a float32 query vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<IReadOnlyList<RagSearchResult>> SearchAsync(string pipeline, string queryText, int? k, double? alpha, IReadOnlyList<float> vector, IReadOnlyDictionary<string, string>? filter = null, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(pipeline, queryText, k, alpha, vector?.Select(NumberFormat.Single), filter, cancellationToken);

        private async Task<IReadOnlyList<RagSearchResult>> SearchCoreAsync(string pipeline, string queryText, int? k, double? alpha, IEnumerable<string>? vector, IReadOnlyDictionary<string, string>? filter, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "RAG.SEARCH", pipeline, queryText };
            if (k.HasValue)
            {
                args.Add("K");
                args.Add(k.Value);
            }
            if (alpha.HasValue)
            {
                args.Add("ALPHA");
                args.Add(alpha.Value);
            }
            var vectorText = vector is null ? null : string.Join(",", vector);
            if (!string.IsNullOrEmpty(vectorText))
            {
                args.Add("VECTOR");
                args.Add(vectorText);
            }
            // FILTER must come last: the server reads filter pairs greedily
            // until K/ALPHA/VECTOR or end of args.
            if (filter is { Count: > 0 })
            {
                args.Add("FILTER");
                foreach (var kv in filter)
                {
                    args.Add(kv.Key);
                    args.Add(kv.Value);
                }
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(ParseResult).ToList();
        }

        private static RagSearchResult ParseResult(RespValue item)
        {
            var parts = item.AsArray();
            var meta = new Dictionary<string, string>();
            for (int i = 6; i + 1 < parts.Count; i += 2)
            {
                meta[parts[i].AsString() ?? string.Empty] = parts[i + 1].AsString() ?? string.Empty;
            }
            return new RagSearchResult(
                parts[0].AsString() ?? string.Empty,
                parts[1].AsString() ?? string.Empty,
                parts[2].AsString() ?? string.Empty,
                parts[3].AsDouble(),
                parts[4].AsDouble(),
                parts[5].AsDouble(),
                meta);
        }

        /// <summary>RAG.STATS pipeline.</summary>
        public async Task<RagStats> StatsAsync(string pipeline, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "RAG.STATS", pipeline }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return new RagStats(d.GetString("name"), d.GetInt64("dimensions"), d.GetInt64("totalDocs"), d.GetInt64("totalChunks"), d.GetInt64("totalIngested"), d.GetInt64("totalQueries"), d.GetInt64("chunkSize"), d.GetString("chunkStrategy"), d.GetInt64("chunkOverlap"));
        }

        /// <summary>RAG.DROP pipeline.</summary>
        public Task DropAsync(string pipeline, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "RAG.DROP", pipeline }, cancellationToken).AsTask();

        /// <summary>RAG.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "RAG.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
