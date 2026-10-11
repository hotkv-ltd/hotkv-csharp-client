// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class ObserveAndAlertsTests : IntegrationTestBase
    {
        public ObserveAndAlertsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task ObserveInfoAndMetrics()
        {
            RequireServer();
            await SkipIfNotLicensedAsync(async () =>
            {
                var info = await Client.Observe.InfoAsync();
                Assert.True(info.UptimeSeconds >= 0);

                var metrics = await Client.Observe.MetricsAsync();
                Assert.True(metrics.TotalCommands >= 0);
            });
        }

        [SkippableFact]
        public async Task AlertsInfoReportsConfiguration()
        {
            RequireServer();
            await SkipIfNotLicensedAsync(async () =>
            {
                var info = await Client.Observe.AlertsInfoAsync();
                Assert.False(string.IsNullOrEmpty(info.SupportedProviders));
            });
        }
    }
}
