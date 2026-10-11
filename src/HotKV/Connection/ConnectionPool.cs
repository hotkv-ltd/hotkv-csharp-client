// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV.Connection
{
    /// <summary>
    /// A bounded pool of <see cref="HotKVConnection"/>s. Rentals block (up
    /// to the options' connect timeout) once <see cref="HotKVClientOptions.MaxPoolSize"/>
    /// connections are outstanding; connections idle longer than
    /// <see cref="HotKVClientOptions.IdleTimeout"/> are health checked with
    /// PING before reuse and transparently replaced if the check fails.
    /// Safe for concurrent use from multiple threads/tasks.
    /// </summary>
    public sealed class ConnectionPool : IAsyncDisposable
    {
        private readonly HotKVClientOptions _options;
        private readonly SemaphoreSlim _capacity;
        private readonly ConcurrentStack<HotKVConnection> _idle = new ConcurrentStack<HotKVConnection>();
        private volatile bool _disposed;

        /// <summary>Total number of physical connections ever created by this pool. Diagnostic only.</summary>
        public int TotalCreated;

        public ConnectionPool(HotKVClientOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (options.MaxPoolSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "MaxPoolSize must be at least 1");
            }
            _capacity = new SemaphoreSlim(options.MaxPoolSize, options.MaxPoolSize);
        }

        /// <summary>
        /// Rents a connection, reusing a healthy idle one when available or
        /// opening a new one within the pool's bounds. The caller must
        /// return it with <see cref="Return"/> exactly once, even on error
        /// paths.
        /// </summary>
        public async Task<HotKVConnection> RentAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            bool acquired = await _capacity.WaitAsync((int)_options.ConnectTimeout.TotalMilliseconds, cancellationToken).ConfigureAwait(false);
            if (!acquired)
            {
                throw new HotKVTimeoutException($"timed out after {_options.ConnectTimeout} waiting for a free connection (pool size {_options.MaxPoolSize})");
            }

            try
            {
                while (_idle.TryPop(out var candidate))
                {
                    bool needsCheck = DateTime.UtcNow - candidate.LastUsedUtc > _options.IdleTimeout;
                    if (!needsCheck || await candidate.TryPingAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return candidate;
                    }
                    candidate.Dispose();
                }

                var created = await HotKVConnection.ConnectAsync(_options, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref TotalCreated);
                return created;
            }
            catch
            {
                _capacity.Release();
                throw;
            }
        }

        /// <summary>
        /// Returns a connection to the pool. Pass <paramref name="healthy"/>
        /// as false (for example after a protocol error or a caller-observed
        /// connection failure) to have the pool discard it instead of
        /// reusing it; a fresh connection is opened on a later rental.
        /// </summary>
        public void Return(HotKVConnection connection, bool healthy = true)
        {
            if (connection is null)
            {
                throw new ArgumentNullException(nameof(connection));
            }

            if (_disposed || !healthy)
            {
                connection.Dispose();
                _capacity.Release();
                return;
            }

            _idle.Push(connection);
            _capacity.Release();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(ConnectionPool));
            }
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return default;
            }
            _disposed = true;
            while (_idle.TryPop(out var connection))
            {
                connection.Dispose();
            }
            _capacity.Dispose();
            return default;
        }
    }
}
