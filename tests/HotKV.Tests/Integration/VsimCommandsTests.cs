// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class VsimCommandsTests : IntegrationTestBase
    {
        public VsimCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateSetGetSearchDrop()
        {
            RequireServer();
            string index = UniqueKey("vsim-idx");
            try
            {
                await Client.Vsim.CreateIndexAsync(index, 3, VectorMetric.Cosine);

                Assert.True(await Client.Vsim.SetAsync(index, "v1", new[] { 1.0, 0.0, 0.0 }));
                Assert.False(await Client.Vsim.SetAsync(index, "v1", new[] { 1.0, 0.1, 0.0 }));
                Assert.True(await Client.Vsim.SetAsync(index, "v2", new[] { 0.0, 1.0, 0.0 }));

                var stored = await Client.Vsim.GetAsync(index, "v1");
                Assert.NotNull(stored);
                Assert.Equal(3, stored!.Count);

                var results = await Client.Vsim.SearchAsync(index, 2, new[] { 1.0, 0.0, 0.0 });
                Assert.NotEmpty(results);
                Assert.Equal("v1", results[0].Key);

                var info = await Client.Vsim.InfoAsync(index);
                Assert.Equal(3, info.Dimensions);
                Assert.Equal("cosine", info.Metric);

                var indexes = await Client.Vsim.ListIndexesAsync();
                Assert.Contains(index, indexes);

                Assert.True(await Client.Vsim.DeleteAsync(index, "v2"));
                Assert.False(await Client.Vsim.DeleteAsync(index, "v2"));
            }
            finally
            {
                await Client.Vsim.DropIndexAsync(index);
            }
        }

        [SkippableFact]
        public async Task SearchWithFilterUsesTagIntersection()
        {
            RequireServer();
            string index = UniqueKey("vsim-filter-idx");
            try
            {
                await Client.Vsim.CreateIndexAsync(index, 2);
                await Client.Vsim.SetAsync(index, "a", new[] { 1.0, 0.0 });
                await Client.Vsim.SetAsync(index, "b", new[] { 1.0, 0.0 });
                // TAG.SET requires the key to already exist in the regular keyspace.
                await Client.StringSetAsync("a", "placeholder");
                await Client.StringSetAsync("b", "placeholder");
                await Client.Tags.SetAsync("a", "group1");
                await Client.Tags.SetAsync("b", "group2");

                var filtered = await Client.Vsim.SearchAsync(index, 5, new[] { 1.0, 0.0 }, filterTags: new[] { "group1" });
                Assert.Single(filtered);
                Assert.Equal("a", filtered[0].Key);
            }
            finally
            {
                await Client.Vsim.DropIndexAsync(index);
                await Client.DeleteAsync(new[] { "a", "b" });
            }
        }

        [SkippableFact]
        public async Task SnapshotSaveLoadListDrop()
        {
            RequireServer();
            // Snapshot names are file names: no dot, colon or path separators.
            string name = "csharp-snap-" + System.Guid.NewGuid().ToString("N");
            await SkipIfNotLicensedAsync(async () =>
            {
                var blob = new byte[] { 1, 2, 3, 0, 255, 13, 10 };
                try
                {
                    Assert.Equal(12 + blob.Length, await Client.Vsim.SaveSnapshotAsync(name, blob));
                    Assert.Equal(blob, await Client.Vsim.LoadSnapshotAsync(name));
                    Assert.Contains(name, await Client.Vsim.ListSnapshotsAsync());
                    Assert.True(await Client.Vsim.DropSnapshotAsync(name));
                    Assert.False(await Client.Vsim.DropSnapshotAsync(name));
                    Assert.DoesNotContain(name, await Client.Vsim.ListSnapshotsAsync());
                }
                finally
                {
                    await Client.Vsim.DropSnapshotAsync(name);
                }
            });
        }
    }
}
