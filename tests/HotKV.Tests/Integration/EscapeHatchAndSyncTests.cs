// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class EscapeHatchAndSyncTests : IntegrationTestBase
    {
        public EscapeHatchAndSyncTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task GenericCommandAsyncSendsAnyCommand()
        {
            RequireServer();
            string key = UniqueKey("escape-hatch");
            try
            {
                var setReply = await Client.CommandAsync("SET", key, "raw-value");
                Assert.Equal("OK", setReply.AsString());

                var getReply = await Client.CommandAsync("GET", key);
                Assert.Equal("raw-value", getReply.AsString());

                // Also reachable through the low level ICommandExecutor.ExecuteAsync directly.
                var reply = await ((ICommandExecutor)Client).ExecuteAsync(new object?[] { "STRLEN", key });
                Assert.Equal(9, reply.AsInt64());
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public void SyncConveniencesWork()
        {
            RequireServer();
            string key = UniqueKey("sync");
            try
            {
                Assert.Equal("PONG", Client.Ping());
                Assert.True(Client.StringSet(key, "sync-value"));
                Assert.Equal("sync-value", Client.StringGet(key));
                Assert.True(Client.Exists(key));
                Assert.Equal(1, Client.Delete(key));
                Assert.False(Client.Exists(key));
            }
            finally
            {
                Client.Delete(key);
            }
        }
    }
}
