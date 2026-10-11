// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HotKV;
using HotKV.AI;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Live checks (asserting reply values) of the licensed typed methods the per-family classes do not
    /// reach: LIST commands, the LLM price table, strict USD limits, PCACHE/SCACHE maintenance, TS retention,
    /// keyed cache listing, ALERTS configuration. They run against a server with a valid license and are
    /// skipped (not failed) when the server answers NOLICENSE.
    /// </summary>
    public class EnterpriseTypedMethodTests : IntegrationTestBase
    {
        public EnterpriseTypedMethodTests(TestServerFixture fixture) : base(fixture)
        {
        }

        private static string Id(string label) => "csharp-" + label + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        [SkippableFact]
        public async Task LlmPriceTableSetGetListDelete()
        {
            RequireServer();
            string model = Id("model");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.LlmStats.PriceSetAsync(model, 3, 15, cacheReadUsdPerMtok: 0.3, cacheWrite5mUsdPerMtok: 3.75, cacheWrite1hUsdPerMtok: 6);
                    var p = await Client.LlmStats.PriceGetAsync(model);
                    Assert.True(p.Priced);
                    Assert.Equal(model, p.Model);
                    Assert.Equal(3.0, p.InputUsdPerMtok);
                    Assert.Equal(15.0, p.OutputUsdPerMtok);
                    Assert.Equal(0.3, p.CacheReadUsdPerMtok);
                    Assert.Equal(3.75, p.CacheWrite5mUsdPerMtok);
                    Assert.Equal(6.0, p.CacheWrite1hUsdPerMtok);
                    Assert.Contains(model, (await Client.LlmStats.PriceListAsync()).Select(x => x.Model));

                    // a sub-cent price that a general double formatter would write as an exponent
                    await Client.LlmStats.PriceSetAsync(model, 0.00001, 0.000002);
                    var tiny = await Client.LlmStats.PriceGetAsync(model);
                    Assert.Equal(0.00001, tiny.InputUsdPerMtok);
                    Assert.Equal(0.000002, tiny.OutputUsdPerMtok);

                    Assert.True(await Client.LlmStats.PriceDelAsync(model));
                    Assert.False(await Client.LlmStats.PriceDelAsync(model));
                    Assert.False((await Client.LlmStats.PriceGetAsync(model)).Priced);
                }
                finally
                {
                    await Client.LlmStats.PriceDelAsync(model);
                }
            });
        }

        [SkippableFact]
        public async Task LlmBudgetFloatLimitsAreAcceptedByTheStrictParser()
        {
            RequireServer();
            string name = Id("budget");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    // 1e-5 is the case that went out as "1E-05" with the old formatter.
                    await Client.LlmBudget.SetAsync(name, 1000, 1e-5, 3600);
                    var s = await Client.LlmBudget.StatusAsync(name);
                    Assert.Equal(1e-5, s.CostLimitUsd, 12);

                    // the server really rejects the exponent form the SDK no longer sends
                    var ex = await Assert.ThrowsAsync<HotKVServerException>(async () => await Client.CommandAsync("LLMBUDGET.SET", name, 1000, "1E-05", 3600));
                    Assert.Contains("invalid argument", ex.RawMessage);

                    await Client.LlmBudget.SetAsync(name, 1000, 12.5, 3600);
                    Assert.Equal(12.5, (await Client.LlmBudget.StatusAsync(name)).CostLimitUsd, 12);

                    // consume with a tiny cost goes through the general (from_chars) parser
                    Assert.True(await Client.LlmBudget.ConsumeAsync(name, 10, 0.000001));
                    var after = await Client.LlmBudget.StatusAsync(name);
                    Assert.Equal(10, after.TokensUsed);
                    Assert.Equal(1e-6, after.CostUsedUsd, 12);

                    Assert.Contains(name, await Client.LlmBudget.ListAsync());

                    // a limit the SDK refuses to send (would round to 0 = no limit)
                    await Assert.ThrowsAnyAsync<ArgumentException>(() => Client.LlmBudget.SetAsync(name, 1000, 1e-9, 3600));
                }
                finally
                {
                    await Client.LlmBudget.DropAsync(name);
                }
            });
        }

        [SkippableFact]
        public async Task LlmStatsRecordWithSubCentCostsAndList()
        {
            RequireServer();
            string bucket = Id("stats");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.LlmStats.CreateBucketAsync(bucket);
                    await Client.LlmStats.RecordAsync(bucket, 1, 2, 0, 0.000001, 5);
                    await Client.LlmStats.RecordAsync(bucket, 1, 2, 0, 2.5, 5);
                    var snap = await Client.LlmStats.GetAsync(bucket);
                    Assert.Equal(2, snap.Requests);
                    Assert.Equal(2, snap.InputTokens);
                    Assert.Equal(2.500001, snap.CostUsd, 6);
                    Assert.Contains(bucket, await Client.LlmStats.ListAsync());
                }
                finally
                {
                    await Client.LlmStats.DropAsync(bucket);
                }
            });
        }

        [SkippableFact]
        public async Task PromptCacheMaintenanceCommands()
        {
            RequireServer();
            string ns = Id("pcache");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.PromptCache.CreateNamespaceAsync(ns, maxEntries: 100, ttl: TimeSpan.FromHours(1), costPerHit: 0.00001);
                    Assert.Contains(ns, await Client.PromptCache.ListAsync());

                    await Client.PromptCache.SetAsync(ns, "q1", "a1", meta: "{\"m\":1}");
                    await Client.PromptCache.SetAsync(ns, "q2", "a2");
                    var ext = await Client.PromptCache.GetExtendedAsync(ns, "q1");
                    Assert.NotNull(ext);
                    Assert.Equal("a1", ext!.Response);
                    Assert.Equal("{\"m\":1}", ext.Metadata);
                    Assert.Null(await Client.PromptCache.GetExtendedAsync(ns, "absent"));

                    await Client.PromptCache.ConfigSetAsync(ns, "MAX_ENTRIES", "50");
                    await Client.PromptCache.ConfigSetAsync(ns, "COST_PER_HIT", "0.5");

                    Assert.True(await Client.PromptCache.DeleteAsync(ns, "q2"));
                    Assert.False(await Client.PromptCache.DeleteAsync(ns, "q2"));
                    Assert.Equal(1, await Client.PromptCache.FlushAsync(ns));
                    Assert.Null(await Client.PromptCache.GetAsync(ns, "q1"));
                }
                finally
                {
                    await Client.PromptCache.DropAsync(ns);
                }
            });
        }

        [SkippableFact]
        public async Task SemanticCacheFloat32VectorsThresholdFlushList()
        {
            RequireServer();
            string ns = Id("scache");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.SemanticCache.CreateNamespaceAsync(ns, 3, VectorMetric.Cosine, threshold: 0.9);
                    Assert.Contains(ns, await Client.SemanticCache.ListAsync());

                    // float32 overloads send shortest float32 digits (0.1, 1e-05 as plain decimals)
                    await Client.SemanticCache.SetAsync(ns, "q", "answer", new[] { 0.1f, 0.00001f, 1f });
                    var hit = await Client.SemanticCache.GetAsync(ns, new[] { 0.1f, 0.00001f, 1f });
                    Assert.NotNull(hit);
                    Assert.Equal("answer", hit!.Response);
                    Assert.True(hit.Similarity > 0.99);
                    // double overload, nearly the same vector
                    Assert.NotNull(await Client.SemanticCache.GetAsync(ns, new[] { 0.1, 0.00001, 0.99 }));

                    // an orthogonal vector misses at 0.9 and hits once the threshold is lowered to 0
                    Assert.Null(await Client.SemanticCache.GetAsync(ns, new[] { 1.0, 0.0, 0.0 }));
                    await Client.SemanticCache.SetThresholdAsync(ns, 0.00001);
                    Assert.NotNull(await Client.SemanticCache.GetAsync(ns, new[] { 1.0, 0.0, 0.0 }));

                    var stats = await Client.SemanticCache.StatsAsync(ns);
                    Assert.Equal(1, stats.Entries);
                    Assert.True(stats.Hits >= 2);

                    Assert.True(await Client.SemanticCache.FlushAsync(ns));
                    Assert.Equal(0, (await Client.SemanticCache.StatsAsync(ns)).Entries);
                }
                finally
                {
                    await Client.SemanticCache.DropAsync(ns);
                }
            });
        }

        [SkippableFact]
        public async Task KeyedCachesListFlushAndStats()
        {
            RequireServer();
            await SkipIfNotLicensedAsync(async () =>
            {
                foreach (var (family, label) in new[] { (Client.ToolCache, "tool"), (Client.NegativeCache, "neg"), (Client.GuardCache, "guard"), (Client.RerankCache, "rerank") })
                {
                    string ns = Id(label);
                    try
                    {
                        await family.CreateAsync(ns, maxEntries: 10, ttl: TimeSpan.FromMinutes(5));
                        Assert.Contains(ns, await family.ListAsync());
                        await family.SetAsync(ns, "k1", "v1", meta: "m");
                        await family.SetAsync(ns, "k2", "v2", ttl: TimeSpan.FromSeconds(60));
                        Assert.Equal("v1", await family.GetAsync(ns, "k1"));
                        Assert.Null(await family.GetAsync(ns, "nope"));
                        var st = await family.StatsAsync(ns);
                        Assert.Equal(2, st.Entries);
                        Assert.Equal(1, st.Hits);
                        Assert.Equal(1, st.Misses);
                        Assert.Equal(2, await family.FlushAsync(ns));
                        Assert.Null(await family.GetAsync(ns, "k1"));
                    }
                    finally
                    {
                        await family.DropAsync(ns);
                    }
                }
            });
        }

        [SkippableFact]
        public async Task TimeSeriesRetentionAndFloatValues()
        {
            RequireServer();
            string key = Id("ts");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.TimeSeries.CreateAsync(key, retentionMs: 60000, labels: new Dictionary<string, string> { ["sensor"] = "a" });
                    await Client.TimeSeries.SetRetentionAsync(key, 120000);
                    var info = await Client.TimeSeries.InfoAsync(key);
                    Assert.Equal(120000, info.RetentionMs);

                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await Client.TimeSeries.AddAsync(key, now, 0.000001);
                    await Client.TimeSeries.AddAsync(key, now + 1, 1e21);
                    var range = await Client.TimeSeries.RangeAsync(key);
                    Assert.Equal(2, range.Count);
                    Assert.Equal(1e-6, range[0].Value, 12);
                    Assert.Equal(1e21, range[1].Value, 3);
                    Assert.Contains(key, await Client.TimeSeries.ListAsync("sensor", "a"));
                }
                finally
                {
                    await Client.TimeSeries.DeleteAsync(key);
                }
            });
        }

        [SkippableFact]
        public async Task ListCommandsOfTheRegistryFamilies()
        {
            RequireServer();
            await SkipIfNotLicensedAsync(async () =>
            {
                string tpl = Id("prompt");
                string dedup = Id("dedup");
                string agent = Id("agent");
                string limiter = Id("limiter");
                try
                {
                    await Client.PromptRegistry.PutAsync(tpl, "hello");
                    Assert.Contains(tpl, await Client.PromptRegistry.ListAsync());

                    await Client.EmbedDedup.PutAsync(dedup, "h", "m", "0.1,0.2");
                    Assert.Contains(dedup, await Client.EmbedDedup.ListAsync());

                    await Client.AgentMemory.Summary.SetAsync(agent, "s");
                    Assert.Contains(agent, await Client.AgentMemory.Summary.ListAsync());

                    await Client.RateLimit.CreateAsync(limiter, 5, 1000, RateLimitAlgorithm.TokenBucket, refillRate: 0.5);
                    Assert.Contains(limiter, await Client.RateLimit.ListAsync());
                    var first = await Client.RateLimit.CheckAsync(limiter, "user1");
                    Assert.True(first.Allowed);
                }
                finally
                {
                    await Client.PromptRegistry.DropAsync(tpl);
                    await Client.EmbedDedup.DropAsync(dedup);
                    await Client.AgentMemory.Summary.DropAsync(agent);
                    await Client.RateLimit.DropAsync(limiter);
                }
            });
        }

        [SkippableFact]
        public async Task AlertsConfigurationRoundTrip()
        {
            RequireServer();
            await SkipIfNotLicensedAsync(async () =>
            {
                var before = await Client.Observe.AlertsInfoAsync();
                string originalSeverity = before.MinSeverity;
                try
                {
                    Assert.StartsWith("OK", await Client.Observe.SetMinSeverityAsync("critical"));
                    Assert.Equal("critical", (await Client.Observe.AlertsInfoAsync()).MinSeverity);
                    Assert.StartsWith("OK", await Client.Observe.SetMinSeverityAsync("info"));
                    Assert.Equal("info", (await Client.Observe.AlertsInfoAsync()).MinSeverity);

                    // The webhook URL must be https; the server says so (nothing is ever contacted here).
                    var badUrl = await Assert.ThrowsAsync<HotKVServerException>(() => Client.Observe.SetWebhookUrlAsync("http://127.0.0.1:1/hook"));
                    Assert.Contains("https", badUrl.RawMessage);

                    // With no webhook configured, ALERTS TEST reports that alerting is not enabled.
                    var notEnabled = await Assert.ThrowsAsync<HotKVServerException>(() => Client.Observe.AlertsTestAsync("warning", "csharp sdk test"));
                    Assert.Contains("not enabled", notEnabled.RawMessage);

                    await Client.Observe.SetEnabledAsync(false);
                    Assert.False((await Client.Observe.AlertsInfoAsync()).FlagEnabled);
                    await Client.Observe.SetEnabledAsync(true);
                    Assert.True((await Client.Observe.AlertsInfoAsync()).FlagEnabled);
                }
                finally
                {
                    await Client.Observe.SetMinSeverityAsync(originalSeverity);
                    await Client.Observe.SetEnabledAsync(before.FlagEnabled);
                }
            });
        }
    }
}
