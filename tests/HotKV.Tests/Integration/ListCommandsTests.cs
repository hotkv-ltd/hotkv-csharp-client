// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class ListCommandsTests : IntegrationTestBase
    {
        public ListCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task PushPopAndRange()
        {
            RequireServer();
            string key = UniqueKey("list-basic");
            try
            {
                Assert.Equal(1, await Client.ListRightPushAsync(key, "a"));
                Assert.Equal(2, await Client.ListRightPushAsync(key, "b"));
                Assert.Equal(3, await Client.ListLeftPushAsync(key, "z"));
                Assert.Equal(3, await Client.ListLengthAsync(key));
                var range = await Client.ListRangeAsync(key, 0, -1);
                Assert.Equal(new[] { "z", "a", "b" }, range);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task PopWithCount()
        {
            RequireServer();
            string key = UniqueKey("list-popcount");
            try
            {
                await Client.ListRightPushAsync(key, new[] { "a", "b", "c", "d" });
                var popped = await Client.ListLeftPopAsync(key, 2);
                Assert.Equal(new[] { "a", "b" }, popped);
                var single = await Client.ListRightPopAsync(key);
                Assert.Equal(new[] { "d" }, single);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task PopOfMissingKeyReturnsNull()
        {
            RequireServer();
            Assert.Null(await Client.ListLeftPopAsync(UniqueKey("list-missing")));
        }

        [SkippableFact]
        public async Task IndexSetInsertRemoveTrim()
        {
            RequireServer();
            string key = UniqueKey("list-manip");
            try
            {
                await Client.ListRightPushAsync(key, new[] { "a", "b", "c" });
                Assert.Equal("b", await Client.ListIndexAsync(key, 1));
                await Client.ListSetAsync(key, 1, "B");
                Assert.Equal("B", await Client.ListIndexAsync(key, 1));

                await Client.ListInsertAsync(key, before: true, pivot: "B", value: "A.5");
                Assert.Equal(new[] { "a", "A.5", "B", "c" }, await Client.ListRangeAsync(key, 0, -1));

                await Client.ListRightPushAsync(key, "c");
                Assert.Equal(2, await Client.ListRemoveAsync(key, 0, "c"));

                await Client.ListTrimAsync(key, 0, 1);
                Assert.Equal(new[] { "a", "A.5" }, await Client.ListRangeAsync(key, 0, -1));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task LPosFindsElements()
        {
            RequireServer();
            string key = UniqueKey("list-lpos");
            try
            {
                await Client.ListRightPushAsync(key, new[] { "a", "b", "c", "b" });
                var all = await Client.ListPositionAsync(key, "b", count: 0);
                Assert.Equal(new long[] { 1, 3 }, all);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task LMoveMovesBetweenLists()
        {
            RequireServer();
            string source = UniqueKey("list-move-src");
            string destination = UniqueKey("list-move-dst");
            try
            {
                await Client.ListRightPushAsync(source, new[] { "a", "b" });
                string? moved = await Client.ListMoveAsync(source, destination, fromLeft: true, toLeft: true);
                Assert.Equal("a", moved);
                Assert.Equal(new[] { "a" }, await Client.ListRangeAsync(destination, 0, -1));
                Assert.Equal(new[] { "b" }, await Client.ListRangeAsync(source, 0, -1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { source, destination });
            }
        }

        [SkippableFact]
        public async Task PushXOnMissingKeyIsNoop()
        {
            RequireServer();
            string key = UniqueKey("list-pushx-missing");
            Assert.Equal(0, await Client.ListLeftPushIfExistsAsync(key, new[] { "a" }));
            Assert.False(await Client.ExistsAsync(key));
        }
    }
}
