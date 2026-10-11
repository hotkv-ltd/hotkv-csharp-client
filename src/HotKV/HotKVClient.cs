// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Commands;
using HotKV.Connection;
using HotKV.Pipelines;
using HotKV.PubSub;
using HotKV.Transactions;

namespace HotKV
{
    /// <summary>
    /// A pooled, async-first client for a single HotKV server (or a single
    /// node; see <see cref="HotKV.Cluster.HotKVCluster"/> for cluster-aware routing).
    /// Every core command family (strings, keys,
    /// lists, sets, hashes, sorted sets, streams, geo, bitmaps, HyperLogLog,
    /// server) is available as extension methods on <see cref="ICommandExecutor"/>,
    /// which <see cref="HotKVClient"/> implements directly, so
    /// <c>client.StringGetAsync(...)</c> and friends are always in scope.
    /// The HotKV exclusive and AI commands are grouped under
    /// typed sub-APIs such as <see cref="SemanticCache"/>,
    /// <see cref="PromptCache"/>, <see cref="AgentMemory"/> and
    /// <see cref="Rag"/>.
    /// </summary>
    public sealed partial class HotKVClient : ICommandExecutor, IAsyncDisposable, IDisposable
    {
        private readonly ConnectionPool _pool;
        private bool _disposed;

        /// <summary>The options this client was created with.</summary>
        public HotKVClientOptions Options { get; }

        /// <summary>Raw wrapper over the SCACHE.* commands plus the idiomatic embedding based get-or-compute helper. Needs the SemanticCache license feature.</summary>
        public HotKV.AI.SemanticCache SemanticCache { get; }

        /// <summary>Raw wrapper over the PCACHE.* commands plus the idiomatic get-or-call LLM response cache. Needs the PromptCache license feature.</summary>
        public HotKV.AI.PromptCache PromptCache { get; }

        /// <summary>AMEM.* conversation, semantic, working and episodic agent memory. Every AMEM.* command needs the AgentMemory license feature on server 0.3.0 (the rolling summaries also need AMemSummary).</summary>
        public AgentMemoryCommands AgentMemory { get; }

        /// <summary>RAG.* retrieval augmented generation pipelines. Needs the RAGPipeline license feature.</summary>
        public RagCommands Rag { get; }

        /// <summary>LLMSTATS.* usage accounting buckets. Needs the LLMStats license feature.</summary>
        public LlmStatsCommands LlmStats { get; }

        /// <summary>LLMBUDGET.* spend/token caps. Needs the LLMBudget license feature.</summary>
        public LlmBudgetCommands LlmBudget { get; }

        /// <summary>PROMPT.* versioned prompt template registry. Needs the PromptRegistry license feature.</summary>
        public PromptRegistryCommands PromptRegistry { get; }

        /// <summary>TOOL.CACHE.* tool call result cache. Needs the ToolCache license feature.</summary>
        public KeyedCacheCommands ToolCache { get; }

        /// <summary>NCACHE.* negative/"don't retry" cache. Needs the NegativeCache license feature.</summary>
        public KeyedCacheCommands NegativeCache { get; }

        /// <summary>GUARD.* guardrail/moderation decision cache. Needs the GuardCache license feature.</summary>
        public KeyedCacheCommands GuardCache { get; }

        /// <summary>RERANK.* reranker result cache. Needs the RerankCache license feature.</summary>
        public KeyedCacheCommands RerankCache { get; }

        /// <summary>EMBED.DEDUP.* embedding dedupe/refcount store. Needs the EmbedDedup license feature.</summary>
        public EmbedDedupCommands EmbedDedup { get; }

        /// <summary>RATELIMIT.* named rate limiters. Needs the RateLimiting license feature.</summary>
        public RateLimitCommands RateLimit { get; }

        /// <summary>TS.* time series. Needs the TimeSeries license feature.</summary>
        public TimeSeriesCommands TimeSeries { get; }

        /// <summary>FEATURE.* online feature store. Needs the FeatureStore license feature.</summary>
        public FeatureStoreCommands FeatureStore { get; }

        /// <summary>TAG.* key tagging (core, no license required).</summary>
        public TagCommands Tags { get; }

        /// <summary>VSIM.* vector similarity indexes (core except SAVE/LOAD/LIST/DROP persistence, which needs the VSIMPersist license feature).</summary>
        public VsimCommands Vsim { get; }

        /// <summary>GCRA.* generic cell rate limiter primitive (core, no license required).</summary>
        public GcraCommands Gcra { get; }

        /// <summary>OBSERVE/ALERTS operational metrics and alerting. Needs the Observability license feature.</summary>
        public ObserveCommands Observe { get; }

