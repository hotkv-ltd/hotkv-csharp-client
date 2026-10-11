// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>
    /// Anything that can run a HotKV command and return its reply:
    /// <see cref="HotKVClient"/> (one command, one round trip against the
    /// pool), a pipeline <see cref="HotKV.Pipelines.IBatch"/> (queued, sent together) and an
    /// <see cref="HotKV.Transactions.ITransaction"/> (queued inside MULTI/EXEC). Every typed
    /// command helper in this package (strings, lists, hashes, the HotKV
    /// exclusive and AI families, ...) is written as an extension method
    /// against this interface, so the full command surface works
    /// identically on the client, in a pipeline and inside a transaction.
    /// </summary>
    public interface ICommandExecutor
    {
        /// <summary>
        /// Sends one command, given as its full list of wire tokens
        /// (including the command name as the first element), and returns
        /// its reply. This is the generic "execute anything" escape hatch:
        /// every typed helper is implemented in terms of it.
        /// </summary>
        ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default);
    }
}
