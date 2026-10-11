// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class PipelineTests : IntegrationTestBase
    {
        public PipelineTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task QueuedCommandsRunTogetherAndResolveInOrder()
        {
            RequireServer();
            string k1 = UniqueKey("pipe-1");
            string k2 = UniqueKey("pipe-2");
            try
            {
                var batch = Client.CreateBatch();
                var setTask = batch.StringSetAsync(k1, "a");
                var incrTask = batch.IncrementByAsync(k2, 5);
                var getTask = batch.StringGetAsync(k1);

                Assert.Equal(3, batch.QueuedCount);
                await batch.ExecuteAsync();

                Assert.True(await setTask);
                Assert.Equal(5, await incrTask);
                Assert.Equal("a", await getTask);
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2 });
            }
        }

        [SkippableFact]
        public async Task OneFailingCommandDoesNotFailTheOthers()
        {
            RequireServer();
            string stringKey = UniqueKey("pipe-wrongtype");
            try
            {
                await Client.StringSetAsync(stringKey, "not-a-list");

                var batch = Client.CreateBatch();
                var okTask = batch.StringSetAsync(UniqueKey("pipe-ok"), "v");
                var badTask = batch.ListLeftPushAsync(stringKey, "x"); // WRONGTYPE
                var okTask2 = batch.PingAsync();

                await batch.ExecuteAsync();

                Assert.True(await okTask);
                Assert.Equal("PONG", await okTask2);
                var ex = await Assert.ThrowsAsync<HotKVWrongTypeException>(() => badTask);
                Assert.Equal("WRONGTYPE", ex.Prefix);
            }
            finally
            {
                await Client.DeleteAsync(stringKey);
            }
        }

        [SkippableFact]
        public async Task EmptyBatchExecutesWithoutError()
        {
            RequireServer();
            var batch = Client.CreateBatch();
            await batch.ExecuteAsync();
        }

        [SkippableFact]
        public async Task ExecutingTwiceThrows()
        {
            RequireServer();
            var batch = Client.CreateBatch();
            _ = batch.PingAsync();
            await batch.ExecuteAsync();
            await Assert.ThrowsAsync<System.InvalidOperationException>(() => batch.ExecuteAsync());
        }
    }
}
