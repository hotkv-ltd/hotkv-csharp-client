// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Linq;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class SetCommandsTests : IntegrationTestBase
    {
        public SetCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task AddRemoveMembersCard()
        {
            RequireServer();
            string key = UniqueKey("set-basic");
            try
            {
                Assert.Equal(2, await Client.SetAddAsync(key, new[] { "a", "b" }));
                Assert.Equal(0, await Client.SetAddAsync(key, "a"));
                Assert.Equal(2, await Client.SetCardinalityAsync(key));
                Assert.True(await Client.SetIsMemberAsync(key, "a"));
                Assert.False(await Client.SetIsMemberAsync(key, "z"));
                var members = await Client.SetMembersAsync(key);
                Assert.Equal(new[] { "a", "b" }, members.OrderBy(x => x));
                Assert.Equal(1, await Client.SetRemoveAsync(key, new[] { "a" }));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task MultiIsMember()
        {
            RequireServer();
            string key = UniqueKey("set-mismember");
            try
            {
                await Client.SetAddAsync(key, new[] { "a", "b" });
                var result = await Client.SetMultiIsMemberAsync(key, new[] { "a", "z", "b" });
                Assert.Equal(new[] { true, false, true }, result);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task PopAndRandomMember()
        {
            RequireServer();
            string key = UniqueKey("set-pop");
            try
            {
                await Client.SetAddAsync(key, new[] { "a", "b", "c" });
                var popped = await Client.SetPopAsync(key, 2);
                Assert.Equal(2, popped.Count);
                Assert.Equal(1, await Client.SetCardinalityAsync(key));

                var random = await Client.SetRandomMemberAsync(key, -5);
                Assert.Equal(5, random.Count);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task MoveBetweenSets()
        {
            RequireServer();
            string source = UniqueKey("set-move-src");
            string destination = UniqueKey("set-move-dst");
            try
            {
                await Client.SetAddAsync(source, new[] { "a", "b" });
                Assert.True(await Client.SetMoveAsync(source, destination, "a"));
                Assert.True(await Client.SetIsMemberAsync(destination, "a"));
                Assert.False(await Client.SetIsMemberAsync(source, "a"));
            }
            finally
            {
                await Client.DeleteAsync(new[] { source, destination });
            }
        }

        [SkippableFact]
        public async Task InterUnionDiffAndStores()
        {
            RequireServer();
            string a = UniqueKey("set-a");
            string b = UniqueKey("set-b");
            string dest = UniqueKey("set-dest");
            try
            {
                await Client.SetAddAsync(a, new[] { "1", "2", "3" });
                await Client.SetAddAsync(b, new[] { "2", "3", "4" });

                Assert.Equal(new[] { "2", "3" }, (await Client.SetIntersectAsync(new[] { a, b })).OrderBy(x => x));
                Assert.Equal(new[] { "1", "2", "3", "4" }, (await Client.SetUnionAsync(new[] { a, b })).OrderBy(x => x));
                Assert.Equal(new[] { "1" }, await Client.SetDifferenceAsync(new[] { a, b }));

                Assert.Equal(2, await Client.SetIntersectStoreAsync(dest, new[] { a, b }));
                Assert.Equal(4, await Client.SetUnionStoreAsync(dest, new[] { a, b }));
                Assert.Equal(1, await Client.SetDifferenceStoreAsync(dest, new[] { a, b }));
                Assert.Equal(2, await Client.SetIntersectCardinalityAsync(new[] { a, b }));
                Assert.Equal(1, await Client.SetIntersectCardinalityAsync(new[] { a, b }, limit: 1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b, dest });
            }
        }
    }
}
