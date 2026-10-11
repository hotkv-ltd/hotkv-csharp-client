// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Bitwise operation for <see cref="BitmapCommands.BitOpAsync"/>.</summary>
    public enum BitOp
    {
        And,
        Or,
        Xor,
        Not,
    }

    /// <summary>Typed wrappers for the bitmap command family.</summary>
    public static class BitmapCommands
    {
        /// <summary>SETBIT key offset value -> the bit's previous value.</summary>
        public static async Task<bool> SetBitAsync(this ICommandExecutor executor, string key, long offset, bool value, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SETBIT", key, offset, value ? 1 : 0 }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>GETBIT key offset.</summary>
        public static async Task<bool> GetBitAsync(this ICommandExecutor executor, string key, long offset, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "GETBIT", key, offset }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>BITCOUNT key [start end [BYTE|BIT]].</summary>
        public static async Task<long> BitCountAsync(this ICommandExecutor executor, string key, long? start = null, long? end = null, bool bitRange = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "BITCOUNT", key };
            if (start.HasValue && end.HasValue)
            {
                args.Add(start.Value);
                args.Add(end.Value);
                if (bitRange)
                {
                    args.Add("BIT");
                }
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>BITPOS key bit [start [end [BYTE|BIT]]] -> index of the first matching bit, or -1.</summary>
        public static async Task<long> BitPositionAsync(this ICommandExecutor executor, string key, bool bit, long? start = null, long? end = null, bool bitRange = false, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "BITPOS", key, bit ? 1 : 0 };
            if (start.HasValue)
            {
                args.Add(start.Value);
                if (end.HasValue)
                {
                    args.Add(end.Value);
                    if (bitRange)
                    {
                        args.Add("BIT");
                    }
                }
            }
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>BITOP AND|OR|XOR|NOT destkey key [key ...] -> length of the stored result.</summary>
        public static async Task<long> BitOpAsync(this ICommandExecutor executor, BitOp operation, string destination, IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(keys.Count + 3) { "BITOP", operation.ToString().ToUpperInvariant(), destination };
            args.AddRange(keys);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>
        /// BITFIELD key operation [operation ...], where each operation is
        /// its raw tokens (e.g. "GET","u8","0" or "INCRBY","i5","100","1").
        /// One reply element per operation; GET/SET/INCRBY reply with an
        /// integer, OVERFLOW itself replies nothing (no element).
        /// </summary>
        public static async Task<IReadOnlyList<long?>> BitFieldAsync(this ICommandExecutor executor, string key, IReadOnlyList<object> operationTokens, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(operationTokens.Count + 2) { "BITFIELD", key };
            args.AddRange(operationTokens);
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsArray().Select(v => v.AsNullableInt64()).ToList();
        }
    }
}
