// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Pipelines
{
    /// <summary>
    /// A pipeline: every typed command extension method works on an
    /// <see cref="IBatch"/> exactly as it does on <see cref="HotKVClient"/>,
    /// but instead of running immediately, the command is queued and its
    /// <see cref="Task"/> only completes once <see cref="ExecuteAsync"/>
    /// sends everything queued so far in a single round trip. A failing
    /// individual command surfaces as an exception on that command's own
    /// task, without failing the commands queued around it.
    /// </summary>
    public interface IBatch : ICommandExecutor
    {
        /// <summary>Sends every command queued so far in one round trip and completes each one's task with its reply (or exception).</summary>
        Task ExecuteAsync(CancellationToken cancellationToken = default);

        /// <summary>Number of commands queued so far.</summary>
        int QueuedCount { get; }
    }

    internal sealed class Batch : IBatch
    {
        private readonly ConnectionPool _pool;
        private readonly List<(List<object?> Args, TaskCompletionSource<RespValue> Tcs)> _queued = new List<(List<object?>, TaskCompletionSource<RespValue>)>();
        private bool _executed;

        public Batch(ConnectionPool pool)
        {
            _pool = pool;
        }

        public int QueuedCount => _queued.Count;

        public ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            if (_executed)
            {
                throw new InvalidOperationException("this batch has already been executed; create a new one to queue more commands");
            }
            RespWriter.Validate(args);
            var tcs = new TaskCompletionSource<RespValue>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queued.Add((new List<object?>(args), tcs));
            return new ValueTask<RespValue>(tcs.Task);
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            if (_executed)
            {
                throw new InvalidOperationException("this batch has already been executed");
            }
            _executed = true;
            if (_queued.Count == 0)
            {
                return;
            }

            HotKVConnection? connection = null;
            bool healthy = false;
            try
            {
                connection = await _pool.RentAsync(cancellationToken).ConfigureAwait(false);
                foreach (var (args, _) in _queued)
                {
                    connection.QueueCommand(args);
                }
                await connection.FlushAsync(cancellationToken).ConfigureAwait(false);

                foreach (var (_, tcs) in _queued)
                {
                    var reply = await connection.ReadReplyAsync(cancellationToken).ConfigureAwait(false);
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
                healthy = true;
            }
            catch (Exception ex)
            {
                // A connection/protocol level failure leaves later commands
                // unread; fail every task that has not resolved yet so no
                // caller awaits forever.
                foreach (var (_, tcs) in _queued)
                {
                    tcs.TrySetException(ex);
                }
                throw;
            }
            finally
            {
                if (connection is not null)
                {
                    _pool.Return(connection, healthy);
                }
            }
        }
    }
}
