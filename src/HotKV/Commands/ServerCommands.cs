// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>Typed wrappers for server administration and introspection commands.</summary>
    public static class ServerCommands
    {
        /// <summary>PING [message] -> "PONG", or the given message echoed back.</summary>
        public static async Task<string> PingAsync(this ICommandExecutor executor, string? message = null, CancellationToken cancellationToken = default)
        {
            var args = message is null ? new object?[] { "PING" } : new object?[] { "PING", message };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? "PONG";
        }

        /// <summary>ECHO message.</summary>
        public static async Task<string> EchoAsync(this ICommandExecutor executor, string message, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "ECHO", message }, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>INFO [section] -> the raw, human readable info text.</summary>
        public static async Task<string> InfoAsync(this ICommandExecutor executor, string? section = null, CancellationToken cancellationToken = default)
        {
            var args = section is null ? new object?[] { "INFO" } : new object?[] { "INFO", section };
            var reply = await executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsString() ?? string.Empty;
        }

        /// <summary>DBSIZE.</summary>
        public static async Task<long> DbSizeAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "DBSIZE" }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>FLUSHDB [ASYNC|SYNC].</summary>
        public static Task FlushDbAsync(this ICommandExecutor executor, bool async = false, CancellationToken cancellationToken = default)
        {
            var args = async ? new object?[] { "FLUSHDB", "ASYNC" } : new object?[] { "FLUSHDB" };
            return executor.ExecuteAsync(args, cancellationToken).AsTask();
        }

        /// <summary>TIME -> (unix seconds, microseconds).</summary>
        public static async Task<(long Seconds, long Microseconds)> TimeAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "TIME" }, cancellationToken).ConfigureAwait(false);
            var items = reply.AsArray();
            return (items[0].AsInt64(), items[1].AsInt64());
        }

        /// <summary>CONFIG GET pattern.</summary>
        public static async Task<IReadOnlyDictionary<string, string>> ConfigGetAsync(this ICommandExecutor executor, string pattern, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "CONFIG", "GET", pattern }, cancellationToken).ConfigureAwait(false);
            var result = new Dictionary<string, string>();
            foreach (var (k, v) in reply.AsPairs())
            {
                result[k.AsString() ?? string.Empty] = v.AsString() ?? string.Empty;
            }
            return result;
        }

        /// <summary>CONFIG SET parameter value.</summary>
        public static Task ConfigSetAsync(this ICommandExecutor executor, string parameter, string value, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "CONFIG", "SET", parameter, value }, cancellationToken).AsTask();

        /// <summary>CLIENT SETNAME name.</summary>
        public static Task ClientSetNameAsync(this ICommandExecutor executor, string name, CancellationToken cancellationToken = default) =>
            executor.ExecuteAsync(new object?[] { "CLIENT", "SETNAME", name }, cancellationToken).AsTask();

        /// <summary>CLIENT GETNAME.</summary>
        public static async Task<string?> ClientGetNameAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "CLIENT", "GETNAME" }, cancellationToken).ConfigureAwait(false);
            var name = reply.AsString();
            return string.IsNullOrEmpty(name) ? null : name;
        }

        /// <summary>CLIENT ID.</summary>
        public static async Task<long> ClientIdAsync(this ICommandExecutor executor, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "CLIENT", "ID" }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }
    }
}
