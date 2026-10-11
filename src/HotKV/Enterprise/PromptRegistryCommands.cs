// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>One stored PROMPT.* template version.</summary>
    public sealed record PromptTemplate(long Version, string Content, string Metadata, long CreatedUnix);

    /// <summary>
    /// PROMPT.* versioned prompt/template registry.
    /// Needs the PromptRegistry license feature. Reachable as
    /// <c>client.PromptRegistry</c>.
    /// </summary>
    public sealed class PromptRegistryCommands
    {
        private readonly ICommandExecutor _executor;

        internal PromptRegistryCommands(ICommandExecutor executor)
        {
            _executor = executor;
        }

        /// <summary>PROMPT.PUT name content [META json] -> the new version number (versions start at 1).</summary>
        public async Task<long> PutAsync(string name, string content, string? meta = null, CancellationToken cancellationToken = default)
        {
            var args = new List<object?> { "PROMPT.PUT", name, content };
            if (meta is not null)
            {
                args.Add("META");
                args.Add(meta);
            }
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>PROMPT.GET name [version]. Omitting version (or passing 0) returns the latest version.</summary>
        public async Task<PromptTemplate?> GetAsync(string name, long? version = null, CancellationToken cancellationToken = default)
        {
            var args = version.HasValue ? new object?[] { "PROMPT.GET", name, version.Value } : new object?[] { "PROMPT.GET", name };
            var reply = await _executor.ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            return ParseTemplate(reply);
        }

        /// <summary>PROMPT.LATEST name (alias of GET with no version).</summary>
        public async Task<PromptTemplate?> GetLatestAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PROMPT.LATEST", name }, cancellationToken).ConfigureAwait(false);
            return ParseTemplate(reply);
        }

        private static PromptTemplate? ParseTemplate(RespValue reply)
        {
            if (reply.IsNull)
            {
                return null;
            }
            var items = reply.AsArray();
            return new PromptTemplate(items[0].AsInt64(), items[1].AsString() ?? string.Empty, items[2].AsString() ?? string.Empty, items[3].AsInt64());
        }

        /// <summary>PROMPT.SETACTIVE name version.</summary>
        public Task SetActiveAsync(string name, long version, CancellationToken cancellationToken = default) =>
            _executor.ExecuteAsync(new object?[] { "PROMPT.SETACTIVE", name, version }, cancellationToken).AsTask();

        /// <summary>PROMPT.VERSIONS name -> every stored version number.</summary>
        public async Task<IReadOnlyList<long>> VersionsAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PROMPT.VERSIONS", name }, cancellationToken).ConfigureAwait(false);
            var result = new List<long>();
            foreach (var item in reply.AsArray())
            {
                result.Add(item.AsInt64());
            }
            return result;
        }

        /// <summary>PROMPT.DROP name.</summary>
        public async Task<bool> DropAsync(string name, CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PROMPT.DROP", name }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64() == 1;
        }

        /// <summary>PROMPT.LIST.</summary>
        public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        {
            var reply = await _executor.ExecuteAsync(new object?[] { "PROMPT.LIST" }, cancellationToken).ConfigureAwait(false);
            return reply.AsNonNullStringArray();
        }
    }
}
