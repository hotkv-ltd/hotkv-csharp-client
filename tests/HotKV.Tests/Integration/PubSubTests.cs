// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using HotKV.PubSub;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class PubSubTests : IntegrationTestBase
    {
        public PubSubTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task IteratorReceivesPublishedMessages()
        {
            RequireServer();
            string channel = UniqueKey("chan");
            await using var subscriber = await Client.CreateSubscriberAsync();
            await subscriber.SubscribeAsync(channel);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var enumerator = subscriber.Messages(cts.Token).GetAsyncEnumerator(cts.Token);

            // First frame is the subscribe acknowledgement.
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.Subscribe, enumerator.Current.Kind);

            long receivers = await Client.PublishAsync(channel, "hello");
            Assert.True(receivers >= 1);

            Assert.True(await enumerator.MoveNextAsync());
            var message = enumerator.Current;
            Assert.Equal(HotKVMessageKind.Message, message.Kind);
            Assert.Equal(channel, message.Channel);
            Assert.Equal("hello", message.Payload);
        }

        [SkippableFact]
        public async Task CallbackReceivesPublishedMessages()
        {
            RequireServer();
            string channel = UniqueKey("chan-cb");
            await using var subscriber = await Client.CreateSubscriberAsync();

            var subscribed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var received = new TaskCompletionSource<HotKVMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Attach the handler before subscribing so it cannot miss the
            // subscribe acknowledgement, which the background read loop may
            // otherwise deliver before a handler added afterward would see it.
            subscriber.OnMessage += m =>
            {
                if (m.Kind == HotKVMessageKind.Subscribe)
                {
                    subscribed.TrySetResult(true);
                }
                if (m.Kind == HotKVMessageKind.Message)
                {
                    received.TrySetResult(m);
                }
            };

            await subscriber.SubscribeAsync(channel);
            await subscribed.Task.WaitAsync(TimeSpan.FromSeconds(10));

            await Client.PublishAsync(channel, "via-callback");

            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("via-callback", message.Payload);
        }

        [SkippableFact]
        public async Task PatternSubscribeMatchesChannels()
        {
            RequireServer();
            string prefix = UniqueKey("pchan");
            string channel = prefix + ":room1";
            await using var subscriber = await Client.CreateSubscriberAsync();
            await subscriber.PSubscribeAsync(prefix + ":*");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var enumerator = subscriber.Messages(cts.Token).GetAsyncEnumerator(cts.Token);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.PSubscribe, enumerator.Current.Kind);

            await Client.PublishAsync(channel, "pattern-hit");

            Assert.True(await enumerator.MoveNextAsync());
            var message = enumerator.Current;
            Assert.Equal(HotKVMessageKind.PMessage, message.Kind);
            Assert.Equal(channel, message.Channel);
            Assert.Equal(prefix + ":*", message.Pattern);
            Assert.Equal("pattern-hit", message.Payload);
        }

        [SkippableFact]
        public async Task UnsubscribeStopsDelivery()
        {
            RequireServer();
            string channel = UniqueKey("chan-unsub");
            await using var subscriber = await Client.CreateSubscriberAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var enumerator = subscriber.Messages(cts.Token).GetAsyncEnumerator(cts.Token);

            await subscriber.SubscribeAsync(channel);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.Subscribe, enumerator.Current.Kind);

            await subscriber.UnsubscribeAsync(channel);
            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.Unsubscribe, enumerator.Current.Kind);

            long receivers = await Client.PublishAsync(channel, "nobody-home");
            Assert.Equal(0, receivers);
        }

        [SkippableFact]
        public async Task SSubscribeReceivesShardMessages()
        {
            RequireServer();
            string channel = UniqueKey("schan");
            await using var subscriber = await Client.CreateSubscriberAsync();
            await subscriber.SSubscribeAsync(channel);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var enumerator = subscriber.Messages(cts.Token).GetAsyncEnumerator(cts.Token);

            Assert.True(await enumerator.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.SSubscribe, enumerator.Current.Kind);

            long receivers = await Client.ShardPublishAsync(channel, "shard-hello");
            Assert.True(receivers >= 1);

            Assert.True(await enumerator.MoveNextAsync());
            var message = enumerator.Current;
            Assert.Equal(HotKVMessageKind.SMessage, message.Kind);
            Assert.Equal(channel, message.Channel);
            Assert.Equal("shard-hello", message.Payload);
        }
    }
}
