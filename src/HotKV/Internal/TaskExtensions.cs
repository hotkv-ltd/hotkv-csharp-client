// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV.Internal
{
    /// <summary>
    /// Cancellation helpers for the handful of BCL async APIs (socket
    /// connect chief among them) that do not accept a <see cref="CancellationToken"/>
    /// directly on every target framework this package supports.
    /// </summary>
    internal static class TaskExtensions
    {
        public static async Task WithCancellation(this Task task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return;
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs))
            {
                var winner = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
                if (winner == tcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            await task.ConfigureAwait(false);
        }

        public static async Task<T> WithCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            if (!cancellationToken.CanBeCanceled || task.IsCompleted)
            {
                return await task.ConfigureAwait(false);
            }

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state!).TrySetResult(true), tcs))
            {
                var winner = await Task.WhenAny(task, tcs.Task).ConfigureAwait(false);
                if (winner == tcs.Task)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return await task.ConfigureAwait(false);
        }
    }
}
