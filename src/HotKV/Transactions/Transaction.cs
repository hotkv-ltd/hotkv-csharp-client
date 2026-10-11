// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Transactions
{
    /// <summary>
    /// A MULTI/EXEC transaction bound to one dedicated connection. Queue
    /// commands with the usual typed extension methods (each returns a
    /// <see cref="Task"/> that resolves once the transaction executes);
    /// call <see cref="ExecuteAsync"/> to send WATCH's chosen keys were
    /// already checked, then run MULTI, every queued command and EXEC as a
    /// single round trip. Dispose the transaction (an <c>await using</c>
    /// block is the natural shape) to release its connection back to the
    /// pool; disposing before <see cref="ExecuteAsync"/> implicitly
    /// discards it.
    /// </summary>
    public interface ITransaction : ICommandExecutor, IAsyncDisposable
    {
        /// <summary>WATCH key [key ...]. Must be called before any command is queued.</summary>
        Task WatchAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

        /// <summary>UNWATCH.</summary>
        Task UnwatchAsync(CancellationToken cancellationToken = default);

        /// <summary>Runs MULTI, every queued command, and EXEC. Returns false if EXEC aborted the transaction (a watched key changed); each queued command's task then faults instead of completing.</summary>
        Task<bool> ExecuteAsync(CancellationToken cancellationToken = default);

        /// <summary>Abandons the transaction without sending MULTI/EXEC, clearing any WATCH and releasing the connection back to the pool.</summary>
        Task DiscardAsync(CancellationToken cancellationToken = default);
    }

    internal sealed class Transaction : ITransaction
    {
        private readonly HotKVConnection _connection;
        private readonly ConnectionPool _pool;
        private readonly List<(List<object?> Args, TaskCompletionSource<RespValue> Tcs)> _queued = new List<(List<object?>, TaskCompletionSource<RespValue>)>();
        private bool _watched;
        private bool _finished;
        private bool _returned;
        private bool _healthy = true;

        public Transaction(HotKVConnection connection, ConnectionPool pool)
        {
            _connection = connection;
            _pool = pool;
        }

        public async Task WatchAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            ThrowIfFinished();
            var args = new List<object?>(keys.Count + 1) { "WATCH" };
            args.AddRange(keys);
            try
            {
                await _connection.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                _watched = true;
            }
            catch
            {
                _healthy = false;
                throw;
            }
        }

        public async Task UnwatchAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFinished();
            try
            {
                await _connection.ExecuteAsync(new object?[] { "UNWATCH" }, cancellationToken).ConfigureAwait(false);
                _watched = false;
            }
            catch
            {
                _healthy = false;
                throw;
            }
        }

        public ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            ThrowIfFinished();
            RespWriter.Validate(args);
            var tcs = new TaskCompletionSource<RespValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queued.Add((new List<object?>(args), tcs));
            return new ValueTask<RespValue>(tcs.Task);
        }

        public async Task<bool> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFinished();
            _finished = true;
            try
            {
                await _connection.ExecuteAsync(new object?[] { "MULTI" }, cancellationToken).ConfigureAwait(false);
                foreach (var (args, _) in _queued)
                {
                    // Inside MULTI every well formed command replies +QUEUED;
                    // HotKVConnection.ExecuteAsync only throws for an actual
                    // error reply (e.g. bad arity), which is the one case a
                    // real server also marks the transaction dirty for.
                    await _connection.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                }

                var execReply = await _connection.ExecuteAsync(new object?[] { "EXEC" }, cancellationToken).ConfigureAwait(false);
                if (execReply.IsNull)
                {
                    var abortEx = new HotKVException("transaction aborted: EXEC returned null (a watched key was modified)");
                    foreach (var (_, tcs) in _queued)
                    {
                        tcs.TrySetException(abortEx);
                    }
                    return false;
                }

                var results = execReply.AsArray();
                for (int i = 0; i < _queued.Count; i++)
                {
                    var reply = results[i];
                    var tcs = _queued[i].Tcs;
                    if (reply.IsError)
                    {
                        var (prefix, message) = RespValue.SplitError(reply.ErrorText);
                        tcs.SetException(HotKVServerException.Create(prefix, message));
                    }
                    else
                    {
                        tcs.SetResult(reply);
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _healthy = false;
                foreach (var (_, tcs) in _queued)
                {
                    tcs.TrySetException(ex);
                }
                throw;
            }
            finally
            {
                ReturnConnection();
            }
        }

        public async Task DiscardAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfFinished();
            _finished = true;
            try
            {
                if (_watched)
                {
                    await _connection.ExecuteAsync(new object?[] { "UNWATCH" }, cancellationToken).ConfigureAwait(false);
                }
            }
            catch
            {
                _healthy = false;
            }
            var discardEx = new InvalidOperationException("the transaction was discarded");
            foreach (var (_, tcs) in _queued)
            {
                tcs.TrySetException(discardEx);
            }
            ReturnConnection();
        }

        public async ValueTask DisposeAsync()
        {
            if (!_finished)
            {
                _finished = true;
                try
                {
                    if (_watched)
                    {
                        await _connection.ExecuteAsync(new object?[] { "UNWATCH" }, CancellationToken.None).ConfigureAwait(false);
                    }
                }
                catch
                {
                    _healthy = false;
                }
                var disposeEx = new ObjectDisposedException(nameof(Transaction), "the transaction was disposed without calling ExecuteAsync or DiscardAsync");
                foreach (var (_, tcs) in _queued)
                {
                    tcs.TrySetException(disposeEx);
                }
            }
            ReturnConnection();
        }

        private void ThrowIfFinished()
        {
            if (_finished)
            {
                throw new InvalidOperationException("this transaction has already been executed or discarded");
            }
        }

        private void ReturnConnection()
        {
            if (_returned)
            {
                return;
            }
            _returned = true;
            _pool.Return(_connection, _healthy);
        }
    }
}
