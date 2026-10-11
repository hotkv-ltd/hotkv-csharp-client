// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>One AMEM.CONV.* turn.</summary>
    public sealed record ConversationTurn(string Role, string Content);

    /// <summary>One AMEM.SEM.RECALL result.</summary>
    public sealed record SemanticMemoryResult(string Key, string Content, double Similarity);

    /// <summary>One AMEM.EPIS.* event.</summary>
    public sealed record EpisodicEvent(string Type, string Content, string Metadata, long TimestampNanos);

    /// <summary>One AMEM.SEARCH cross-memory result.</summary>
    public sealed record AgentMemorySearchResult(string Source, string Key, string Content, double Similarity);

    /// <summary>AMEM.SUMMARY.GET reply.</summary>
    public sealed record AgentMemorySummarySnapshot(string Summary, long Version, long UpdatedUnix);

    /// <summary>
    /// AMEM.* AI agent memory: conversation
    /// history, a per-agent semantic vector store, per-session working
    /// memory, an episodic event log, cross-memory search, and rolling
    /// summaries. On server 0.3.0 every AMEM.* command needs
    /// the AgentMemory license feature, and Summary (AMEM.SUMMARY.*) also needs
    /// the AMemSummary license feature. Reachable as
    /// <c>client.AgentMemory</c>.
    /// </summary>
    public sealed class AgentMemoryCommands
    {
        private readonly ICommandExecutor _executor;

        /// <summary>AMEM.CONV.* conversation history.</summary>
        public AgentMemoryConversation Conversation { get; }

        /// <summary>AMEM.SEM.* per-agent semantic vector store. Needs the AgentMemory license feature.</summary>
        public AgentMemorySemantic Semantic { get; }

        /// <summary>AMEM.WORK.* per-session working memory.</summary>
        public AgentMemoryWorking Working { get; }

        /// <summary>AMEM.EPIS.* episodic event log.</summary>
        public AgentMemoryEpisodic Episodic { get; }

        /// <summary>AMEM.SUMMARY.* rolling summaries. Needs the AMemSummary license feature.</summary>
        public AgentMemorySummary Summary { get; }

        internal AgentMemoryCommands(ICommandExecutor executor)
        {
            _executor = executor;
            Conversation = new AgentMemoryConversation(executor);
            Semantic = new AgentMemorySemantic(executor);
            Working = new AgentMemoryWorking(executor);
            Episodic = new AgentMemoryEpisodic(executor);
            Summary = new AgentMemorySummary(executor);
        }

        /// <summary>AMEM.SEARCH agent_id k val1 .. valN: searches semantic, episodic and working memory together.</summary>
        public Task<IReadOnlyList<AgentMemorySearchResult>> SearchAsync(string agentId, int k, IReadOnlyList<double> vector, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(agentId, k, vector.Cast<object?>(), cancellationToken);

        /// <summary>AMEM.SEARCH with a float32 vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<IReadOnlyList<AgentMemorySearchResult>> SearchAsync(string agentId, int k, IReadOnlyList<float> vector, CancellationToken cancellationToken = default) =>
            SearchCoreAsync(agentId, k, vector.Cast<object?>(), cancellationToken);

        private async Task<IReadOnlyList<AgentMemorySearchResult>> SearchCoreAsync(string agentId, int k, IEnumerable<object?> vector, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "AMEM.SEARCH", agentId, k };
            args.AddRange(vector);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(item =>
            {
                var d = item.AsDictionary();
                return new AgentMemorySearchResult(d.GetString("source"), d.GetString("key"), d.GetString("content"), ParseSimilarity(d));
            }).ToList();
        }

        internal static double ParseSimilarity(IReadOnlyDictionary<string, RespValue> dict) =>
            double.Parse(dict.GetString("similarity", "0"), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>AMEM.CONV.* conversation history commands.</summary>
    public sealed class AgentMemoryConversation
    {
        private readonly ICommandExecutor _executor;
        internal AgentMemoryConversation(ICommandExecutor executor) => _executor = executor;

        /// <summary>AMEM.CONV.CREATE session_id [WINDOW size] [TTL seconds].</summary>
        public Task CreateAsync(string sessionId, int? window = null, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "AMEM.CONV.CREATE", sessionId };
            if (window.HasValue)
            {
                args.Add("WINDOW");
                args.Add(window.Value);
            }
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>AMEM.CONV.APPEND session_id role content.</summary>
        public Task AppendAsync(string sessionId, string role, string content, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "AMEM.CONV.APPEND", sessionId, role, content }, cancellationToken).AsTask();

        /// <summary>AMEM.CONV.GET session_id -> every stored turn.</summary>
        public async Task<IReadOnlyList<ConversationTurn>> GetAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.CONV.GET", sessionId }, cancellationToken).ConfigureAwait(false);
            return ParseTurns(reply);
        }

        /// <summary>AMEM.CONV.WINDOW session_id n -> the last n turns.</summary>
        public async Task<IReadOnlyList<ConversationTurn>> WindowAsync(string sessionId, int n, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.CONV.WINDOW", sessionId, n }, cancellationToken).ConfigureAwait(false);
            return ParseTurns(reply);
        }

        /// <summary>AMEM.CONV.CLEAR session_id.</summary>
        public async Task<bool> ClearAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.CONV.CLEAR", sessionId }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.CONV.DROP session_id.</summary>
        public async Task<bool> DropAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.CONV.DROP", sessionId }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        private static IReadOnlyList<ConversationTurn> ParseTurns(RespValue reply) =>
            reply.AsArray().Select(t =>
            {
                var d = t.AsDictionary();
                return new ConversationTurn(d.GetString("role"), d.GetString("content"));
            }).ToList();
    }

    /// <summary>AMEM.SEM.* per-agent semantic vector store commands.</summary>
    public sealed class AgentMemorySemantic
    {
        private readonly ICommandExecutor _executor;
        internal AgentMemorySemantic(ICommandExecutor executor) => _executor = executor;

        /// <summary>AMEM.SEM.CREATE agent_id dimensions. dimensions must be in 1..65536.</summary>
        public Task CreateAsync(string agentId, int dimensions, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "AMEM.SEM.CREATE", agentId, dimensions }, cancellationToken).AsTask();

        /// <summary>AMEM.SEM.STORE agent_id key content val1 .. valN [TTL seconds].</summary>
        public Task StoreAsync(string agentId, string key, string content, IReadOnlyList<double> vector, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
            StoreCoreAsync(agentId, key, content, vector.Cast<object?>(), ttl, cancellationToken);

        /// <summary>AMEM.SEM.STORE with a float32 vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task StoreAsync(string agentId, string key, string content, IReadOnlyList<float> vector, TimeSpan? ttl = null, CancellationToken cancellationToken = default) =>
            StoreCoreAsync(agentId, key, content, vector.Cast<object?>(), ttl, cancellationToken);

        private Task StoreCoreAsync(string agentId, string key, string content, IEnumerable<object?> vector, TimeSpan? ttl, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "AMEM.SEM.STORE", agentId, key, content };
            args.AddRange(vector);
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>AMEM.SEM.RECALL agent_id k val1 .. valN -> up to k nearest neighbors, most similar first.</summary>
        public Task<IReadOnlyList<SemanticMemoryResult>> RecallAsync(string agentId, int k, IReadOnlyList<double> vector, CancellationToken cancellationToken = default) =>
            RecallCoreAsync(agentId, k, vector.Cast<object?>(), cancellationToken);

        /// <summary>AMEM.SEM.RECALL with a float32 vector: each value is sent as its shortest float32 round-trip decimal.</summary>
        public Task<IReadOnlyList<SemanticMemoryResult>> RecallAsync(string agentId, int k, IReadOnlyList<float> vector, CancellationToken cancellationToken = default) =>
            RecallCoreAsync(agentId, k, vector.Cast<object?>(), cancellationToken);

        private async Task<IReadOnlyList<SemanticMemoryResult>> RecallCoreAsync(string agentId, int k, IEnumerable<object?> vector, CancellationToken cancellationToken)
        {
            var args = new List<object?> { "AMEM.SEM.RECALL", agentId, k };
            args.AddRange(vector);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(item =>
            {
                var d = item.AsDictionary();
                return new SemanticMemoryResult(d.GetString("key"), d.GetString("content"), AgentMemoryCommands.ParseSimilarity(d));
            }).ToList();
        }

        /// <summary>AMEM.SEM.FORGET agent_id key.</summary>
        public async Task<bool> ForgetAsync(string agentId, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SEM.FORGET", agentId, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.SEM.LIST agent_id -> stored keys.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(string agentId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SEM.LIST", agentId }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }

    /// <summary>AMEM.WORK.* per-session working memory commands.</summary>
    public sealed class AgentMemoryWorking
    {
        private readonly ICommandExecutor _executor;
        internal AgentMemoryWorking(ICommandExecutor executor) => _executor = executor;

        /// <summary>AMEM.WORK.SET session_id key value [TTL seconds].</summary>
        public Task SetAsync(string sessionId, string key, string value, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "AMEM.WORK.SET", sessionId, key, value };
            if (ttl.HasValue)
            {
                args.Add("TTL");
                args.Add((long)ttl.Value.TotalSeconds);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>AMEM.WORK.GET session_id key. Note: an empty stored value is indistinguishable from a miss, both reply null.</summary>
        public async Task<string?> GetAsync(string sessionId, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.WORK.GET", sessionId, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsString();
        }

        /// <summary>AMEM.WORK.DEL session_id key.</summary>
        public async Task<bool> DeleteAsync(string sessionId, string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.WORK.DEL", sessionId, key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.WORK.CLEAR session_id.</summary>
        public async Task<bool> ClearAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.WORK.CLEAR", sessionId }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.WORK.LIST session_id -> stored keys.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.WORK.LIST", sessionId }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }

    /// <summary>AMEM.EPIS.* append-only episodic event log commands.</summary>
    public sealed class AgentMemoryEpisodic
    {
        private readonly ICommandExecutor _executor;
        internal AgentMemoryEpisodic(ICommandExecutor executor) => _executor = executor;

        /// <summary>AMEM.EPIS.LOG agent_id event_type content [METADATA json].</summary>
        public Task LogAsync(string agentId, string eventType, string content, string? metadata = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "AMEM.EPIS.LOG", agentId, eventType, content };
            if (metadata is not null)
            {
                args.Add("METADATA");
                args.Add(metadata);
            }
            return _executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>AMEM.EPIS.QUERY agent_id [TYPE event_type] [FROM ts] [TO ts] [LIMIT n].</summary>
        public async Task<IReadOnlyList<EpisodicEvent>> QueryAsync(string agentId, string? eventType = null, long? fromTimestamp = null, long? toTimestamp = null, long? limit = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "AMEM.EPIS.QUERY", agentId };
            if (eventType is not null)
            {
                args.Add("TYPE");
                args.Add(eventType);
            }
            if (fromTimestamp.HasValue)
            {
                args.Add("FROM");
                args.Add(fromTimestamp.Value);
            }
            if (toTimestamp.HasValue)
            {
                args.Add("TO");
                args.Add(toTimestamp.Value);
            }
            if (limit.HasValue)
            {
                args.Add("LIMIT");
                args.Add(limit.Value);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(item =>
            {
                var d = item.AsDictionary();
                return new EpisodicEvent(d.GetString("type"), d.GetString("content"), d.GetString("metadata"), d.GetInt64("timestamp"));
            }).ToList();
        }

        /// <summary>AMEM.EPIS.CLEAR agent_id.</summary>
        public async Task<bool> ClearAsync(string agentId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.EPIS.CLEAR", agentId }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.EPIS.STATS agent_id -> (total_events, oldest_ts, newest_ts).</summary>
        public async Task<(long TotalEvents, long OldestTimestamp, long NewestTimestamp)> StatsAsync(string agentId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.EPIS.STATS", agentId }, cancellationToken).ConfigureAwait(false);
            var d = reply.AsDictionary();
            return (d.GetInt64("total_events"), d.GetInt64("oldest_ts"), d.GetInt64("newest_ts"));
        }
    }

    /// <summary>AMEM.SUMMARY.* rolling summary commands.</summary>
    public sealed class AgentMemorySummary
    {
        private readonly ICommandExecutor _executor;
        internal AgentMemorySummary(ICommandExecutor executor) => _executor = executor;

        /// <summary>AMEM.SUMMARY.SET agent_id summary -> the new version number.</summary>
        public async Task<long> SetAsync(string agentId, string summary, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SUMMARY.SET", agentId, summary }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>AMEM.SUMMARY.APPEND agent_id extra -> the new version number.</summary>
        public async Task<long> AppendAsync(string agentId, string extra, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SUMMARY.APPEND", agentId, extra }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>AMEM.SUMMARY.GET agent_id.</summary>
        public async Task<AgentMemorySummarySnapshot?> GetAsync(string agentId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SUMMARY.GET", agentId }, cancellationToken).ConfigureAwait(false);
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return new AgentMemorySummarySnapshot(items[0].AsString() ?? string.Empty, items[1].AsInt64(), items[2].AsInt64());
        }

        /// <summary>AMEM.SUMMARY.DROP agent_id.</summary>
        public async Task<bool> DropAsync(string agentId, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SUMMARY.DROP", agentId }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>AMEM.SUMMARY.LIST -> agent ids.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "AMEM.SUMMARY.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
