// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class RagTests : IntegrationTestBase
    {
        public RagTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task CreateIngestSearchStats()
        {
            RequireServer();
            string pipeline = UniqueKey("rag");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.Rag.CreateAsync(pipeline, dimensions: 3, strategy: RagChunkStrategy.Fixed, chunkSize: 100, chunkOverlap: 10);

                    long chunks = await Client.Rag.IngestAsync(
                        pipeline,
                        "doc1",
                        "HotKV is a fast in-memory data store.",
                        meta: new Dictionary<string, string> { ["source"] = "docs" },
                        vectors: new List<IReadOnlyList<double>> { new[] { 1.0, 0.0, 0.0 } });
                    Assert.True(chunks >= 1);

                    var results = await Client.Rag.SearchAsync(pipeline, "fast data store", k: 3, alpha: 0.5, vector: new[] { 1.0, 0.0, 0.0 });
                    Assert.NotEmpty(results);
                    Assert.Equal("doc1", results[0].DocId);

                    var stats = await Client.Rag.StatsAsync(pipeline);
                    Assert.Equal(pipeline, stats.Name);
                    Assert.True(stats.TotalDocs >= 1);

                    var list = await Client.Rag.ListAsync();
                    Assert.Contains(pipeline, list);
                }
                finally
                {
                    await Client.Rag.DropAsync(pipeline);
                }
            });
        }

        [SkippableFact]
        public async Task SearchWithFilterAfterVector()
        {
            RequireServer();
            string pipeline = UniqueKey("rag-filter");
            await SkipIfNotLicensedAsync(async () =>
            {
                try
                {
                    await Client.Rag.CreateAsync(pipeline, dimensions: 2);
                    await Client.Rag.IngestAsync(pipeline, "doc1", "some text about cats",
                        meta: new Dictionary<string, string> { ["category"] = "animals" },
                        vectors: new List<IReadOnlyList<double>> { new[] { 1.0, 0.0 } });

                    var results = await Client.Rag.SearchAsync(pipeline, "cats", vector: new[] { 1.0, 0.0 },
                        filter: new Dictionary<string, string> { ["category"] = "animals" });
                    Assert.NotEmpty(results);
                }
                finally
                {
                    await Client.Rag.DropAsync(pipeline);
                }
            });
        }
    }
}
