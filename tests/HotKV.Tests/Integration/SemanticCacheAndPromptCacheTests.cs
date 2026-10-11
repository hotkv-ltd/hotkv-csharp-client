// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading.Tasks;
using HotKV;
using HotKV.AI;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class SemanticCacheTests : IntegrationTestBase
    {
        public SemanticCacheTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateSetGetRoundTrip()
        {
            RequireServer();
            string ns = UniqueKey("scache");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.SemanticCache.CreateNamespaceAsync(ns, 3, VectorMetric.Cosine, threshold: 0.5);
                    await Client.SemanticCache.SetAsync(ns, "what is the capital of france", "Paris", new[] { 1.0, 0.0, 0.0 },
                        usage: new Usage("gpt-4") { Input = 10, Output = 4 });

                    var hit = await Client.SemanticCache.GetAsync(ns, new[] { 1.0, 0.0, 0.0 });
                    Assert.NotNull(hit);
                    Assert.Equal("Paris", hit!.Response);
                    Assert.True(hit.Similarity > 0.9);

                    var miss = await Client.SemanticCache.GetAsync(ns, new[] { 0.0, 0.0, 1.0 });
                    Assert.Null(miss);

                    var stats = await Client.SemanticCache.StatsAsync(ns);
                    Assert.True(stats.Entries >= 1);
                    Assert.True(stats.UsageSaved.Requests >= 1);
                    Assert.True(stats.UsageSaved.InputTokens >= 10);
                }
                finally
                {
                    await Client.SemanticCache.DropAsync(ns);
                }
            });
        }

        [SkippableFact]
        public async Task GetOrComputeOnlyCallsFunctionOnMiss()
        {
            RequireServer();
            string ns = UniqueKey("scache-getorcompute");
            int calls = 0;
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.SemanticCache.CreateNamespaceAsync(ns, 2, threshold: 0.8);

                    var first = await Client.SemanticCache.GetOrComputeAsync(ns, "hi", new[] { 1.0, 0.0 }, _ =>
                    {
                        calls++;
                        return Task.FromResult(new LlmCallResult("computed-answer"));
                    });
                    Assert.False(first.Hit);
                    Assert.Equal("computed-answer", first.Response);
                    Assert.Equal(1, calls);

                    var second = await Client.SemanticCache.GetOrComputeAsync(ns, "hi", new[] { 1.0, 0.0 }, _ =>
                    {
                        calls++;
                        return Task.FromResult(new LlmCallResult("should-not-be-called"));
                    });
                    Assert.True(second.Hit);
                    Assert.Equal("computed-answer", second.Response);
                    Assert.Equal(1, calls); // the LLM/compute function was not invoked again.
                }
                finally
                {
                    await Client.SemanticCache.DropAsync(ns);
                }
            });
        }
    }

    public class PromptCacheTests : IntegrationTestBase
    {
        public PromptCacheTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task RawCommandsAreSkippedWithoutFullLicense()
        {
            RequireServer();
            string ns = UniqueKey("pcache");
            await SkipIfNotLicensedAsync(async () =>
            {
                await Client.PromptCache.CreateNamespaceAsync(ns);
                await Client.PromptCache.SetAsync(ns, "prompt", "response", usage: new Usage("gpt-4") { Input = 12, Output = 8 });
                Assert.Equal("response", await Client.PromptCache.GetAsync(ns, "prompt"));

                var stats = await Client.PromptCache.StatsAsync(ns);
                Assert.True(stats.Entries >= 1);
                Assert.True(stats.UsageSaved.InputTokens >= 12);

                await Client.PromptCache.DropAsync(ns);
            });
        }

        [SkippableFact]
        public async Task GetOrCallRunsTheFunctionAtMostOnce()
        {
            RequireServer();
            string ns = UniqueKey("pcache-getorcall");
            int calls = 0;
            var request = new PromptCacheKeyRequest
            {
                Provider = "openai",
                Model = "gpt-4o",
                Messages = new[] { new PromptMessage("user", "what is 2+2") },
                Params = new Dictionary<string, string> { ["temperature"] = "0.0" },
            };

            await SkipIfNotLicensedAsync(async () =>
            {
                // The namespace must exist first: PCACHE.GET on an unknown namespace is "ERR namespace not found".
                await Client.PromptCache.CreateNamespaceAsync(ns);
                try
                {
                    var result = await Client.PromptCache.GetOrCallAsync(ns, request, _ =>
                    {
                        calls++;
                        return Task.FromResult(new LlmCallResult("4", new LlmUsage("gpt-4o", 20, 4)));
                    });
                    Assert.False(result.Hit);
                    Assert.Equal("4", result.Response);

                    var second = await Client.PromptCache.GetOrCallAsync(ns, request, _ =>
                    {
                        calls++;
                        return Task.FromResult(new LlmCallResult("should-not-run"));
                    });
                    Assert.True(second.Hit);
                    Assert.Equal("4", second.Response);
                    Assert.Equal(1, calls);
                }
                finally
                {
                    await Client.PromptCache.DropAsync(ns);
                }
            });
        }
    }
}
