// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class AgentMemoryTests : IntegrationTestBase
    {
        public AgentMemoryTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task ConversationHistory()
        {
            RequireServer();
            string session = UniqueKey("amem-conv");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.AgentMemory.Conversation.CreateAsync(session, window: 10);
                    await Client.AgentMemory.Conversation.AppendAsync(session, "user", "hello");
                    await Client.AgentMemory.Conversation.AppendAsync(session, "assistant", "hi there");

                    var turns = await Client.AgentMemory.Conversation.GetAsync(session);
                    Assert.Equal(2, turns.Count);
                    Assert.Equal("user", turns[0].Role);
                    Assert.Equal("hello", turns[0].Content);

                    var window = await Client.AgentMemory.Conversation.WindowAsync(session, 1);
                    Assert.Single(window);
                    Assert.Equal("hi there", window[0].Content);

                    Assert.True(await Client.AgentMemory.Conversation.ClearAsync(session));
                }
                finally
                {
                    await Client.AgentMemory.Conversation.DropAsync(session);
                }
            });
        }

        [SkippableFact]
        public async Task WorkingMemory()
        {
            RequireServer();
            string session = UniqueKey("amem-work");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.AgentMemory.Working.SetAsync(session, "scratch", "value1");
                    Assert.Equal("value1", await Client.AgentMemory.Working.GetAsync(session, "scratch"));
                    var keys = await Client.AgentMemory.Working.ListAsync(session);
                    Assert.Contains("scratch", keys);
                    Assert.True(await Client.AgentMemory.Working.DeleteAsync(session, "scratch"));
                }
                finally
                {
                    await Client.AgentMemory.Working.ClearAsync(session);
                }
            });
        }

        [SkippableFact]
        public async Task EpisodicLog()
        {
            RequireServer();
            string agentId = UniqueKey("amem-epis");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.AgentMemory.Episodic.LogAsync(agentId, "tool_call", "called search()", metadata: "{\"tool\":\"search\"}");
                    var events = await Client.AgentMemory.Episodic.QueryAsync(agentId);
                    Assert.Single(events);
                    Assert.Equal("tool_call", events[0].Type);

                    var stats = await Client.AgentMemory.Episodic.StatsAsync(agentId);
                    Assert.Equal(1, stats.TotalEvents);
                }
                finally
                {
                    await Client.AgentMemory.Episodic.ClearAsync(agentId);
                }
            });
        }

        [SkippableFact]
        public async Task SemanticMemoryAndCrossSearch()
        {
            RequireServer();
            string agentId = UniqueKey("amem-sem");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.AgentMemory.Semantic.CreateAsync(agentId, 3);
                    await Client.AgentMemory.Semantic.StoreAsync(agentId, "fact1", "the sky is blue", new[] { 1.0, 0.0, 0.0 });

                    var recalled = await Client.AgentMemory.Semantic.RecallAsync(agentId, 1, new[] { 1.0, 0.0, 0.0 });
                    Assert.Single(recalled);
                    Assert.Equal("fact1", recalled[0].Key);
                    Assert.Equal("the sky is blue", recalled[0].Content);

                    var searched = await Client.AgentMemory.SearchAsync(agentId, 5, new[] { 1.0, 0.0, 0.0 });
                    Assert.NotEmpty(searched);

                    Assert.True(await Client.AgentMemory.Semantic.ForgetAsync(agentId, "fact1"));
                }
                finally
                {
                    var remaining = await Client.AgentMemory.Semantic.ListAsync(agentId);
                    foreach (var k in remaining)
                    {
                        await Client.AgentMemory.Semantic.ForgetAsync(agentId, k);
                    }
                }
            });
        }

        [SkippableFact]
        public async Task SummaryNeedsFullLicense()
        {
            RequireServer();
            string agentId = UniqueKey("amem-summary");
            await SkipIfNotLicensedAsync(async () =>
            {
                long v1 = await Client.AgentMemory.Summary.SetAsync(agentId, "Initial summary.");
                Assert.Equal(1, v1);
                long v2 = await Client.AgentMemory.Summary.AppendAsync(agentId, " More detail.");
                Assert.Equal(2, v2);
                var snapshot = await Client.AgentMemory.Summary.GetAsync(agentId);
                Assert.NotNull(snapshot);
                Assert.True(await Client.AgentMemory.Summary.DropAsync(agentId));
            });
        }
    }
}
