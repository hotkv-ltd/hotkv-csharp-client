// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class TransactionTests : IntegrationTestBase
    {
        public TransactionTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task QueuedCommandsCommitTogether()
        {
            RequireServer();
            string k1 = UniqueKey("tx-1");
            string k2 = UniqueKey("tx-2");
            try
            {
                await using var tx = await Client.CreateTransactionAsync();
                var setTask = tx.StringSetAsync(k1, "a");
                var incrTask = tx.IncrementByAsync(k2, 3);

                bool committed = await tx.ExecuteAsync();
                Assert.True(committed);
                Assert.True(await setTask);
                Assert.Equal(3, await incrTask);
                Assert.Equal("a", await Client.StringGetAsync(k1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { k1, k2 });
            }
        }

        [SkippableFact]
        public async Task WatchAbortsWhenKeyChangesBeforeExec()
        {
            RequireServer();
            string key = UniqueKey("tx-watch");
            try
            {
                await Client.StringSetAsync(key, "initial");

                await using var tx = await Client.CreateTransactionAsync();
                await tx.WatchAsync(new[] { key });

                // A concurrent writer changes the watched key before EXEC.
                await Client.StringSetAsync(key, "changed-by-someone-else");

                var queued = tx.StringSetAsync(key, "from-transaction");
                bool committed = await tx.ExecuteAsync();

                Assert.False(committed);
                await Assert.ThrowsAsync<HotKVException>(() => queued);
                Assert.Equal("changed-by-someone-else", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task DiscardAbandonsQueuedCommands()
        {
            RequireServer();
            string key = UniqueKey("tx-discard");
            try
            {
                await using var tx = await Client.CreateTransactionAsync();
                var queued = tx.StringSetAsync(key, "should-not-happen");
                await tx.DiscardAsync();

                await Assert.ThrowsAsync<System.InvalidOperationException>(() => queued);
                Assert.False(await Client.ExistsAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task FailingCommandInsideTransactionSurfacesOnlyOnItsOwnTask()
        {
            RequireServer();
            string stringKey = UniqueKey("tx-wrongtype");
            string okKey = UniqueKey("tx-ok");
            try
            {
                await Client.StringSetAsync(stringKey, "not-a-list");

                await using var tx = await Client.CreateTransactionAsync();
                var okTask = tx.StringSetAsync(okKey, "v");
                var badTask = tx.ListLeftPushAsync(stringKey, "x");

                bool committed = await tx.ExecuteAsync();
                Assert.True(committed);
                Assert.True(await okTask);
                await Assert.ThrowsAsync<HotKVWrongTypeException>(() => badTask);
            }
            finally
            {
                await Client.DeleteAsync(new[] { stringKey, okKey });
            }
        }
    }
}
