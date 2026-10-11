// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;

namespace HotKV.Commands
{
    /// <summary>The publishing half of pub/sub; the subscribing half is <see cref="HotKV.PubSub.HotKVSubscriber"/>.</summary>
    public static class PubSubCommands
    {
        /// <summary>PUBLISH channel message -> number of subscribers that received it.</summary>
        public static async Task<long> PublishAsync(this ICommandExecutor executor, string channel, string message, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "PUBLISH", channel, message }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }

        /// <summary>SPUBLISH shardchannel message -> number of subscribers that received it.</summary>
        public static async Task<long> ShardPublishAsync(this ICommandExecutor executor, string shardChannel, string message, CancellationToken cancellationToken = default)
        {
            var reply = await executor.ExecuteAsync(new object?[] { "SPUBLISH", shardChannel, message }, cancellationToken).ConfigureAwait(false);
            return reply.AsInt64();
        }
    }
}
