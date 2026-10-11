// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Cluster;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// The shared test server is a single, standalone node (not an actual
    /// cluster), so these tests exercise <see cref="HotKVCluster"/>'s
    /// topology discovery and graceful-failure paths against a real
    /// connection rather than genuine cross-node redirects; the hashing and
    /// redirect-following logic itself is covered exhaustively by the unit
    /// tests (Crc16Tests, ExceptionMappingTests).
    /// </summary>
    public class ClusterTests : IntegrationTestBase
    {
        public ClusterTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task ConnectDiscoversAnEmptyTopologyOnANonClusterNode()
        {
            RequireServer();
            await using var cluster = await HotKVCluster.ConnectAsync(Fixture.Url!);

            // No slot is owned by anything, so routing a real command fails
            // clearly rather than hanging or guessing a node.
            var ex = await Assert.ThrowsAsync<HotKVException>(() => cluster.PingAsync());
            Assert.Contains("slot", ex.Message);
        }

        [SkippableFact]
        public async Task RefreshSlotsCanBeCalledExplicitly()
        {
            RequireServer();
            await using var cluster = await HotKVCluster.ConnectAsync(Fixture.Url!);
            await cluster.RefreshSlotsAsync();
        }
    }
}
