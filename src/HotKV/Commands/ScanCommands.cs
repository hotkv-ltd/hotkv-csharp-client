// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Cursor based iterators for the SCAN command family: SCAN, HSCAN, SSCAN, ZSCAN.</summary>
    public static class ScanCommands
    {
        private static void AppendCommonOptions(List<object?> args, string? match, long? count, string? type)
        {
            if (match is not null)
            {
                args.Add("MATCH");
                args.Add(match);
            }
            if (count.HasValue)
            {
                args.Add("COUNT");
                args.Add(count.Value);
            }
            if (type is not null)
            {
                args.Add("TYPE");
                args.Add(type);
            }
        }

        /// <summary>Iterates the whole keyspace with SCAN, transparently issuing further SCAN calls as the enumeration advances until the server reports cursor "0".</summary>
        public static async IAsyncEnumerable<string> ScanAsync(this ICommandExecutor executor, string? match = null, long? count = null, string? type = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string cursor = "0";
            do
            {
                var args = new List<object?> { "SCAN", cursor };
                AppendCommonOptions(args, match, count, type);
                var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                var page = reply.AsArray();
                cursor = page[0].AsString() ?? "0";
                foreach (var item in page[1].AsArray())
                {
                    yield return item.AsString() ?? string.Empty;
                }
            } while (cursor != "0");
        }

        /// <summary>Iterates a hash's fields with HSCAN.</summary>
        public static async IAsyncEnumerable<(string Field, string Value)> HashScanAsync(this ICommandExecutor executor, string key, string? match = null, long? count = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string cursor = "0";
            do
            {
                var args = new List<object?> { "HSCAN", key, cursor };
                AppendCommonOptions(args, match, count, null);
                var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                var page = reply.AsArray();
                cursor = page[0].AsString() ?? "0";
                var items = page[1].AsArray();
                for (int i = 0; i + 1 < items.Count; i += 2)
                {
                    yield return (items[i].AsString() ?? string.Empty, items[i + 1].AsString() ?? string.Empty);
                }
            } while (cursor != "0");
        }

        /// <summary>Iterates a set's members with SSCAN.</summary>
        public static async IAsyncEnumerable<string> SetScanAsync(this ICommandExecutor executor, string key, string? match = null, long? count = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string cursor = "0";
            do
            {
                var args = new List<object?> { "SSCAN", key, cursor };
                AppendCommonOptions(args, match, count, null);
                var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                var page = reply.AsArray();
                cursor = page[0].AsString() ?? "0";
                foreach (var item in page[1].AsArray())
                {
                    yield return item.AsString() ?? string.Empty;
                }
            } while (cursor != "0");
        }

        /// <summary>Iterates a sorted set's members with ZSCAN.</summary>
        public static async IAsyncEnumerable<(string Member, double Score)> SortedSetScanAsync(this ICommandExecutor executor, string key, string? match = null, long? count = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            string cursor = "0";
            do
            {
                var args = new List<object?> { "ZSCAN", key, cursor };
                AppendCommonOptions(args, match, count, null);
                var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
                var page = reply.AsArray();
                cursor = page[0].AsString() ?? "0";
                var items = page[1].AsArray();
                for (int i = 0; i + 1 < items.Count; i += 2)
                {
                    yield return (items[i].AsString() ?? string.Empty, items[i + 1].AsDouble());
                }
            } while (cursor != "0");
        }
    }
}
