// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class PromptRegistryTests : IntegrationTestBase
    {
        public PromptRegistryTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task PutGetVersionsSetActive()
        {
            RequireServer();
            string name = UniqueKey("prompt-tpl");
            await SkipIfNotLicensedAsync(async () =>
            {
                long v1 = await Client.PromptRegistry.PutAsync(name, "Hello {{name}}");
                Assert.Equal(1, v1);
                long v2 = await Client.PromptRegistry.PutAsync(name, "Hi {{name}}!");
                Assert.Equal(2, v2);

                var latest = await Client.PromptRegistry.GetLatestAsync(name);
                Assert.Equal("Hi {{name}}!", latest!.Content);

                var v1Content = await Client.PromptRegistry.GetAsync(name, 1);
                Assert.Equal("Hello {{name}}", v1Content!.Content);

                var versions = await Client.PromptRegistry.VersionsAsync(name);
                Assert.Contains(1L, versions);
                Assert.Contains(2L, versions);

                await Client.PromptRegistry.SetActiveAsync(name, 1);
                Assert.True(await Client.PromptRegistry.DropAsync(name));
            });
        }
    }

    public class EmbedDedupTests : IntegrationTestBase
    {
        public EmbedDedupTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task PutGetReleaseStats()
        {
            RequireServer();
            string ns = UniqueKey("embeddedup");
            await SkipIfNotLicensedAsync(async () =>
            {
                bool created = await Client.EmbedDedup.PutAsync(ns, "hash1", "text-embedding-3", "0.1,0.2,0.3");
                Assert.True(created);
                bool createdAgain = await Client.EmbedDedup.PutAsync(ns, "hash1", "text-embedding-3", "0.1,0.2,0.3");
                Assert.False(createdAgain);

                Assert.Equal("0.1,0.2,0.3", await Client.EmbedDedup.GetAsync(ns, "hash1"));

                var stats = await Client.EmbedDedup.StatsAsync(ns);
                Assert.True(stats.Entries >= 1);

                await Client.EmbedDedup.ReleaseAsync(ns, "hash1");
                await Client.EmbedDedup.ReleaseAsync(ns, "hash1");
                await Client.EmbedDedup.PurgeAsync(ns);
                await Client.EmbedDedup.DropAsync(ns);
            });
        }
    }
}
