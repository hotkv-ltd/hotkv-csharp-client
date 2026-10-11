// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Cluster
{
    /// <summary>One CLUSTER SLOTS range: the slots it covers and the endpoints that serve it.</summary>
    internal sealed record SlotRange(int Start, int End, string PrimaryEndpoint, IReadOnlyList<string> ReplicaEndpoints);

    /// <summary>
    /// A cluster aware client: builds a slot map from CLUSTER SLOTS, routes
    /// every command by its key's CRC16 hash slot (honoring "{hash tag}"
    /// pinning), follows MOVED redirects by refreshing the map and ASK
    /// redirects by sending ASKING to the target node first, and bounds how
    /// many redirects a single command follows before giving up. Implements
    /// <see cref="ICommandExecutor"/>, so every typed command extension
    /// method works on it directly. The routing key defaults to the
    /// command's first argument (correct for the large majority of single
    /// key commands); for a multi-key command whose key is not in that
    /// position, use <see cref="ExecuteWithRoutingKeyAsync"/> directly.
    /// </summary>
    public sealed class HotKVCluster : ICommandExecutor, IAsyncDisposable
    {
        private readonly HotKVClientOptions _seedOptions;
        private readonly int _maxRedirects;
        private readonly ConcurrentDictionary<string, Lazy<Task<HotKVClient>>> _nodes = new ConcurrentDictionary<string, Lazy<Task<HotKVClient>>>();
        private readonly HashSet<string> _replicaEndpoints = new HashSet<string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private volatile SlotRange[] _slots = Array.Empty<SlotRange>();
        private int _roundRobin;
        private bool _disposed;

        /// <summary>When true, routes read-only commands (per <see cref="Commands.CommandClassification.IsIdempotentRead"/>-like reasoning is left to the caller) to a replica with READONLY, round-robining across replicas. Writes always go to the primary regardless of this setting.</summary>
        public bool ReadFromReplicas { get; set; }

        private HotKVCluster(HotKVClientOptions seedOptions, int maxRedirects)
        {
            _seedOptions = seedOptions;
            _maxRedirects = maxRedirects;
        }

        /// <summary>Connects to the cluster by discovering its topology from the first reachable seed URL.</summary>
        public static Task<HotKVCluster> ConnectAsync(string seedUrl, int maxRedirects = 5, CancellationToken cancellationToken = default) =>
            ConnectAsync(new[] { seedUrl }, maxRedirects, cancellationToken);

        /// <summary>Connects to the cluster, trying each seed URL in order until one answers CLUSTER SLOTS.</summary>
        public static async Task<HotKVCluster> ConnectAsync(IReadOnlyList<string> seedUrls, int maxRedirects = 5, CancellationToken cancellationToken = default)
        {
            if (seedUrls is null || seedUrls.Count == 0)
            {
                throw new ArgumentException("at least one seed URL is required", nameof(seedUrls));
            }

            Exception? lastError = null;
            foreach (var url in seedUrls)
            {
                var options = HotKVClientOptions.Parse(url);
                var cluster = new HotKVCluster(options, maxRedirects);
                try
                {
                    await cluster.RefreshSlotsAsync(cancellationToken).ConfigureAwait(false);
                    return cluster;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    await cluster.DisposeAsync().ConfigureAwait(false);
                }
            }
            throw new HotKVConnectionException("could not discover cluster topology from any seed URL", lastError);
        }

        /// <summary>Re-reads CLUSTER SLOTS from a reachable node and rebuilds the slot map. Called automatically on MOVED; call it yourself after cluster resharding if you are not issuing commands in the meantime.</summary>
        public async Task RefreshSlotsAsync(CancellationToken cancellationToken = default)
        {
            await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var reply = await FetchClusterSlotsAsync(cancellationToken).ConfigureAwait(false);
                var ranges = new List<SlotRange>();
                foreach (var entry in reply.AsArray())
                {
                    var parts = entry.AsArray();
                    int start = (int)parts[0].AsInt64();
                    int end = (int)parts[1].AsInt64();
                    string primary = ParseNodeEndpoint(parts[2]);
                    var replicas = new List<string>();
                    for (int i = 3; i < parts.Count; i++)
                    {
                        replicas.Add(ParseNodeEndpoint(parts[i]));
                    }
                    ranges.Add(new SlotRange(start, end, primary, replicas));
                    foreach (var r in replicas)
                    {
                        _replicaEndpoints.Add(r);
                    }
                }
                ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
                _slots = ranges.ToArray();
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<RespValue> FetchClusterSlotsAsync(CancellationToken cancellationToken)
        {
            var candidates = new List<HotKVClientOptions> { _seedOptions };
            foreach (var range in _slots)
            {
                var opt = _seedOptions.Clone();
                var (host, port) = SplitEndpoint(range.PrimaryEndpoint);
                opt.Host = host;
                opt.Port = port;
                candidates.Add(opt);
            }

            Exception? lastError = null;
            foreach (var options in candidates)
            {
                HotKVConnection? probe = null;
                try
                {
                    probe = await HotKVConnection.ConnectAsync(options, cancellationToken).ConfigureAwait(false);
                    return await probe.ExecuteAsync(new object?[] { "CLUSTER", "SLOTS" }, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                finally
                {
                    probe?.Dispose();
                }
            }
            throw new HotKVConnectionException("CLUSTER SLOTS failed against every known node", lastError);
        }

        private static string ParseNodeEndpoint(RespValue nodeInfo)
        {
            var parts = nodeInfo.AsArray();
            string host = parts[0].AsString() ?? string.Empty;
            long port = parts[1].AsInt64();
            return host + ":" + port.ToString(CultureInfo.InvariantCulture);
        }

        private static (string Host, int Port) SplitEndpoint(string endpoint)
        {
            int colon = endpoint.LastIndexOf(':');
            return (endpoint.Substring(0, colon), int.Parse(endpoint.Substring(colon + 1), CultureInfo.InvariantCulture));
        }

        private async Task<HotKVClient> GetNodeClientAsync(string endpoint, CancellationToken cancellationToken)
        {
            var lazy = _nodes.GetOrAdd(endpoint, ep => new Lazy<Task<HotKVClient>>(() => CreateNodeClientAsync(ep, cancellationToken)));
            try
            {
                return await lazy.Value.ConfigureAwait(false);
            }
            catch
            {
                _nodes.TryRemove(endpoint, out _);
                throw;
            }
        }

        private Task<HotKVClient> CreateNodeClientAsync(string endpoint, CancellationToken cancellationToken)
        {
            var (host, port) = SplitEndpoint(endpoint);
            var options = _seedOptions.Clone();
            options.Host = host;
            options.Port = port;
            options.UnixSocketPath = null;
            if (_replicaEndpoints.Contains(endpoint))
            {
                options.PostConnectCommands = new List<IReadOnlyList<object?>> { new object?[] { "READONLY" } };
            }
            return HotKVClient.ConnectAsync(options, cancellationToken);
        }

        private string ResolveEndpoint(int slot, bool preferReplica)
        {
            var slots = _slots;
            foreach (var range in slots)
            {
                if (slot < range.Start || slot > range.End)
                {
                    continue;
                }
                if (preferReplica && range.ReplicaEndpoints.Count > 0)
                {
                    int idx = (Interlocked.Increment(ref _roundRobin) & int.MaxValue) % range.ReplicaEndpoints.Count;
                    return range.ReplicaEndpoints[idx];
                }
                return range.PrimaryEndpoint;
            }
            throw new HotKVException($"no cluster node owns slot {slot}; the slot map may be stale (call RefreshSlotsAsync)");
        }

        /// <summary>Routes using the command's first argument (args[1], i.e. right after the command name) as the key. Correct for the large majority of commands; see <see cref="ExecuteWithRoutingKeyAsync"/> for the rest.</summary>
        public ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            string? routingKey = args.Count > 1 ? args[1] as string : null;
            return ExecuteWithRoutingKeyAsync(args, routingKey, cancellationToken);
        }

        /// <summary>Routes a command using an explicit key (or null to hit slot 0's owner, e.g. for a cluster-wide administrative command).</summary>
        public async ValueTask<RespValue> ExecuteWithRoutingKeyAsync(IReadOnlyList<object?> args, string? routingKey, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            int slot = routingKey is not null ? ClusterKeySlot.Compute(routingKey) : 0;
            bool preferReplica = ReadFromReplicas;
            string endpoint = ResolveEndpoint(slot, preferReplica);
            bool asking = false;
            int redirects = 0;

            while (true)
            {
                var client = await GetNodeClientAsync(endpoint, cancellationToken).ConfigureAwait(false);
                try
                {
                    if (asking)
                    {
                        await client.ExecuteAsync(new object?[] { "ASKING" }, cancellationToken).ConfigureAwait(false);
                    }
                    return await client.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                }
                catch (HotKVClusterRedirectException redirect)
                {
                    if (++redirects > _maxRedirects)
                    {
                        throw;
                    }
                    if (redirect.IsAsk)
                    {
                        endpoint = redirect.Endpoint;
                        asking = true;
                    }
                    else
                    {
                        await RefreshSlotsAsync(cancellationToken).ConfigureAwait(false);
                        endpoint = redirect.Endpoint;
                        asking = false;
                    }
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotKVCluster));
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            foreach (var lazy in _nodes.Values)
            {
                if (lazy.IsValueCreated)
                {
                    try
                    {
                        var client = await lazy.Value.ConfigureAwait(false);
                        await client.DisposeAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // best effort cleanup; a node that never connected has nothing to dispose.
                    }
                }
            }
            _refreshLock.Dispose();
        }
    }
}
