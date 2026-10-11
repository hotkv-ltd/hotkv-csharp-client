// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Typed wrappers for the HyperLogLog command family.</summary>
    public static class HyperLogLogCommands
    {
        /// <summary>PFADD key element [element ...] -> true if the estimated cardinality register changed.</summary>
        public static async Task<bool> HyperLogLogAddAsync(this ICommandExecutor executor, string key, IReadOnlyList<string> elements, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(elements.Count + 2) { "PFADD", key };
            args.AddRange(elements);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PFCOUNT key [key ...] -> the approximate cardinality of the union of every given key.</summary>
        public static async Task<long> HyperLogLogCountAsync(this ICommandExecutor executor, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 1) { "PFCOUNT" };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>PFMERGE destkey sourcekey [sourcekey ...].</summary>
        public static Task HyperLogLogMergeAsync(this ICommandExecutor executor, string destination, IReadOnlyList<string> sourceKeys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(sourceKeys.Count + 2) { "PFMERGE", destination };
            args.AddRange(sourceKeys);
            return executor.ExecuteAsync(args, cancellationToken).AsTask();
        }
    }
}
