// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using HotKV.AI;
using HotKV.Connection;
using Xunit;

namespace HotKV.Tests.Unit
{
    /// <summary>
    /// Unit tests (no HotKV server) for the token usage additions: exact wire argument lists
    /// for the shared token usage group and every new option, built directly and via a
    /// <see cref="FakeCommandExecutor"/> that captures the real wire tokens the typed commands
    /// send, plus by-field-name parsing of canned RESP2 flat arrays and RESP3 maps for
    /// LLMSTATS.PRICE and the other changed STATS/GET/STATUS replies.
    /// </summary>
    public class EnterpriseUsageTests
    {
        // --- Usage: exact wire options for every combination ---

        [Fact]
        public void UsageAppendsModelOnlyWhenNoCountsGiven()
        {
            var args = new List<object?>();
            new Usage("gpt-4").AppendTo(args);
            Assert.Equal(new object?[] { "MODEL", "gpt-4" }, args);
        }

        [Fact]
        public void UsageAppendsAllFieldsInOrder()
        {
            var args = new List<object?>();
            new Usage("claude-sonnet-5") { Input = 100, Output = 50, CacheRead = 10, CacheWrite5m = 20, CacheWrite1h = 30 }.AppendTo(args);
            Assert.Equal(new object?[] { "MODEL", "claude-sonnet-5", "INPUT", 100L, "OUTPUT", 50L, "CACHEREAD", 10L, "CACHEWRITE5M", 20L, "CACHEWRITE1H", 30L }, args);
        }

        [Fact]
        public void UsageOmitsZeroCounts()
        {
            var args = new List<object?>();
            new Usage("gpt-4") { Input = 5 }.AppendTo(args);
            Assert.Equal(new object?[] { "MODEL", "gpt-4", "INPUT", 5L }, args);
        }

        [Fact]
        public void UsageAppendIfPresentNullIsNoop()
        {
            var args = new List<object?> { "BASE" };
            Usage.AppendIfPresent(args, null);
            Assert.Equal(new object?[] { "BASE" }, args);
        }

        [Fact]
        public void FormatUsdPerMtokNeverUsesExponentNotation()
        {
            Assert.Equal("3", LlmStatsCommands.FormatUsdPerMtok(3));
            Assert.Equal("0.3", LlmStatsCommands.FormatUsdPerMtok(0.3));
            Assert.Equal("0.000001", LlmStatsCommands.FormatUsdPerMtok(0.000001));
            Assert.Equal("15.000001", LlmStatsCommands.FormatUsdPerMtok(15.000001));
            Assert.Equal("0", LlmStatsCommands.FormatUsdPerMtok(0));
            Assert.Equal("2.5", LlmStatsCommands.FormatUsdPerMtok(2.5));
        }

        // --- exact wire argument lists: PCACHE.SET / SCACHE.SET / keyed cache SET ---

        [Fact]
        public async Task PcacheSetArgsNoUsage()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var pcache = new PromptCache(fake);
            await pcache.SetAsync("ns", "p", "r");
            Assert.Equal(new[] { "PCACHE.SET", "ns", "p", "r" }, fake.LastArgs);
        }

