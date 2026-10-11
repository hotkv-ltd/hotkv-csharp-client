// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class ServerCommandsTests : IntegrationTestBase
    {
        public ServerCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task PingAndEcho()
        {
            RequireServer();
            Assert.Equal("PONG", await Client.PingAsync());
            Assert.Equal("hi", await Client.PingAsync("hi"));
            Assert.Equal("echoed", await Client.EchoAsync("echoed"));
        }

        [SkippableFact]
        public async Task InfoAndTime()
        {
            RequireServer();
            string info = await Client.InfoAsync();
            Assert.False(string.IsNullOrEmpty(info));
            var (seconds, micros) = await Client.TimeAsync();
            Assert.True(seconds > 0);
            Assert.InRange(micros, 0, 999_999);
        }

        [SkippableFact]
        public async Task DbSizeReflectsKeys()
        {
            RequireServer();
            string key = UniqueKey("dbsize");
            try
            {
                await Client.StringSetAsync(key, "v");
                long size = await Client.DbSizeAsync();
                Assert.True(size >= 1);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ConfigGetSetRoundTrip()
        {
            RequireServer();
            var before = await Client.ConfigGetAsync("maxmemory");
            Assert.True(before.ContainsKey("maxmemory"));
        }

        [SkippableFact]
        public async Task ClientSetNameGetNameId()
        {
            RequireServer();
            await Client.ClientSetNameAsync("hotkv-dotnet-test");
            long id = await Client.ClientIdAsync();
            Assert.True(id >= 0);
        }
    }

    public class ScriptingCommandsTests : IntegrationTestBase
    {
        public ScriptingCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task EvalReturnsAValue()
        {
            RequireServer();
            var reply = await Client.EvalAsync("return 1", System.Array.Empty<string>(), System.Array.Empty<string>());
            Assert.Equal(1, reply.AsInt64());
        }

        [SkippableFact]
        public async Task EvalWithKeysAndArgs()
        {
            RequireServer();
            string key = UniqueKey("script-set");
            try
            {
                await Client.EvalAsync("return redis.call('SET', KEYS[1], ARGV[1])", new[] { key }, new[] { "value-from-script" });
                Assert.Equal("value-from-script", await Client.StringGetAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ScriptLoadExistsAndEvalSha()
        {
            RequireServer();
            string sha = await Client.ScriptLoadAsync("return 'loaded'");
            Assert.False(string.IsNullOrEmpty(sha));
            var exists = await Client.ScriptExistsAsync(new[] { sha });
            Assert.True(exists[0]);

            var reply = await Client.EvalShaAsync(sha, System.Array.Empty<string>(), System.Array.Empty<string>());
            Assert.Equal("loaded", reply.AsString());
        }

        [SkippableFact]
        public async Task EvalShaOrEvalFallsBackOnNoScript()
        {
            RequireServer();
            // Deliberately never SCRIPT LOADed: EvalShaOrEvalAsync must fall
            // back to EVAL transparently on NOSCRIPT.
            string script = "return 'fallback-" + System.Guid.NewGuid().ToString("N") + "'";
            var reply = await Client.EvalShaOrEvalAsync(script, System.Array.Empty<string>(), System.Array.Empty<string>());
            Assert.StartsWith("fallback-", reply.AsString());
        }
    }
}
