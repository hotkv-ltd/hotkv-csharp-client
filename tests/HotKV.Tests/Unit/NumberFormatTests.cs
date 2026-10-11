// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using HotKV.AI;
using HotKV.Commands;
using HotKV.Connection;
using HotKV.Internal;
using Xunit;

namespace HotKV.Tests.Unit
{
    /// <summary>
    /// The floating point wire policy: one general formatter (plain decimal, shortest round-trip,
    /// never an exponent or '+', NaN/Inf rejected), a score formatter (Inf as inf/-inf) and a
    /// strict USD formatter (LLMBUDGET.SET / LLMSTATS.PRICE SET), checked directly and through
    /// every command family that formats a double, under non-invariant cultures.
    /// </summary>
    public class NumberFormatTests
    {
        public static readonly string[] Cultures = { "de-DE", "hu-HU", "fr-FR", "ar-SA" };

        public static IEnumerable<object[]> CultureData() => Cultures.Select(c => new object[] { c });

        /// <summary>Runs <paramref name="body"/> with CurrentCulture/CurrentUICulture switched, restoring them afterwards.</summary>
        private static async Task UnderCultureAsync(string name, Func<Task> body)
        {
            var oldC = CultureInfo.CurrentCulture;
            var oldU = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                CultureInfo.CurrentUICulture = new CultureInfo(name);
                await body();
            }
            finally
            {
                CultureInfo.CurrentCulture = oldC;
                CultureInfo.CurrentUICulture = oldU;
            }
        }