        [Fact]
        public async Task PcacheSetArgsTtlMetaAndUsage()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var pcache = new PromptCache(fake);
            var usage = new Usage("gpt-4") { Input = 12, Output = 8, CacheRead = 4 };
            await pcache.SetAsync("ns", "p", "r", ttl: System.TimeSpan.FromSeconds(30), meta: "{\"k\":\"v\"}", usage: usage);
            Assert.Equal(
                new[] { "PCACHE.SET", "ns", "p", "r", "TTL", "30", "META", "{\"k\":\"v\"}", "MODEL", "gpt-4", "INPUT", "12", "OUTPUT", "8", "CACHEREAD", "4" },
                fake.LastArgs);
        }

        [Fact]
        public async Task ScacheSetArgsWithUsage()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var scache = new SemanticCache(fake);
            var usage = new Usage("gpt-4") { CacheWrite5m = 7 };
            await scache.SetAsync("ns", "q", "r", new[] { 0.1, 0.2 }, usage: usage);
            Assert.Equal(new[] { "SCACHE.SET", "ns", "q", "r", "0.1", "0.2", "MODEL", "gpt-4", "CACHEWRITE5M", "7" }, fake.LastArgs);
        }

        [Fact]
        public async Task KeyedCacheSetArgsWithUsage()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var toolCache = new KeyedCacheCommands(fake, "TOOL.CACHE");
            var usage = new Usage("gpt-4") { CacheWrite1h = 9 };
            await toolCache.SetAsync("ns", "k", "v", ttl: System.TimeSpan.FromSeconds(60), meta: "meta", usage: usage);
            Assert.Equal(new[] { "TOOL.CACHE.SET", "ns", "k", "v", "TTL", "60", "META", "meta", "MODEL", "gpt-4", "CACHEWRITE1H", "9" }, fake.LastArgs);
        }

        // --- exact wire argument lists: LLMSTATS.RECORD / LLMSTATS.PRICE / LLMBUDGET.CONSUME ---

        [Fact]
        public async Task LlmStatsRecordArgsNoOptions()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.RecordAsync("bucket", 100, 50, 0, 0.002, 120);
            Assert.Equal(new[] { "LLMSTATS.RECORD", "bucket", "100", "50", "0", "0.002", "120" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsRecordArgsModelAndCacheWrites()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.RecordAsync("bucket", 100, 50, 10, 0, 80, isError: false, model: "claude-sonnet-5", cacheWrite5m: 20, cacheWrite1h: 30);
            Assert.Equal(
                new[] { "LLMSTATS.RECORD", "bucket", "100", "50", "10", "0", "80", "ERROR", "0", "MODEL", "claude-sonnet-5", "CACHEWRITE5M", "20", "CACHEWRITE1H", "30" },
                fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsPriceSetArgsRequiredOnly()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.PriceSetAsync("my-model", 3, 15);
            Assert.Equal(new[] { "LLMSTATS.PRICE", "SET", "my-model", "INPUT", "3", "OUTPUT", "15" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsPriceSetArgsAllOverrides()
        {
            var fake = new FakeCommandExecutor(RespValue.Simple("OK"));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.PriceSetAsync("my-model", 3, 15, cacheReadUsdPerMtok: 0.5, cacheWrite5mUsdPerMtok: 4, cacheWrite1hUsdPerMtok: 6);
            Assert.Equal(
                new[] { "LLMSTATS.PRICE", "SET", "my-model", "INPUT", "3", "OUTPUT", "15", "CACHEREAD", "0.5", "CACHEWRITE5M", "4", "CACHEWRITE1H", "6" },
                fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsPriceGetArgs()
        {
            var fake = new FakeCommandExecutor(PriceReply("gpt-4", "unpriced", false, false));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.PriceGetAsync("gpt-4");
            Assert.Equal(new[] { "LLMSTATS.PRICE", "GET", "gpt-4" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsPriceListArgs()
        {
            var fake = new FakeCommandExecutor(RespValue.Array(System.Array.Empty<RespValue>()));
            var llmStats = new LlmStatsCommands(fake);
            await llmStats.PriceListAsync();
            Assert.Equal(new[] { "LLMSTATS.PRICE", "LIST" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmStatsPriceDelArgs()
        {
            var fake = new FakeCommandExecutor(RespValue.Integer(1));
            var llmStats = new LlmStatsCommands(fake);
            bool removed = await llmStats.PriceDelAsync("gpt-4");
            Assert.True(removed);
            Assert.Equal(new[] { "LLMSTATS.PRICE", "DEL", "gpt-4" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmBudgetConsumeArgsLegacy()
        {
            var fake = new FakeCommandExecutor(RespValue.Integer(1));
            var llmBudget = new LlmBudgetCommands(fake);
            Assert.True(await llmBudget.ConsumeAsync("name", 10, 0.01));
            Assert.Equal(new[] { "LLMBUDGET.CONSUME", "name", "10", "0.01" }, fake.LastArgs);
        }

        [Fact]
        public async Task LlmBudgetConsumeArgsWithUsage()
        {
            var fake = new FakeCommandExecutor(RespValue.Integer(1));
            var llmBudget = new LlmBudgetCommands(fake);
            var usage = new Usage("gpt-4") { Input = 10, Output = 5 };
            await llmBudget.ConsumeAsync("name", 0, 0, usage);
            Assert.Equal(new[] { "LLMBUDGET.CONSUME", "name", "0", "0", "MODEL", "gpt-4", "INPUT", "10", "OUTPUT", "5" }, fake.LastArgs);
        }

        // --- canned reply parsing: RESP2 flat arrays and RESP3 maps, by field name ---

        private static RespValue Bulk(string s) => RespValue.Bulk(Encoding.UTF8.GetBytes(s));

        private static RespValue FlatArray(params (string Name, RespValue Value)[] fields)
        {
            var items = new List<RespValue>(fields.Length * 2);
            foreach (var (name, value) in fields)
            {
                items.Add(Bulk(name));
                items.Add(value);
            }
            return RespValue.Array(items);
        }

        private static RespValue FlatMap(params (string Name, RespValue Value)[] fields)
        {
            var items = new List<RespValue>(fields.Length * 2);
            foreach (var (name, value) in fields)
            {
                items.Add(Bulk(name));
                items.Add(value);
            }
            return RespValue.MapOf(items);
        }

        private static RespValue PriceReply(string model, string source, bool priced, bool retired) => FlatMap(
            ("model", Bulk(model)),
            ("source", Bulk(source)),
            ("priced", RespValue.Integer(priced ? 1 : 0)),
            ("retired", RespValue.Integer(retired ? 1 : 0)),
            ("input_usd_per_mtok", Bulk("3.000000")),
            ("output_usd_per_mtok", Bulk("15.000000")),
            ("cache_read_usd_per_mtok", Bulk("0.300000")),
            ("cache_write_5m_usd_per_mtok", Bulk("3.750000")),
            ("cache_write_1h_usd_per_mtok", Bulk("6.000000")));

        [Fact]
        public async Task LlmStatsPriceGetParsesResp3Map()
        {
            var fake = new FakeCommandExecutor(PriceReply("claude-sonnet-5", "custom", true, false));
            var got = await new LlmStatsCommands(fake).PriceGetAsync("claude-sonnet-5");
            Assert.Equal(new LlmPrice("claude-sonnet-5", "custom", true, false, 3, 15, 0.3, 3.75, 6), got);
        }

        [Fact]
        public async Task LlmStatsPriceGetUnknownModelIsNotAnError()
        {
            var fake = new FakeCommandExecutor(PriceReply("unknown-model", "unpriced", false, false));
            var got = await new LlmStatsCommands(fake).PriceGetAsync("unknown-model");
            Assert.Equal("unpriced", got.Source);
            Assert.False(got.Priced);
        }

        [Fact]
        public async Task LlmStatsPriceListParsesMultipleEntries()
        {
            var fake = new FakeCommandExecutor(RespValue.Array(new[]
            {
                PriceReply("model-a", "default", true, false),
                PriceReply("model-b", "custom", true, true),
            }));
            var got = await new LlmStatsCommands(fake).PriceListAsync();
            Assert.Equal(2, got.Count);
            Assert.Equal("model-a", got[0].Model);
            Assert.Equal("model-b", got[1].Model);
            Assert.True(got[1].Retired);
        }

        [Fact]
        public async Task ScacheStatsParsesResp3MapIncludingUsageSaved()
        {
            var fake = new FakeCommandExecutor(FlatMap(
                ("entries", RespValue.Integer(3)),
                ("hits", RespValue.Integer(10)),
                ("misses", RespValue.Integer(2)),
                ("hit_rate", Bulk("0.833333")),
                ("p50_us", RespValue.Integer(150)),
                ("usage_saved_requests", RespValue.Integer(10)),
                ("usage_saved_input_tokens", RespValue.Integer(120)),
                ("usage_saved_output_tokens", RespValue.Integer(80)),
                ("usage_saved_cache_read_tokens", RespValue.Integer(0)),
                ("usage_saved_cache_write_tokens", RespValue.Integer(0)),
                ("usage_saved_cost_usd", Bulk("0.000160"))));
            var got = await new SemanticCache(fake).StatsAsync("ns");
            Assert.Equal(new SemanticCacheStats(3, 10, 2, 0.833333, 150, new UsageSavings(10, 120, 80, 0, 0, 0.00016)), got);
        }

        [Fact]
        public async Task PcacheStatsParsesResp2FlatArrayMissingFieldsDefaultZero()
        {
            var fake = new FakeCommandExecutor(FlatArray(
                ("entries", RespValue.Integer(1)),
                ("hits", RespValue.Integer(4))));
            var got = await new PromptCache(fake).StatsAsync("ns");
            Assert.Equal(1, got.Entries);
            Assert.Equal(4, got.Hits);
            Assert.Equal(0, got.Misses);
            Assert.Equal(0.0, got.CostSavedUsd);
            Assert.Equal(new UsageSavings(0, 0, 0, 0, 0, 0.0), got.UsageSaved);
        }

        [Fact]
        public async Task KeyedCacheStatsParsesResp3Map()
        {
            var fake = new FakeCommandExecutor(FlatMap(
                ("entries", RespValue.Integer(7)),
                ("hits", RespValue.Integer(5)),
                ("misses", RespValue.Integer(1)),
                ("evictions", RespValue.Integer(0)),
                ("memory_bytes", RespValue.Integer(2048)),
                ("usage_saved_requests", RespValue.Integer(5)),
                ("usage_saved_input_tokens", RespValue.Integer(50)),
                ("usage_saved_output_tokens", RespValue.Integer(10)),
                ("usage_saved_cache_read_tokens", RespValue.Integer(0)),
                ("usage_saved_cache_write_tokens", RespValue.Integer(0)),
                ("usage_saved_cost_usd", Bulk("0.000050"))));
            var got = await new KeyedCacheCommands(fake, "TOOL.CACHE").StatsAsync("ns");
            Assert.Equal(7, got.Entries);
            Assert.Equal(2048, got.MemoryBytes);
            Assert.Equal(0.00005, got.UsageSaved.CostUsd);
        }

        [Fact]
        public async Task LlmStatsGetParsesResp2FlatArray()
        {
            var fake = new FakeCommandExecutor(FlatArray(
                ("requests", RespValue.Integer(10)),
                ("errors", RespValue.Integer(1)),
                ("input_tokens", RespValue.Integer(500)),
                ("output_tokens", RespValue.Integer(200)),
                ("cached_tokens", RespValue.Integer(50)),
                ("cost_usd", Bulk("0.0123")),
                ("avg_latency_ms", Bulk("42.5")),
                ("requests_1m", RespValue.Integer(3)),
                ("tokens_1m", RespValue.Integer(150)),
                ("cost_usd_1m", Bulk("0.004")),
                ("cache_write_5m_tokens", RespValue.Integer(20)),
                ("cache_write_1h_tokens", RespValue.Integer(0)),
                ("unpriced_requests", RespValue.Integer(2))));
            var got = await new LlmStatsCommands(fake).GetAsync("bucket");
            Assert.Equal(new LlmStatsSnapshot(10, 1, 500, 200, 50, 0.0123, 42.5, 3, 150, 0.004, 20, 0, 2), got);
        }

        [Fact]
        public async Task LlmBudgetStatusParsesResp3Map()
        {
            var fake = new FakeCommandExecutor(FlatMap(
                ("tokens_used", RespValue.Integer(100)),
                ("token_limit", RespValue.Integer(1000)),
                ("cost_used_usd", Bulk("0.01")),
                ("cost_limit_usd", Bulk("5.00")),
                ("window_seconds", RespValue.Integer(3600)),
                ("window_remaining", RespValue.Integer(1200)),
                ("requests_allowed", RespValue.Integer(9)),
                ("requests_rejected", RespValue.Integer(1)),
                ("unpriced_requests", RespValue.Integer(0))));
            var got = await new LlmBudgetCommands(fake).StatusAsync("name");
            Assert.Equal(new LlmBudgetStatus(100, 1000, 0.01, 5.00, 3600, 1200, 9, 1, 0), got);
        }
    }
}
