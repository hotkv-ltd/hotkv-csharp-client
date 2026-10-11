// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Tests.Unit
{
    /// <summary>
    /// An in-memory <see cref="ICommandExecutor"/> used to unit test typed
    /// command wrappers without a HotKV server: it captures the exact wire
    /// tokens of the last command it received (through <see cref="RespWriter.ToBytes"/>,
    /// the same encoding a real connection sends over the socket) and
    /// returns a canned reply.
    /// </summary>
    internal sealed class FakeCommandExecutor : ICommandExecutor
    {
        private readonly RespValue _reply;

        public FakeCommandExecutor(RespValue reply)
        {
            _reply = reply;
        }

        /// <summary>The wire tokens (post <see cref="RespWriter.ToBytes"/> encoding) of the last command sent.</summary>
        public IReadOnlyList<string> LastArgs { get; private set; } = System.Array.Empty<string>();

        public ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            LastArgs = args.Select(a => Encoding.UTF8.GetString(RespWriter.ToBytes(a))).ToList();
            return new ValueTask<RespValue>(_reply);
        }
    }
}
