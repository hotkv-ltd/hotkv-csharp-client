// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class LlmStatsTests : IntegrationTestBase
    {
        public LlmStatsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task BucketRecordGetReset()
        {
            RequireServer();
            string bucket = UniqueKey("llmstats");
            await SkipIfNotLicensedAsync(async () =>
            {
                await Client.LlmStats.CreateBucketAsync(bucket);
                await Client.LlmStats.RecordAsync(bucket, inputTokens: 10, outputTokens: 20, cachedTokens: 0, costUsd: 0.01, latencyMs: 100);
                var snapshot = await Client.LlmStats.GetAsync(bucket);
                Assert.Equal(1, snapshot.Requests);
                Assert.Equal(10, snapshot.InputTokens);
                Assert.True(await Client.LlmStats.ResetAsync(bucket));
                Assert.True(await Client.LlmStats.DropAsync(bucket));
            });
        }
    }

    public class LlmBudgetTests : IntegrationTestBase
    {
        public LlmBudgetTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task SetConsumeStatus()
        {
            RequireServer();
            string name = UniqueKey("llmbudget");
            await SkipIfNotLicensedAsync(async () =>
            {
                await Client.LlmBudget.SetAsync(name, tokenLimit: 1000, costLimitUsd: 10.0, windowSeconds: 3600);
                Assert.True(await Client.LlmBudget.ConsumeAsync(name, tokens: 100, costUsd: 1.0));
                var status = await Client.LlmBudget.StatusAsync(name);
                Assert.Equal(100, status.TokensUsed);
                Assert.True(await Client.LlmBudget.ResetAsync(name));
                Assert.True(await Client.LlmBudget.DropAsync(name));
            });
        }
    }
}
