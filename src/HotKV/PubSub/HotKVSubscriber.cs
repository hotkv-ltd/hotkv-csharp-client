// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Connection;
using HotKV.Internal;

namespace HotKV.PubSub
{
    /// <summary>The kind of pub/sub frame a <see cref="HotKVMessage"/> carries.</summary>
    public enum HotKVMessageKind
    {
        Message,
        PMessage,
        SMessage,
        Subscribe,
        PSubscribe,
        SSubscribe,
        Unsubscribe,
        PUnsubscribe,
        SUnsubscribe,
    }

    /// <summary>One pub/sub frame: a published message, or a subscribe/unsubscribe acknowledgement.</summary>
    public sealed record HotKVMessage(HotKVMessageKind Kind, string Channel, string? Pattern, string? Payload, long SubscriptionCount);

    /// <summary>
    /// A dedicated pub/sub connection. Messages are delivered both as an
    /// <see cref="IAsyncEnumerable{T}"/> via <see cref="Messages"/> and as a
    /// plain callback via <see cref="OnMessage"/>, so callers can pick
    /// whichever fits (an iterator loop, or a fire-and-forget handler).
    /// RESP3 push frames and RESP2 multi-bulk pushes are both understood
    /// transparently. Not thread safe for issuing subscribe/unsubscribe
    /// calls concurrently with itself, but message delivery runs on its own
    /// background loop.
    /// </summary>
    public sealed class HotKVSubscriber : IAsyncDisposable
    {
        private readonly HotKVConnection _connection;
        private readonly AsyncQueue<HotKVMessage> _queue = new AsyncQueue<HotKVMessage>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _readLoop;
        private int _disposed;

        /// <summary>Raised on the background read loop for every message and subscribe/unsubscribe acknowledgement. Exceptions thrown by handlers are swallowed so one bad handler cannot stop delivery.</summary>
        public event Action<HotKVMessage>? OnMessage;

        internal HotKVSubscriber(HotKVConnection connection)
        {
            _connection = connection;
            _readLoop = Task.Run(() => ReadLoopAsync(_cts.Token));
        }

        /// <summary>SUBSCRIBE channel [channel ...].</summary>
        public Task SubscribeAsync(params string[] channels) => SendAsync("SUBSCRIBE", channels);

        /// <summary>PSUBSCRIBE pattern [pattern ...].</summary>
        public Task PSubscribeAsync(params string[] patterns) => SendAsync("PSUBSCRIBE", patterns);

        /// <summary>SSUBSCRIBE shardchannel [shardchannel ...].</summary>
        public Task SSubscribeAsync(params string[] shardChannels) => SendAsync("SSUBSCRIBE", shardChannels);

        /// <summary>UNSUBSCRIBE [channel ...]. No channels unsubscribes from all of them.</summary>
        public Task UnsubscribeAsync(params string[] channels) => SendAsync("UNSUBSCRIBE", channels);

        /// <summary>PUNSUBSCRIBE [pattern ...].</summary>
        public Task PUnsubscribeAsync(params string[] patterns) => SendAsync("PUNSUBSCRIBE", patterns);

        /// <summary>SUNSUBSCRIBE [shardchannel ...].</summary>
        public Task SUnsubscribeAsync(params string[] shardChannels) => SendAsync("SUNSUBSCRIBE", shardChannels);

        private Task SendAsync(string command, string[] args)
        {
            var tokens = new List<object?>(args.Length + 1) { command };
            tokens.AddRange(args);
            return _connection.WriteCommandAsync(tokens).AsTask();
        }

        /// <summary>Streams every message and subscribe/unsubscribe acknowledgement as they arrive. Completes when the subscriber is disposed; faults if the connection is lost.</summary>
        public IAsyncEnumerable<HotKVMessage> Messages(CancellationToken cancellationToken = default) => _queue.ReadAllAsync(cancellationToken);

        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RespValue value = await _connection.Reader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
                    var message = Parse(value);
                    if (message is null)
                    {
                        continue;
                    }
                    try
                    {
                        OnMessage?.Invoke(message);
                    }
                    catch
                    {
                        // A misbehaving handler must not stop message delivery.
                    }
                    _queue.Enqueue(message);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _queue.Complete();
            }
            catch (Exception ex)
            {
                _queue.Complete(ex);
            }
        }

        private static HotKVMessage? Parse(RespValue value)
        {
            if (!value.IsAggregate)
            {
                return null;
            }
            var items = value.AsArray();
            if (items.Count == 0)
            {
                return null;
            }
            string kind = (items[0].AsString() ?? string.Empty).ToLowerInvariant();
            return kind switch
            {
                "message" => new HotKVMessage(HotKVMessageKind.Message, items[1].AsString() ?? string.Empty, null, items[2].AsString(), 0),
                "pmessage" => new HotKVMessage(HotKVMessageKind.PMessage, items[2].AsString() ?? string.Empty, items[1].AsString(), items[3].AsString(), 0),
                "smessage" => new HotKVMessage(HotKVMessageKind.SMessage, items[1].AsString() ?? string.Empty, null, items[2].AsString(), 0),
                "subscribe" => new HotKVMessage(HotKVMessageKind.Subscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                "psubscribe" => new HotKVMessage(HotKVMessageKind.PSubscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                "ssubscribe" => new HotKVMessage(HotKVMessageKind.SSubscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                "unsubscribe" => new HotKVMessage(HotKVMessageKind.Unsubscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                "punsubscribe" => new HotKVMessage(HotKVMessageKind.PUnsubscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                "sunsubscribe" => new HotKVMessage(HotKVMessageKind.SUnsubscribe, items[1].AsString() ?? string.Empty, null, null, items[2].AsInt64()),
                _ => null,
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }
            _cts.Cancel();
            try
            {
                await _readLoop.ConfigureAwait(false);
            }
            catch
            {
                // already surfaced to Messages()/OnMessage; disposal itself never throws.
            }
            _cts.Dispose();
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
