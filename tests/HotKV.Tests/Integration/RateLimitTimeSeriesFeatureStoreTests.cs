// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class RateLimitTests : IntegrationTestBase
    {
        public RateLimitTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateCheckPeekReset()
        {
            RequireServer();
            string name = UniqueKey("ratelimit");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.RateLimit.CreateAsync(name, limit: 2, windowMs: 60_000);

                    var first = await Client.RateLimit.CheckAsync(name, "user1");
                    Assert.True(first.Allowed);
                    var second = await Client.RateLimit.CheckAsync(name, "user1");
                    Assert.True(second.Allowed);
                    var third = await Client.RateLimit.CheckAsync(name, "user1");
                    Assert.False(third.Allowed);

                    var peek = await Client.RateLimit.PeekAsync(name, "user1");
                    Assert.False(peek.Allowed);

                    await Client.RateLimit.ResetAsync(name, "user1");
                    var afterReset = await Client.RateLimit.CheckAsync(name, "user1");
                    Assert.True(afterReset.Allowed);

                    var stats = await Client.RateLimit.StatsAsync(name);
                    Assert.Equal(name, stats.Name);
                }
                finally
                {
                    await Client.RateLimit.DropAsync(name);
                }
            });
        }

        [SkippableFact]
        public async Task TokenBucketWithRefill()
        {
            RequireServer();
            string name = UniqueKey("ratelimit-tb");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.RateLimit.CreateAsync(name, limit: 5, windowMs: 1000, algorithm: RateLimitAlgorithm.TokenBucket, refillRate: 1);
                    var result = await Client.RateLimit.CheckAsync(name, "k", cost: 2);
                    Assert.True(result.Allowed);
                }
                finally
                {
                    await Client.RateLimit.DropAsync(name);
                }
            });
        }
    }

    public class TimeSeriesTests : IntegrationTestBase
    {
        public TimeSeriesTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateAddRangeAggregateInfo()
        {
            RequireServer();
            string key = UniqueKey("ts");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.TimeSeries.CreateAsync(key, retentionMs: 3_600_000, labels: new Dictionary<string, string> { ["sensor"] = "temp" });

                    // Retention is enforced against wall clock time, so use
                    // realistic near-current timestamps (an epoch-relative
                    // timestamp like 1000ms would already be outside any
                    // sane retention window).
                    long baseMs = System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    await Client.TimeSeries.AddAsync(key, baseMs, 10.0);
                    await Client.TimeSeries.AddAsync(key, baseMs + 1000, 20.0);
                    await Client.TimeSeries.AddAsync(key, baseMs + 2000, 30.0);

                    var range = await Client.TimeSeries.RangeAsync(key, baseMs.ToString(), (baseMs + 5000).ToString());
                    Assert.Equal(3, range.Count);

                    var agg = await Client.TimeSeries.AggregateAsync(key, baseMs.ToString(), (baseMs + 5000).ToString(), 5000, TimeSeriesAggregation.Avg);
                    Assert.NotEmpty(agg);
                    Assert.Equal(20.0, agg[0].Value, 3);

                    var info = await Client.TimeSeries.InfoAsync(key);
                    Assert.Equal(3, info.TotalSamples);
                    Assert.Contains(info.Labels, l => l.Label == "sensor" && l.Value == "temp");

                    var list = await Client.TimeSeries.ListAsync("sensor", "temp");
                    Assert.Contains(key, list);
                }
                finally
                {
                    await Client.TimeSeries.DeleteAsync(key);
                }
            });
        }

        [SkippableFact]
        public async Task DownsampleRule()
        {
            RequireServer();
            string source = UniqueKey("ts-src");
            string dest = UniqueKey("ts-dest");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.TimeSeries.CreateAsync(source);
                    await Client.TimeSeries.CreateAsync(dest);
                    await Client.TimeSeries.DownsampleAsync(source, dest, TimeSeriesAggregation.Avg, 60_000);
                    await Client.TimeSeries.AddNowAsync(source, 5.0);
                }
                finally
                {
                    await Client.TimeSeries.DeleteAsync(source);
                    await Client.TimeSeries.DeleteAsync(dest);
                }
            });
        }
    }

    public class FeatureStoreTests : IntegrationTestBase
    {
        public FeatureStoreTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateSetGetBatchSchemaStats()
        {
            RequireServer();
            string group = UniqueKey("feature-group");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.FeatureStore.CreateAsync(group, "user features", new (string, FeatureType)[]
                    {
                        ("age", FeatureType.Int64),
                        ("score", FeatureType.Double),
                    });

                    await Client.FeatureStore.SetAsync(group, "user1", new Dictionary<string, string> { ["age"] = "30", ["score"] = "1.5" });

                    var record = await Client.FeatureStore.GetAsync(group, "user1");
                    Assert.NotNull(record);
                    Assert.Equal("30", record!.Values["age"]);

                    var batch = await Client.FeatureStore.BatchGetAsync(group, new[] { "user1", "user-missing" });
                    Assert.NotNull(batch[0]);
                    Assert.Null(batch[1]);

                    var schema = await Client.FeatureStore.SchemaAsync(group);
                    Assert.Contains(schema, s => s.Name == "age");

                    var stats = await Client.FeatureStore.StatsAsync(group);
                    Assert.True(stats.Entities >= 1);

                    var groups = await Client.FeatureStore.ListAsync();
                    Assert.Contains(group, groups);
                }
                finally
                {
                    await Client.FeatureStore.DropAsync(group);
                }
            });
        }
    }
}
