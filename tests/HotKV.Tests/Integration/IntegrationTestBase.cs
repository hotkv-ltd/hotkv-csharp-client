// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using HotKV;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Shared plumbing for every integration test class: the shared
    /// connected client, a unique key/namespace helper (so tests never
    /// collide and clean up after themselves), and skip helpers for an
    /// absent server or a missing license feature.
    /// </summary>
    [Collection("HotKV Server")]
    public abstract class IntegrationTestBase
    {
        private readonly TestServerFixture _fixture;

        protected IntegrationTestBase(TestServerFixture fixture)
        {
            _fixture = fixture;
        }

        protected TestServerFixture Fixture => _fixture;

        protected HotKVClient Client => _fixture.Client!;

        /// <summary>Call first in every test: skips it when HOTKV_TEST_URL was not set.</summary>
        protected void RequireServer() =>
            Skip.IfNot(_fixture.ServerAvailable, "HOTKV_TEST_URL is not set; integration tests are skipped");

        /// <summary>A key/namespace/session id unique to this test run, so concurrent test classes never collide and cleanup is trivial.</summary>
        protected static string UniqueKey(string label) => $"hkvtest:{label}:{Guid.NewGuid():N}";

        /// <summary>
        /// Runs an integration test body, converting a <see cref="NotLicensedException"/>
        /// into a skipped test with a clear reason instead of a failure: the
        /// test then passes automatically once a license that covers the
        /// feature is loaded on the server.
        /// </summary>
        protected static async Task SkipIfNotLicensedAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (NotLicensedException ex)
            {
                throw new SkipException($"server license does not cover this feature yet: {ex.RawMessage}");
            }
        }
    }
}
