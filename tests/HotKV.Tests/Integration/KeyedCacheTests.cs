// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class KeyedCacheTests : IntegrationTestBase
    {
        public KeyedCacheTests(TestServerFixture fixture) : base(fixture)
        {
        }

        private async Task ExerciseAsync(KeyedCacheCommands family, string label)
        {
            string ns = UniqueKey(label);
            await SkipIfNotLicensedAsync(async () =>
            {
                await family.CreateAsync(ns, maxEntries: 100);
                await family.SetAsync(ns, "k", "v");
                Assert.Equal("v", await family.GetAsync(ns, "k"));
                var stats = await family.StatsAsync(ns);
                Assert.True(stats.Entries >= 1);
                Assert.True(await family.DeleteAsync(ns, "k"));
                await family.DropAsync(ns);
            });
        }

        [SkippableFact]
        public Task ToolCacheFamily()
        {
            RequireServer();
            return ExerciseAsync(Client.ToolCache, "toolcache");
        }

        [SkippableFact]
        public Task NegativeCacheFamily()
        {
            RequireServer();
            return ExerciseAsync(Client.NegativeCache, "ncache");
        }

        [SkippableFact]
        public Task GuardCacheFamily()
        {
            RequireServer();
            return ExerciseAsync(Client.GuardCache, "guard");
        }

        [SkippableFact]
        public Task RerankCacheFamily()
        {
            RequireServer();
            return ExerciseAsync(Client.RerankCache, "rerank");
        }
    }
}
