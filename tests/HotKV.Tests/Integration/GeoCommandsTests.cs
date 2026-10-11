// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class GeoCommandsTests : IntegrationTestBase
    {
        public GeoCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task AddPosDistHash()
        {
            RequireServer();
            string key = UniqueKey("geo-basic");
            try
            {
                Assert.Equal(2, await Client.GeoAddAsync(key, new (double, double, string)[]
                {
                    (13.361389, 38.115556, "Palermo"),
                    (15.087269, 37.502669, "Catania"),
                }));

                var positions = await Client.GeoPositionAsync(key, new[] { "Palermo", "missing" });
                Assert.NotNull(positions[0]);
                Assert.Null(positions[1]);
                Assert.Equal(13.361389, positions[0]!.Value.Longitude, 3);

                double? dist = await Client.GeoDistanceAsync(key, "Palermo", "Catania", GeoUnit.Kilometers);
                Assert.NotNull(dist);
                Assert.InRange(dist!.Value, 100, 200);

                var hashes = await Client.GeoHashAsync(key, new[] { "Palermo" });
                Assert.NotNull(hashes[0]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SearchByRadiusWithExtras()
        {
            RequireServer();
            string key = UniqueKey("geo-search");
            try
            {
                await Client.GeoAddAsync(key, 13.361389, 38.115556, "Palermo");
                await Client.GeoAddAsync(key, 15.087269, 37.502669, "Catania");

                var results = await Client.GeoSearchAsync(key, new GeoSearchOptions
                {
                    FromLonLat = (15, 37),
                    ByRadius = 200,
                    Unit = GeoUnit.Kilometers,
                    WithCoord = true,
                    WithDist = true,
                    Ascending = true,
                });

                Assert.NotEmpty(results);
                Assert.All(results, r => Assert.NotNull(r.DistanceUnits));
                Assert.All(results, r => Assert.NotNull(r.Coordinate));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task SearchByBox()
        {
            RequireServer();
            string key = UniqueKey("geo-box");
            try
            {
                await Client.GeoAddAsync(key, 13.361389, 38.115556, "Palermo");
                var results = await Client.GeoSearchAsync(key, new GeoSearchOptions
                {
                    FromMember = "Palermo",
                    ByBox = (400, 400),
                    Unit = GeoUnit.Kilometers,
                });
                Assert.Contains(results, r => r.Member == "Palermo");
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }
}
