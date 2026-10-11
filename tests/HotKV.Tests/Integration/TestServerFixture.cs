// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Connects once to the server named by the HOTKV_TEST_URL environment
    /// variable (e.g. "hotkv://127.0.0.1:6379") and shares that connection
    /// across every integration test class. When the variable is unset,
    /// <see cref="ServerAvailable"/> is false and every integration test
    /// calls <see cref="IntegrationTestBase.RequireServer"/> to skip itself.
    /// </summary>
    public sealed class TestServerFixture : IAsyncLifetime
    {
        public bool ServerAvailable { get; private set; }
        public HotKVClient? Client { get; private set; }
        public string? Url { get; private set; }

        public async Task InitializeAsync()
        {
            Url = Environment.GetEnvironmentVariable("HOTKV_TEST_URL");
            if (string.IsNullOrWhiteSpace(Url))
            {
                ServerAvailable = false;
                return;
            }
            Client = await HotKVClient.ConnectAsync(Url);
            ServerAvailable = true;
        }

        public async Task DisposeAsync()
        {
            if (Client is not null)
            {
                await Client.DisposeAsync();
            }
        }
    }

    [CollectionDefinition("HotKV Server")]
    public sealed class TestServerCollection : ICollectionFixture<TestServerFixture>
    {
    }
}
