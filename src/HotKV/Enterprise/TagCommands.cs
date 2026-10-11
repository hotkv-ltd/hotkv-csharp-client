// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV
{
    /// <summary>
    /// TAG.* key tagging commands. Core: no license
    /// feature is required. Reachable as <c>client.Tags</c>.
    /// </summary>
    public sealed class TagCommands
    {
        private readonly ICommandExecutor _executor;

        internal TagCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>TAG.SET key tag [tag ...] -> number of tags newly added. Errors "ERR no such key" if key does not exist.</summary>
        public async Task<long> SetAsync(string key, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(tags.Count + 2) { "TAG.SET", key };
            args.AddRange(tags);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>TAG.SET key tag.</summary>
        public Task<long> SetAsync(string key, string tag, CancellationToken cancellationToken = default) =>
            SetAsync(key, new[] { tag }, cancellationToken);

        /// <summary>TAG.REM key tag [tag ...] -> number of tags removed.</summary>
        public async Task<long> RemoveAsync(string key, IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(tags.Count + 2) { "TAG.REM", key };
            args.AddRange(tags);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>TAG.GET key -> every tag on key.</summary>
        public async Task<IReadOnlyList<string>> GetAsync(string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TAG.GET", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>TAG.MEMBERS tag -> every key carrying tag.</summary>
        public async Task<IReadOnlyList<string>> MembersAsync(string tag, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TAG.MEMBERS", tag }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>TAG.INTER tag [tag ...] -> keys carrying every given tag.</summary>
        public async Task<IReadOnlyList<string>> IntersectAsync(IReadOnlyList<string> tags, CancellationToken cancellationToken = default)
        {
            var args = new List<object?>(tags.Count + 1) { "TAG.INTER" };
            args.AddRange(tags);
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }

        /// <summary>TAG.COUNT key -> number of tags on key.</summary>
        public async Task<long> CountAsync(string key, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TAG.COUNT", key }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>TAG.EXISTS key tag.</summary>
        public async Task<bool> ExistsAsync(string key, string tag, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "TAG.EXISTS", key, tag }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }
    }
}