        private static async Task<IReadOnlyList<string>> CaptureAsync(Func<FakeCommandExecutor, Task> call)
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            try
            {
                await call(fake);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                // the canned +OK reply does not fit the typed result; only the sent tokens matter here.
            }
            return fake.LastArgs;
        }

        // ---------------------------------------------------------------- general formatter

        [Theory]
        [InlineData(1e-5, "0.00001")]
        [InlineData(1e-12, "0.000000000001")]
        [InlineData(-1.5e-7, "-0.00000015")]
        [InlineData(1.5e-5, "0.000015")]
        [InlineData(1e21, "1000000000000000000000")]
        [InlineData(1.25e21, "1250000000000000000000")]
        [InlineData(-3e22, "-30000000000000000000000")]
        [InlineData(1e15, "1000000000000000")]
        [InlineData(123456789012345680.0, "123456789012345680")]
        [InlineData(5.0, "5")]
        [InlineData(-5.0, "-5")]
        [InlineData(0.5, "0.5")]
        [InlineData(0.0, "0")]
        [InlineData(0.1, "0.1")]
        [InlineData(100.0, "100")]
        [InlineData(1234.5678, "1234.5678")]
        public void DoubleFormatsAsPlainShortestDecimal(double value, string expected)
        {
            Assert.Equal(expected, NumberFormat.Double(value));
        }

        [Fact]
        public void DoubleNegativeZeroIsNormalisedToZero()
        {
            Assert.Equal("0", NumberFormat.Double(-0.0));
            Assert.Equal("0", NumberFormat.Score(-0.0));
            Assert.Equal("0", NumberFormat.Single(-0.0f));
        }

        [Fact]
        public void DoubleHugeAndTinyExtremesHaveNoExponentOrPlus()
        {
            string max = NumberFormat.Double(double.MaxValue);
            Assert.DoesNotContain("E", max);
            Assert.DoesNotContain("e", max);
            Assert.DoesNotContain("+", max);
            Assert.Equal(309, max.Length);
            Assert.StartsWith("17976931348623157", max);
            Assert.Equal(double.MaxValue, double.Parse(max, CultureInfo.InvariantCulture));

            string eps = NumberFormat.Double(double.Epsilon);
            Assert.DoesNotContain("E", eps);
            Assert.StartsWith("0.", eps);
            Assert.EndsWith("5", eps);
            Assert.Equal(double.Epsilon, double.Parse(eps, CultureInfo.InvariantCulture));

            string e300 = NumberFormat.Double(1e300);
            Assert.Equal("1" + new string('0', 300), e300);
            string em300 = NumberFormat.Double(-1e-300);
            Assert.Equal("-0." + new string('0', 299) + "1", em300);
        }

        [Fact]
        public void DoubleRoundTripsExactlyForManyValues()
        {
            var rnd = new Random(12345);
            for (int i = 0; i < 20000; i++)
            {
                double v = BitConverter.Int64BitsToDouble((long)(rnd.NextDouble() * long.MaxValue) ^ (long)(rnd.Next() << 8));
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    continue;
                }
                string s = NumberFormat.Double(v);
                Assert.DoesNotContain("E", s);
                Assert.DoesNotContain("e", s);
                Assert.DoesNotContain("+", s);
                Assert.DoesNotContain(",", s);
                Assert.Equal(v == 0 ? 0.0 : v, double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture));
                // the digits are canonical: no trailing zero after a point, no bare trailing point.
                if (s.Contains('.'))
                {
                    Assert.False(s.EndsWith("0", StringComparison.Ordinal));
                    Assert.False(s.EndsWith(".", StringComparison.Ordinal));
                }
            }
        }

        [Fact]
        public void DoubleRejectsNaNAndInfinity()
        {
            Assert.Throws<ArgumentException>(() => NumberFormat.Double(double.NaN));
            Assert.Throws<ArgumentException>(() => NumberFormat.Double(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => NumberFormat.Double(double.NegativeInfinity));
            Assert.Throws<ArgumentException>(() => NumberFormat.Single(float.NaN));
            Assert.Throws<ArgumentException>(() => NumberFormat.Single(float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => NumberFormat.Single(float.NegativeInfinity));
            Assert.Throws<ArgumentException>(() => NumberFormat.Score(double.NaN));
        }

        [Theory]
        [InlineData(0.1f, "0.1")]
        [InlineData(1e-5f, "0.00001")]
        [InlineData(1e-10f, "0.0000000001")]
        [InlineData(1e21f, "1000000000000000000000")]
        [InlineData(-2.5e-7f, "-0.00000025")]
        [InlineData(3.0f, "3")]
        [InlineData(0.333333343f, "0.33333334")]
        public void SingleUsesFloat32ShortestDigits(float value, string expected)
        {
            Assert.Equal(expected, NumberFormat.Single(value));
            Assert.Equal(value, float.Parse(expected, CultureInfo.InvariantCulture));
        }

        [Fact]
        public void SingleIsShorterThanWidenedDouble()
        {
            // (double)0.1f is 0.10000000149011612; the float32 formatter sends the 3 character form.
            Assert.Equal("0.1", NumberFormat.Single(0.1f));
            Assert.NotEqual("0.1", NumberFormat.Double((double)0.1f));
        }

        // ---------------------------------------------------------------- score formatter

        [Fact]
        public void ScoreAcceptsInfinityLiterals()
        {
            Assert.Equal("inf", NumberFormat.Score(double.PositiveInfinity));
            Assert.Equal("-inf", NumberFormat.Score(double.NegativeInfinity));
            Assert.Equal("0.00001", NumberFormat.Score(1e-5));
            Assert.Equal("1000000000000000000000", NumberFormat.Score(1e21));
        }

        // ---------------------------------------------------------------- USD formatter

        [Theory]
        [InlineData(0.5, "0.5")]
        [InlineData(12.5, "12.5")]
        [InlineData(1e-5, "0.00001")]
        [InlineData(0.000001, "0.000001")]
        [InlineData(0.002, "0.002")]
        [InlineData(3.0, "3")]
        [InlineData(0.0, "0")]
        [InlineData(15.000001, "15.000001")]
        [InlineData(1e6, "1000000")]
        [InlineData(1e12, "1000000000000")]
        [InlineData(0.1 + 0.2, "0.3")]
        [InlineData(1234.5678, "1234.5678")]
        public void UsdFormatsAsFixedPointWithoutExponent(double value, string expected)
        {
            Assert.Equal(expected, NumberFormat.Usd(value));
        }

        [Fact]
        public void UsdRoundsToSixFractionalDigits()
        {
            Assert.Equal("0.000002", NumberFormat.Usd(0.0000015));
            Assert.Equal("1.234568", NumberFormat.Usd(1.2345678));
            Assert.Equal("0.000001", NumberFormat.Usd(0.0000009)); // rounds up to 1 micro, not to 0
            Assert.Equal("0.5", NumberFormat.Usd(0.5000001));
            Assert.Equal("1.5", NumberFormat.Usd(1.5m));
            Assert.Equal("7", NumberFormat.Usd(7m));
        }

        [Theory]
        [InlineData(1e-7)]
        [InlineData(1e-12)]
        [InlineData(4e-7)]
        [InlineData(double.Epsilon)]
        [InlineData(1e-300)]
        public void UsdRejectsPositiveAmountsThatRoundToZero(double value)
        {
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(value));
        }

        [Fact]
        public void UsdRejectsNegativeNaNInfinityAndTooLarge()
        {
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(-0.5));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(-1e-12));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(double.NaN));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(double.PositiveInfinity));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(double.NegativeInfinity));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(1e21));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(1e300));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(double.MaxValue));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(18446744073710.0)); // just above ulong.MaxValue micro-dollars
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(-1m));
            Assert.ThrowsAny<ArgumentException>(() => NumberFormat.Usd(decimal.MaxValue));
        }

        [Fact]
        public void UsdAcceptsTheLargestRepresentableAmount()
        {
            Assert.Equal("18446744073709.551615", NumberFormat.Usd(18446744073709.551615m));
        }

        [Fact]
        public void UsdOutputIsAlwaysAcceptedByTheStrictServerGrammar()
        {
            // digits, optional '.' plus 1..6 digits; no sign, no exponent, leading digit.
            var rx = new System.Text.RegularExpressions.Regex(@"^[0-9]+(\.[0-9]{1,6})?$");
            foreach (double v in new[] { 0.0, 1e-6, 1e-5, 0.1, 0.5, 1, 12.5, 99.999999, 1e3, 1e9, 123456.789012 })
            {
                Assert.Matches(rx, NumberFormat.Usd(v));
            }
        }

        // ---------------------------------------------------------------- RespWriter

        [Fact]
        public void RespWriterEncodesDoublesAndFloatsWithTheGeneralFormatter()
        {
            Assert.Equal("0.00001", System.Text.Encoding.ASCII.GetString(RespWriter.ToBytes(1e-5)));
            Assert.Equal("1000000000000000000000", System.Text.Encoding.ASCII.GetString(RespWriter.ToBytes(1e21)));
            Assert.Equal("0.1", System.Text.Encoding.ASCII.GetString(RespWriter.ToBytes(0.1f)));
            Assert.Throws<ArgumentException>(() => RespWriter.ToBytes(double.NaN));
            Assert.Throws<ArgumentException>(() => RespWriter.ToBytes(double.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => RespWriter.ToBytes(float.NaN));
        }

        [Fact]
        public void RespWriterValidateRejectsBadArgumentsBeforeAnyIo()
        {
            Assert.Throws<ArgumentException>(() => RespWriter.Validate(new object?[] { "SET", "k", double.NaN }));
            Assert.Throws<ArgumentException>(() => RespWriter.Validate(new object?[] { "SET", "k", float.PositiveInfinity }));
            Assert.Throws<ArgumentNullException>(() => RespWriter.Validate(new object?[] { "SET", null }));
            RespWriter.Validate(new object?[] { "SET", "k", 1.5, 2L, "x", new byte[] { 1 } });
        }

        [Fact]
        public void WriteCommandIsAtomicWhenAnArgumentCannotBeEncoded()
        {
            var buffer = new ByteBuffer();
            Assert.Throws<ArgumentException>(() => RespWriter.WriteCommand(buffer, new object?[] { "ZADD", "k", double.NaN, "m" }));
            Assert.Equal(0, buffer.Length);
        }

        [Fact]
        public async Task ClientStyleExecutorsRejectNaNAtTheCallSite()
        {
            var fake = new FakeCommandExecutor(RespValue.Integer(1));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => fake.SortedSetAddAsync("z", "m", double.NaN));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => fake.GeoAddAsync("g", double.NaN, 1.0, "m"));
        }

        // ---------------------------------------------------------------- per command family, under foreign cultures

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task CoreNumericCommandsWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "INCRBYFLOAT", "k", "0.00001" }, await CaptureAsync(f => f.IncrementByFloatAsync("k", 1e-5)));
            Assert.Equal(new[] { "INCRBYFLOAT", "k", "-1000000000000000000000" }, await CaptureAsync(f => f.IncrementByFloatAsync("k", -1e21)));
            Assert.Equal(new[] { "HINCRBYFLOAT", "h", "f", "1.5" }, await CaptureAsync(f => f.HashIncrementByFloatAsync("h", "f", 1.5)));
            Assert.Equal(new[] { "HINCRBYFLOAT", "h", "f", "0.000000000001" }, await CaptureAsync(f => f.HashIncrementByFloatAsync("h", "f", 1e-12)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => f.IncrementByFloatAsync("k", double.PositiveInfinity)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => f.HashIncrementByFloatAsync("h", "f", double.NaN)));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task SortedSetWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "ZADD", "z", "0.00001", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", 1e-5)));
            Assert.Equal(new[] { "ZADD", "z", "1000000000000000000000", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", 1e21)));
            Assert.Equal(new[] { "ZADD", "z", "-0.5", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", -0.5)));
            Assert.Equal(new[] { "ZADD", "z", "7", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", 7.0)));
            Assert.Equal(new[] { "ZADD", "z", "inf", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", double.PositiveInfinity)));
            Assert.Equal(new[] { "ZADD", "z", "-inf", "a" }, await CaptureAsync(f => f.SortedSetAddAsync("z", "a", double.NegativeInfinity)));
            Assert.Equal(new[] { "ZADD", "z", "INCR", "0." + new string('0', 299) + "1", "a" }, await CaptureAsync(f => f.SortedSetIncrementAsync("z", "a", 1e-300)));
            Assert.Equal(new[] { "ZINCRBY", "z", "0.00001", "a" }, await CaptureAsync(f => f.SortedSetIncrementByAsync("z", "a", 1e-5)));
            Assert.Equal(new[] { "ZINCRBY", "z", "-inf", "a" }, await CaptureAsync(f => f.SortedSetIncrementByAsync("z", "a", double.NegativeInfinity)));
            Assert.Equal(new[] { "ZUNIONSTORE", "d", "2", "a", "b", "WEIGHTS", "0.00001", "1000000000000000000000" }, await CaptureAsync(f => f.SortedSetUnionStoreAsync("d", new[] { "a", "b" }, new[] { 1e-5, 1e21 })));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => f.SortedSetAddAsync("z", "a", double.NaN)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => f.SortedSetIncrementByAsync("z", "a", double.NaN)));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task GeoWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "GEOADD", "g", "13.361389", "38.115556", "Palermo" }, await CaptureAsync(f => f.GeoAddAsync("g", 13.361389, 38.115556, "Palermo")));
            Assert.Equal(new[] { "GEOADD", "g", "0.00001", "-0.00002", "tiny" }, await CaptureAsync(f => f.GeoAddAsync("g", 1e-5, -2e-5, "tiny")));
            var o = new GeoSearchOptions { FromLonLat = (1e-5, 2.5), ByRadius = 1e21, Unit = GeoUnit.Kilometers };
            Assert.Equal(new[] { "GEOSEARCH", "g", "FROMLONLAT", "0.00001", "2.5", "BYRADIUS", "1000000000000000000000", "km" }, await CaptureAsync(f => f.GeoSearchAsync("g", o)));
            var box = new GeoSearchOptions { FromMember = "m", ByBox = (1.5, 1e-6) };
            Assert.Equal(new[] { "GEOSEARCH", "g", "FROMMEMBER", "m", "BYBOX", "1.5", "0.000001", "m" }, await CaptureAsync(f => f.GeoSearchAsync("g", box)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => f.GeoAddAsync("g", double.PositiveInfinity, 1.0, "m")));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task VsimWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "VSIM.SET", "i", "k", "0.00001", "-1000000000000000000000", "0.5", "1" },
                await CaptureAsync(f => new VsimCommands(f).SetAsync("i", "k", new[] { 1e-5, -1e21, 0.5, 1.0 })));
            Assert.Equal(new[] { "VSIM.SET", "i", "k", "0.1", "0.00001", "0.33333334", "1000000000000000000000" },
                await CaptureAsync(f => new VsimCommands(f).SetAsync("i", "k", new[] { 0.1f, 1e-5f, 1f / 3f, 1e21f })));
            Assert.Equal(new[] { "VSIM.SEARCH", "i", "3", "0.00001", "2", "EF", "64" },
                await CaptureAsync(f => new VsimCommands(f).SearchAsync("i", 3, new[] { 1e-5, 2.0 }, null, 64)));
            Assert.Equal(new[] { "VSIM.SEARCH", "i", "3", "0.1", "0.2" },
                await CaptureAsync(f => new VsimCommands(f).SearchAsync("i", 3, new[] { 0.1f, 0.2f })));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new VsimCommands(f).SetAsync("i", "k", new[] { 1.0, double.NaN })));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new VsimCommands(f).SetAsync("i", "k", new[] { 1f, float.PositiveInfinity })));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task SemanticAndPromptCacheWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "SCACHE.CREATE", "ns", "3", "METRIC", "cosine", "THRESHOLD", "0.00001" },
                await CaptureAsync(f => new SemanticCache(f).CreateNamespaceAsync("ns", 3, VectorMetric.Cosine, 1e-5)));
            Assert.Equal(new[] { "SCACHE.SET", "ns", "q", "r", "0.00001", "1000000000000000000000" },
                await CaptureAsync(f => new SemanticCache(f).SetAsync("ns", "q", "r", new[] { 1e-5, 1e21 })));
            Assert.Equal(new[] { "SCACHE.SET", "ns", "q", "r", "0.1", "0.00001" },
                await CaptureAsync(f => new SemanticCache(f).SetAsync("ns", "q", "r", new[] { 0.1f, 1e-5f })));
            Assert.Equal(new[] { "SCACHE.GET", "ns", "0.00001", "1", "THRESHOLD", "0.000001" },
                await CaptureAsync(f => new SemanticCache(f).GetAsync("ns", new[] { 1e-5, 1.0 }, 1e-6)));
            Assert.Equal(new[] { "SCACHE.GET", "ns", "0.25", "0.5" },
                await CaptureAsync(f => new SemanticCache(f).GetAsync("ns", new[] { 0.25f, 0.5f })));
            Assert.Equal(new[] { "SCACHE.THRESHOLD", "ns", "0.00001" },
                await CaptureAsync(f => new SemanticCache(f).SetThresholdAsync("ns", 1e-5)));
            Assert.Equal(new[] { "PCACHE.CREATE", "ns", "COST_PER_HIT", "0.00001" },
                await CaptureAsync(f => new PromptCache(f).CreateNamespaceAsync("ns", costPerHit: 1e-5)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new PromptCache(f).CreateNamespaceAsync("ns", costPerHit: double.NaN)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new SemanticCache(f).SetThresholdAsync("ns", double.PositiveInfinity)));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task RateLimitTimeSeriesAndRagWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "RATELIMIT.CREATE", "rl", "10", "1000", "ALGO", "token_bucket", "REFILL", "0.00001", "DRAIN", "1000000000000000000000" },
                await CaptureAsync(f => new RateLimitCommands(f).CreateAsync("rl", 10, 1000, RateLimitAlgorithm.TokenBucket, 1e-5, 1e21)));
            Assert.Equal(new[] { "TS.ADD", "ts", "1000", "0.000001" },
                await CaptureAsync(f => new TimeSeriesCommands(f).AddAsync("ts", 1000L, 1e-6)));
            Assert.Equal(new[] { "TS.ADD", "ts", "*", "-1000000000000000000000" },
                await CaptureAsync(f => new TimeSeriesCommands(f).AddNowAsync("ts", -1e21)));
            Assert.Equal(new[] { "RAG.SEARCH", "p", "q", "K", "3", "ALPHA", "0.00001", "VECTOR", "0.00001,1000000000000000000000,0.5" },
                await CaptureAsync(f => new RagCommands(f).SearchAsync("p", "q", 3, 1e-5, new[] { 1e-5, 1e21, 0.5 })));
            Assert.Equal(new[] { "RAG.SEARCH", "p", "q", "VECTOR", "0.1,0.00001" },
                await CaptureAsync(f => new RagCommands(f).SearchAsync("p", "q", null, null, new[] { 0.1f, 1e-5f })));
            Assert.Equal(new[] { "RAG.INGEST", "p", "d", "t", "VECTORS", "0.00001,2", "0.5,0.000000000001" },
                await CaptureAsync(f => new RagCommands(f).IngestAsync("p", "d", "t", null, new IReadOnlyList<double>[] { new[] { 1e-5, 2.0 }, new[] { 0.5, 1e-12 } })));
            Assert.Equal(new[] { "RAG.INGEST", "p", "d", "t", "VECTORS", "0.1,0.00001" },
                await CaptureAsync(f => new RagCommands(f).IngestAsync("p", "d", "t", null, new IReadOnlyList<float>[] { new[] { 0.1f, 1e-5f } })));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new RateLimitCommands(f).CreateAsync("rl", 1, 1, RateLimitAlgorithm.TokenBucket, double.NaN)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new TimeSeriesCommands(f).AddNowAsync("ts", double.PositiveInfinity)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new RagCommands(f).SearchAsync("p", "q", null, null, new[] { 1.0, double.NaN })));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task AgentMemoryVectorsWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            Assert.Equal(new[] { "AMEM.SEARCH", "a", "2", "0.00001", "1000000000000000000000" },
                await CaptureAsync(f => new AgentMemoryCommands(f).SearchAsync("a", 2, new[] { 1e-5, 1e21 })));
            Assert.Equal(new[] { "AMEM.SEARCH", "a", "2", "0.1", "0.2" },
                await CaptureAsync(f => new AgentMemoryCommands(f).SearchAsync("a", 2, new[] { 0.1f, 0.2f })));
            Assert.Equal(new[] { "AMEM.SEM.STORE", "a", "k", "c", "0.00001", "-2" },
                await CaptureAsync(f => new AgentMemoryCommands(f).Semantic.StoreAsync("a", "k", "c", new[] { 1e-5, -2.0 })));
            Assert.Equal(new[] { "AMEM.SEM.STORE", "a", "k", "c", "0.1" },
                await CaptureAsync(f => new AgentMemoryCommands(f).Semantic.StoreAsync("a", "k", "c", new[] { 0.1f })));
            Assert.Equal(new[] { "AMEM.SEM.RECALL", "a", "5", "0.00001" },
                await CaptureAsync(f => new AgentMemoryCommands(f).Semantic.RecallAsync("a", 5, new[] { 1e-5 })));
            Assert.Equal(new[] { "AMEM.SEM.RECALL", "a", "5", "0.25" },
                await CaptureAsync(f => new AgentMemoryCommands(f).Semantic.RecallAsync("a", 5, new[] { 0.25f })));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new AgentMemoryCommands(f).Semantic.RecallAsync("a", 5, new[] { double.NaN })));
        });

        [Theory]
        [MemberData(nameof(CultureData))]
        public Task LlmBudgetAndStatsMoneyWire(string culture) => UnderCultureAsync(culture, async () =>
        {
            // LLMBUDGET.SET and LLMSTATS.PRICE SET use the strict fixed-point USD rule.
            Assert.Equal(new[] { "LLMBUDGET.SET", "b", "1000", "0.00001", "60" },
                await CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1000, 1e-5, 60)));
            Assert.Equal(new[] { "LLMBUDGET.SET", "b", "1000", "12.5", "60" },
                await CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1000, 12.5, 60)));
            Assert.Equal(new[] { "LLMBUDGET.SET", "b", "1000", "0", "60" },
                await CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1000, 0.0, 60)));
            Assert.Equal(new[] { "LLMSTATS.PRICE", "SET", "m", "INPUT", "0.00001", "OUTPUT", "15", "CACHEREAD", "0.000001" },
                await CaptureAsync(f => new LlmStatsCommands(f).PriceSetAsync("m", 1e-5, 15, 1e-6)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1, 1e-12, 60)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1, -1.0, 60)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1, double.NaN, 60)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1, double.PositiveInfinity, 60)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).SetAsync("b", 1, 1e300, 60)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmStatsCommands(f).PriceSetAsync("m", 1e-9, 15)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmStatsCommands(f).PriceSetAsync("m", 3, -15)));

            // LLMBUDGET.CONSUME and LLMSTATS.RECORD cost_usd use the general rule (from_chars): plain decimal, '-' allowed by the parser.
            Assert.Equal(new[] { "LLMBUDGET.CONSUME", "b", "10", "0.00001" },
                await CaptureAsync(f => new LlmBudgetCommands(f).ConsumeAsync("b", 10, 1e-5)));
            Assert.Equal(new[] { "LLMBUDGET.CONSUME", "b", "10", "1000000000000000000000" },
                await CaptureAsync(f => new LlmBudgetCommands(f).ConsumeAsync("b", 10, 1e21)));
            Assert.Equal(new[] { "LLMSTATS.RECORD", "bk", "1", "2", "3", "0.000000000001", "4" },
                await CaptureAsync(f => new LlmStatsCommands(f).RecordAsync("bk", 1, 2, 3, 1e-12, 4)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmBudgetCommands(f).ConsumeAsync("b", 10, double.NaN)));
            await Assert.ThrowsAnyAsync<ArgumentException>(() => CaptureAsync(f => new LlmStatsCommands(f).RecordAsync("bk", 1, 2, 3, double.PositiveInfinity, 4)));
        });

        [Fact]
        public void NoCommandFamilySourceFormatsDoublesWithAnImplicitCulture()
        {
            // The formatters never consult CurrentCulture: same output under every test culture.
            foreach (string c in Cultures)
            {
                var old = CultureInfo.CurrentCulture;
                try
                {
                    CultureInfo.CurrentCulture = new CultureInfo(c);
                    Assert.Equal("1234.5678", NumberFormat.Double(1234.5678));
                    Assert.Equal("1234.5678", NumberFormat.Score(1234.5678));
                    Assert.Equal("1234.5678", NumberFormat.Usd(1234.5678));
                    Assert.Equal("1234.5", NumberFormat.Single(1234.5f));
                }
                finally
                {
                    CultureInfo.CurrentCulture = old;
                }
            }
        }
    }
}