        private HotKVClient(HotKVClientOptions options)
        {
            Options = options;
            _pool = new ConnectionPool(options);
            SemanticCache = new HotKV.AI.SemanticCache(this);
            PromptCache = new HotKV.AI.PromptCache(this);
            AgentMemory = new AgentMemoryCommands(this);
            Rag = new RagCommands(this);
            LlmStats = new LlmStatsCommands(this);
            LlmBudget = new LlmBudgetCommands(this);
            PromptRegistry = new PromptRegistryCommands(this);
            ToolCache = new KeyedCacheCommands(this, "TOOL.CACHE");
            NegativeCache = new KeyedCacheCommands(this, "NCACHE");
            GuardCache = new KeyedCacheCommands(this, "GUARD");
            RerankCache = new KeyedCacheCommands(this, "RERANK");
            EmbedDedup = new EmbedDedupCommands(this);
            RateLimit = new RateLimitCommands(this);
            TimeSeries = new TimeSeriesCommands(this);
            FeatureStore = new FeatureStoreCommands(this);
            Tags = new TagCommands(this);
            Vsim = new VsimCommands(this);
            Gcra = new GcraCommands(this);
            Observe = new ObserveCommands(this);
        }

        /// <summary>Parses <paramref name="url"/> (see <see cref="HotKVClientOptions.Parse"/>) and connects.</summary>
        public static Task<HotKVClient> ConnectAsync(string url, CancellationToken cancellationToken = default) =>
            ConnectAsync(HotKVClientOptions.Parse(url), cancellationToken);

        /// <summary>Connects using explicit <paramref name="options"/>, validating reachability before returning.</summary>
        public static async Task<HotKVClient> ConnectAsync(HotKVClientOptions options, CancellationToken cancellationToken = default)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            var client = new HotKVClient(options);
            var probe = await client._pool.RentAsync(cancellationToken).ConfigureAwait(false);
            client._pool.Return(probe, healthy: true);
            return client;
        }

        /// <summary>Sends one command and returns its reply. Retries once, on a fresh connection, for a read known to be idempotent when the connection was lost.</summary>
        public async ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (args is null || args.Count == 0)
            {
                throw new ArgumentException("a command needs at least its name", nameof(args));
            }

            Connection.RespWriter.Validate(args);

            string? commandName = args[0] as string;
            bool retryable = Options.RetryOnConnectionLoss && CommandClassification.IsIdempotentRead(commandName);
            int maxAttempts = retryable ? 2 : 1;

            using var cts = CreateCommandCts(cancellationToken);
            try
            {
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    var connection = await _pool.RentAsync(cts.Token).ConfigureAwait(false);
                    bool healthy = false;
                    try
                    {
                        var result = await connection.ExecuteAsync(args, cts.Token).ConfigureAwait(false);
                        healthy = true;
                        return result;
                    }
                    catch (HotKVServerException)
                    {
                        healthy = true; // the server replied cleanly; the connection/stream is fine.
                        throw;
                    }
                    catch (HotKVConnectionException) when (attempt < maxAttempts)
                    {
                        // swallow: fall through and retry on a newly rented connection.
                    }
                    finally
                    {
                        _pool.Return(connection, healthy);
                    }
                }
                throw new HotKVConnectionException($"command '{commandName}' did not complete");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HotKVTimeoutException($"command '{commandName}' timed out after {Options.CommandTimeout}");
            }
        }

        private CancellationTokenSource CreateCommandCts(CancellationToken userToken)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(userToken);
            if (Options.CommandTimeout > TimeSpan.Zero)
            {
                cts.CancelAfter(Options.CommandTimeout);
            }
            return cts;
        }

        /// <summary>Generic escape hatch: sends any command by name and positional arguments.</summary>
        public ValueTask<RespValue> CommandAsync(string command, params object?[] args) =>
            CommandAsync(CancellationToken.None, command, args);

        /// <summary>Generic escape hatch with an explicit <see cref="CancellationToken"/>.</summary>
        public ValueTask<RespValue> CommandAsync(CancellationToken cancellationToken, string command, params object?[] args)
        {
            var tokens = new List<object?>(args.Length + 1) { command };
            tokens.AddRange(args);
            return ExecuteAsync(tokens, cancellationToken);
        }

        /// <summary>Starts a pipeline: commands queued on the returned <see cref="HotKV.Pipelines.IBatch"/> are sent together in one round trip when it is executed.</summary>
        public IBatch CreateBatch() => new Batch(_pool);

        /// <summary>
        /// Rents a dedicated connection and starts a transaction on it
        /// (optionally after WATCHing keys). Dispose the transaction to
        /// return its connection to the pool; call
        /// <see cref="ITransaction.ExecuteAsync"/> to run MULTI, every
        /// queued command, and EXEC.
        /// </summary>
        public async Task<ITransaction> CreateTransactionAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var connection = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
            return new Transaction(connection, _pool);
        }

        /// <summary>
        /// Opens a dedicated connection for pub/sub. The subscriber owns
        /// that connection for its lifetime (it is not pooled); dispose it
        /// to unsubscribe from everything and release the connection.
        /// </summary>
        public async Task<HotKVSubscriber> CreateSubscriberAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            var connection = await Connection.HotKVConnection.ConnectAsync(Options, cancellationToken).ConfigureAwait(false);
            return new HotKVSubscriber(connection);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotKVClient));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            await _pool.DisposeAsync().ConfigureAwait(false);
        }
    }
}
