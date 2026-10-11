// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    public class BitmapCommandsTests : IntegrationTestBase
    {
        public BitmapCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task SetGetBitAndCount()
        {
            RequireServer();
            string key = UniqueKey("bit-basic");
            try
            {
                Assert.False(await Client.SetBitAsync(key, 7, true));
                Assert.True(await Client.GetBitAsync(key, 7));
                Assert.Equal(1, await Client.BitCountAsync(key));
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task BitPosFindsFirstSetBit()
        {
            RequireServer();
            string key = UniqueKey("bit-pos");
            try
            {
                await Client.SetBitAsync(key, 10, true);
                long pos = await Client.BitPositionAsync(key, true);
                Assert.Equal(10, pos);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task BitOpAndCombinesTwoKeys()
        {
            RequireServer();
            string a = UniqueKey("bit-a");
            string b = UniqueKey("bit-b");
            string dest = UniqueKey("bit-dest");
            try
            {
                await Client.SetBitAsync(a, 1, true);
                await Client.SetBitAsync(a, 2, true);
                await Client.SetBitAsync(b, 2, true);
                await Client.BitOpAsync(BitOp.And, dest, new[] { a, b });
                Assert.True(await Client.GetBitAsync(dest, 2));
                Assert.False(await Client.GetBitAsync(dest, 1));
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b, dest });
            }
        }

        [SkippableFact]
        public async Task BitFieldGetSetIncrBy()
        {
            RequireServer();
            string key = UniqueKey("bit-field");
            try
            {
                var result = await Client.BitFieldAsync(key, new object[] { "SET", "u8", "0", 42, "GET", "u8", "0" });
                Assert.Equal(2, result.Count);
                Assert.Equal(42, result[1]);

                var incr = await Client.BitFieldAsync(key, new object[] { "INCRBY", "u8", "0", 1 });
                Assert.Equal(43, incr[0]);
            }
            finally
            {
                await Client.DeleteAsync(key);
            }
        }
    }

    public class HyperLogLogCommandsTests : IntegrationTestBase
    {
        public HyperLogLogCommandsTests(TestServerFixture fixture) : base(fixture)
        {
        }

        [SkippableFact]
        public async Task AddCountMerge()
        {
            RequireServer();
            string a = UniqueKey("hll-a");
            string b = UniqueKey("hll-b");
            string dest = UniqueKey("hll-dest");
            try
            {
                Assert.True(await Client.HyperLogLogAddAsync(a, new[] { "x", "y", "z" }));
                Assert.False(await Client.HyperLogLogAddAsync(a, new[] { "x" }));
                long count = await Client.HyperLogLogCountAsync(new[] { a });
                Assert.InRange(count, 2, 4);

                await Client.HyperLogLogAddAsync(b, new[] { "z", "w" });
                await Client.HyperLogLogMergeAsync(dest, new[] { a, b });
                long merged = await Client.HyperLogLogCountAsync(new[] { dest });
                Assert.InRange(merged, 3, 5);
            }
            finally
            {
                await Client.DeleteAsync(new[] { a, b, dest });
            }
        }
    }
}
