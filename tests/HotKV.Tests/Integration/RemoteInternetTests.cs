// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HotKV;
using HotKV.AI;
using HotKV.Commands;
using HotKV.Pipelines;
using HotKV.PubSub;
using HotKV.Transactions;
using Xunit;
using Xunit.Abstractions;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// A customer's view: connect over the internet to a TLS + password protected HotKV instance exactly as
    /// documented, then use the SDK. The instance is shared, so every key and namespace carries a unique
    /// <c>remote-csharp-&lt;random&gt;</c> prefix and is deleted afterwards; nothing global (FLUSH*, CONFIG SET, ACL,
    /// SHUTDOWN) is ever issued.
    /// <para>
    /// Skipped unless <c>HOTKV_REMOTE_HOST</c> is set. Configuration is read from the environment only:
    /// <c>HOTKV_REMOTE_HOST</c>, <c>HOTKV_REMOTE_PORT</c> (default 6379), <c>HOTKV_REMOTE_CA</c> (path of the CA file
    /// that signed the server certificate), <c>HOTKV_REMOTE_PASSWORD</c>. The password is never written anywhere:
    /// tests that need it skip when it is not set; the TLS verification and wrong password tests do not need it.
    /// </para>
    /// </summary>
    public class RemoteInternetTests
    {
        private readonly ITestOutputHelper _output;
        private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 10);

        public RemoteInternetTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

        private static string Host
        {
            get
            {
                string? h = Env("HOTKV_REMOTE_HOST");
                Skip.If(string.IsNullOrWhiteSpace(h), "HOTKV_REMOTE_HOST is not set; the remote customer scenario is skipped");
                return h!;
            }
        }

        private static int Port => int.TryParse(Env("HOTKV_REMOTE_PORT"), out int p) ? p : 6379;

        private static string Ca
        {
            get
            {
                string? c = Env("HOTKV_REMOTE_CA");
                Skip.If(string.IsNullOrWhiteSpace(c), "HOTKV_REMOTE_CA is not set");
                return c!;
            }
        }

        private static string Password
        {
            get
            {
                string? p = Env("HOTKV_REMOTE_PASSWORD");
                Skip.If(string.IsNullOrEmpty(p), "HOTKV_REMOTE_PASSWORD is not set");
                return p!;
            }
        }

        /// <summary>The options form: Host/Port/Password plus TLS verified against the CA file.</summary>
        private static HotKVClientOptions OptionsForm(string? password)
        {
            return new HotKVClientOptions
            {
                Host = Host,
                Port = Port,
                Password = password,
                ClientName = "csharp-sdk-remote-test",
                ConnectTimeout = TimeSpan.FromSeconds(15),
                CommandTimeout = TimeSpan.FromSeconds(30),
                Tls = new HotKVTlsOptions { Enabled = true, CaCertificatePath = Ca },
            };
        }

        /// <summary>The URL form a customer pastes from a dashboard: hotkvs://:password@host:port?caFile=...</summary>
        private static string UrlForm(string password) =>
            $"hotkvs://:{Uri.EscapeDataString(password)}@{Host}:{Port}?caFile={Uri.EscapeDataString(Ca)}&clientName=csharp-sdk-remote-url";

        private static string Key(string label) => $"remote-csharp-{RunId}:{label}";

        private async Task<T> TimedAsync<T>(string label, Func<Task<T>> body)
        {
            var sw = Stopwatch.StartNew();
            T result = await body();
            _output.WriteLine($"{label}: {sw.ElapsedMilliseconds} ms");
            return result;
        }

        // ------------------------------------------------------------------ connection and security

        [SkippableFact]
        public async Task TlsIsVerifiedAgainstTheCaAndPingWorksOrDemandsAPassword()
        {
            // No password: the TLS handshake (host name SAN IP + CA chain) must succeed, and the server then demands AUTH.
            await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(OptionsForm(null));
                await client.PingAsync();
            });
        }

        [SkippableFact]
        public async Task WrongPasswordIsACleanAuthenticationError()
        {
            var ex = await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(OptionsForm("remote-csharp-definitely-wrong"));
                await client.PingAsync();
            });
            _output.WriteLine($"wrong password -> {ex.Prefix}: {ex.RawMessage}");
            Assert.DoesNotContain("remote-csharp-definitely-wrong", ex.Message);
        }

        [SkippableFact]
        public async Task TlsWithoutTheCaFailsAndInsecureSkipVerifyIsTheOnlyWayAround()
        {
            string host = Host;
            var noCa = new HotKVClientOptions { Host = host, Port = Port, Password = "x", ConnectTimeout = TimeSpan.FromSeconds(15), Tls = new HotKVTlsOptions { Enabled = true } };
            await Assert.ThrowsAsync<HotKVConnectionException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(noCa);
                await client.PingAsync();
            });

            // the one extra test of the insecure option: the handshake succeeds, and a wrong password is then refused by AUTH
            var insecure = new HotKVClientOptions { Host = host, Port = Port, Password = "remote-csharp-definitely-wrong", ConnectTimeout = TimeSpan.FromSeconds(15), Tls = new HotKVTlsOptions { Enabled = true, InsecureSkipVerify = true } };
            await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(insecure);
                await client.PingAsync();
            });

            // a name that is not in the certificate is rejected even with the right CA
            var wrongName = OptionsForm("x");
            wrongName.Tls.ServerName = "not-this-host.example";
            await Assert.ThrowsAsync<HotKVConnectionException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(wrongName);
                await client.PingAsync();
            });
        }

        [SkippableFact]
        public async Task BothConnectionFormsWorkWithThePassword()
        {
            string pw = Password;
            await using (var viaOptions = await HotKVClient.ConnectAsync(OptionsForm(pw)))
            {
                var sw = Stopwatch.StartNew();
                Assert.Equal("PONG", await viaOptions.PingAsync());
                _output.WriteLine($"PING (options form, includes pooled connection setup): {sw.ElapsedMilliseconds} ms");
            }
            await using (var viaUrl = await HotKVClient.ConnectAsync(UrlForm(pw)))
            {
                Assert.Equal("PONG", await viaUrl.PingAsync());
                Assert.Equal("csharp-sdk-remote-url", await viaUrl.ClientGetNameAsync());
            }
        }

        // ------------------------------------------------------------------ core data structures

        [SkippableFact]
        public async Task CoreCommandsJourney()
        {
            await using var c = await HotKVClient.ConnectAsync(OptionsForm(Password));
            var keys = new List<string>();
            string K(string l) { string k = Key(l); keys.Add(k); return k; }
            try
            {
                // strings + expiry
                string s = K("str");
                await c.StringSetAsync(s, "hello");
                Assert.Equal("hello", await c.StringGetAsync(s));
                Assert.Equal(6, await c.IncrementByAsync(K("n"), 6));
                Assert.True(await c.ExpireAsync(s, TimeSpan.FromSeconds(100)));
                Assert.InRange(await c.TtlAsync(s), 90, 100);
                await c.PersistAsync(s);
                Assert.Equal(-1, await c.TtlAsync(s));
                await c.StringSetAsync(K("short"), "x", new SetOptions { Expiry = TimeSpan.FromMilliseconds(300) });
                await Task.Delay(700);
                Assert.Null(await c.StringGetAsync(Key("short")));

                // hashes
                string h = K("hash");
                await c.HashSetAsync(h, new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" });
                Assert.Equal("2", await c.HashGetAsync(h, "b"));
                Assert.Equal(2, (await c.HashGetAllAsync(h)).Count);
                Assert.Equal(5, await c.HashIncrementByAsync(h, "a", 4));

                // lists
                string l = K("list");
                await c.ListRightPushAsync(l, new[] { "1", "2", "3" });
                Assert.Equal(new[] { "1", "2", "3" }, await c.ListRangeAsync(l, 0, -1));
                Assert.Equal(new[] { "1" }, await c.ListLeftPopAsync(l));

                // sets
                string st = K("set");
                Assert.Equal(3, await c.SetAddAsync(st, new[] { "x", "y", "z" }));
                Assert.True(await c.SetIsMemberAsync(st, "y"));
                Assert.Equal(3, await c.SetCardinalityAsync(st));

                // sorted sets with float scores (plain decimals over the wire)
                string z = K("zset");
                await c.SortedSetAddAsync(z, new (string, double)[] { ("a", 0.00001), ("b", 1e21), ("c", -3.5) });
                Assert.Equal(new[] { "c", "a", "b" }, await c.SortedSetRangeByRankAsync(z, 0, -1));
                Assert.Equal(0.00001, await c.SortedSetScoreAsync(z, "a"));
                Assert.Equal(1e21, await c.SortedSetScoreAsync(z, "b"));

                // streams
                string x = K("stream");
                string id1 = await c.StreamAddAsync(x, new Dictionary<string, string> { ["k"] = "v1" });
                await c.StreamAddAsync(x, new Dictionary<string, string> { ["k"] = "v2" });
                var range = await c.StreamRangeAsync(x, "-", "+");
                Assert.Equal(2, range.Count);
                Assert.Equal(id1, range[0].Id);
                Assert.Equal("v2", range[1].Fields["k"]);

                // 1 MB (and a binary) value round trip
                string big = K("big");
                var payload = new byte[1024 * 1024];
                new Random(42).NextBytes(payload);
                var sw = Stopwatch.StartNew();
                await c.StringSetAsync(big, payload);
                long upMs = sw.ElapsedMilliseconds;
                sw.Restart();
                byte[]? back = await c.StringGetBytesAsync(big);
                _output.WriteLine($"1 MB upload {upMs} ms, download {sw.ElapsedMilliseconds} ms");
                Assert.NotNull(back);
                Assert.True(payload.AsSpan().SequenceEqual(back!), "1 MB value differs after the round trip");

                // generic command method for something with no typed wrapper
                string bf = K("bf");
                Assert.Equal("OK", (await c.CommandAsync("BF.RESERVE", bf, "0.01", 100)).AsString());
                Assert.Equal(1, (await c.CommandAsync("BF.ADD", bf, "item")).AsInt64());
            }
            finally
            {
                await c.DeleteAsync(keys);
            }
        }

        [SkippableFact]
        public async Task PipelineAndTransaction()
        {
            await using var c = await HotKVClient.ConnectAsync(OptionsForm(Password));
            string a = Key("pipe-a");
            string n = Key("pipe-n");
            string t1 = Key("tx-1");
            string t2 = Key("tx-2");
            try
            {
                var sw = Stopwatch.StartNew();
                IBatch batch = c.CreateBatch();
                var set = batch.StringSetAsync(a, "1");
                var incr = batch.IncrementByAsync(n, 5);
                var get = batch.StringGetAsync(a);
                var bad = batch.IncrementAsync(a + "-not-a-number-" + RunId);
                var typeErr = batch.ListLeftPopAsync(a);
                await batch.ExecuteAsync();
                _output.WriteLine($"pipeline of 5 commands: {sw.ElapsedMilliseconds} ms");
                Assert.True(await set);
                Assert.Equal(5, await incr);
                Assert.Equal("1", await get);
                Assert.Equal(1, await bad);
                await Assert.ThrowsAsync<HotKVWrongTypeException>(async () => await typeErr);

                await c.StringSetAsync(t1, "10");
                await using ITransaction tx = await c.CreateTransactionAsync();
                await tx.WatchAsync(new[] { t1 });
                var d = tx.DecrementByAsync(t1, 3);
                var i = tx.IncrementByAsync(t2, 3);
                Assert.True(await tx.ExecuteAsync());
                Assert.Equal(7, await d);
                Assert.Equal(3, await i);
            }
            finally
            {
                await c.DeleteAsync(new[] { a, n, t1, t2, a + "-not-a-number-" + RunId });
            }
        }

        [SkippableFact]
        public async Task PubSubOnSeparatePublisherAndSubscriberConnections()
        {
            string pw = Password;
            await using var publisher = await HotKVClient.ConnectAsync(OptionsForm(pw));
            await using var subClient = await HotKVClient.ConnectAsync(OptionsForm(pw));
            string channel = Key("chan");
            await using var subscriber = await subClient.CreateSubscriberAsync();
            await subscriber.SubscribeAsync(channel);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var e = subscriber.Messages(cts.Token).GetAsyncEnumerator(cts.Token);
            Assert.True(await e.MoveNextAsync());
            Assert.Equal(HotKVMessageKind.Subscribe, e.Current.Kind);

            var sw = Stopwatch.StartNew();
            Assert.Equal(1, await publisher.PublishAsync(channel, "over the internet"));
            Assert.True(await e.MoveNextAsync());
            _output.WriteLine($"publish to delivery: {sw.ElapsedMilliseconds} ms");
            Assert.Equal("over the internet", e.Current.Payload);
            Assert.Equal(channel, e.Current.Channel);
        }

        [SkippableFact]
        public async Task ReconnectsAfterTheServerClosesAConnection()
        {
            string pw = Password;
            var o = OptionsForm(pw);
            o.MaxPoolSize = 1; // one connection, so the kill below is certain to hit the one the client uses
            await using var victim = await HotKVClient.ConnectAsync(o);
            await using var killer = await HotKVClient.ConnectAsync(OptionsForm(pw));
            string key = Key("reconnect");
            try
            {
                await victim.StringSetAsync(key, "before");
                long id = await victim.ClientIdAsync();
                // CLIENT KILL ID only closes that one connection, which belongs to this test.
                Assert.Equal(1, (await killer.CommandAsync("CLIENT", "KILL", "ID", id)).AsInt64());
                await Task.Delay(300);

                // An idempotent read is retried once on a fresh connection automatically.
                Assert.Equal("before", await victim.StringGetAsync(key));

                // And the pool keeps working for writes afterwards.
                await victim.StringSetAsync(key, "after");
                Assert.Equal("after", await victim.StringGetAsync(key));
            }
            finally
            {
                await killer.DeleteAsync(key);
            }
        }

        [SkippableFact]
        public async Task ConcurrentUseFromSeveralTasks()
        {
            await using var c = await HotKVClient.ConnectAsync(OptionsForm(Password));
            string prefix = Key("conc");
            const int tasks = 16, perTask = 25;
            var sw = Stopwatch.StartNew();
            try
            {
                await Task.WhenAll(Enumerable.Range(0, tasks).Select(async t =>
                {
                    for (int i = 0; i < perTask; i++)
                    {
                        string k = $"{prefix}:{t}:{i}";
                        await c.StringSetAsync(k, $"{t}-{i}");
                        Assert.Equal($"{t}-{i}", await c.StringGetAsync(k));
                        await c.IncrementAsync(prefix + ":counter");
                    }
                }));
                _output.WriteLine($"{tasks} tasks x {perTask} set+get+incr: {sw.ElapsedMilliseconds} ms");
                Assert.Equal(tasks * perTask, long.Parse((await c.StringGetAsync(prefix + ":counter"))!));
            }
            finally
            {
                var keys = new List<string> { prefix + ":counter" };
                for (int t = 0; t < tasks; t++)
                {
                    for (int i = 0; i < perTask; i++)
                    {
                        keys.Add($"{prefix}:{t}:{i}");
                    }
                }
                await c.DeleteAsync(keys);
            }
        }

        // ------------------------------------------------------------------ AI features (typed API)

        [SkippableFact]
        public async Task AiFeaturesThroughTheTypedApi()
        {
            await using var c = await HotKVClient.ConnectAsync(UrlForm(Password));
            string id = "remote-csharp-" + RunId;
            var cleanup = new List<Func<Task>>();
            void Cleanup(Func<Task> f) => cleanup.Insert(0, f);
            try
            {
                // semantic cache: create, set with an embedding, get by a similar vector, stats, drop
                string sc = id + "-sc";
                await c.SemanticCache.CreateNamespaceAsync(sc, 4, VectorMetric.Cosine, threshold: 0.9);
                Cleanup(() => c.SemanticCache.DropAsync(sc));
                await c.SemanticCache.SetAsync(sc, "What is the capital of France?", "Paris", new[] { 0.9f, 0.1f, 0.00001f, 0.2f });
                var hit = await TimedAsync("SCACHE.GET", () => c.SemanticCache.GetAsync(sc, new[] { 0.89f, 0.11f, 0.00001f, 0.21f }));
                Assert.NotNull(hit);
                Assert.Equal("Paris", hit!.Response);
                Assert.Null(await c.SemanticCache.GetAsync(sc, new[] { 0f, 0f, 1f, 0f }));
                var scStats = await c.SemanticCache.StatsAsync(sc);
                Assert.Equal(1, scStats.Entries);
                Assert.Equal(1, scStats.Hits);

                // prompt cache with usage tracking
                string pc = id + "-pc";
                await c.PromptCache.CreateNamespaceAsync(pc, ttl: TimeSpan.FromMinutes(5), costPerHit: 0.00001);
                Cleanup(() => c.PromptCache.DropAsync(pc));
                int calls = 0;
                var request = new PromptCacheKeyRequest { Provider = "openai", Model = "gpt-4o", Messages = new[] { new PromptMessage("user", "2+2?") } };
                var first = await c.PromptCache.GetOrCallAsync(pc, request, _ => { calls++; return Task.FromResult(new LlmCallResult("4", new LlmUsage("gpt-4o", 10, 1))); });
                var second = await c.PromptCache.GetOrCallAsync(pc, request, _ => { calls++; return Task.FromResult(new LlmCallResult("never")); });
                Assert.False(first.Hit);
                Assert.True(second.Hit);
                Assert.Equal("4", second.Response);
                Assert.Equal(1, calls);

                // keyed caches
                foreach (var (family, label) in new[] { (c.ToolCache, "tool"), (c.NegativeCache, "neg"), (c.GuardCache, "guard"), (c.RerankCache, "rerank") })
                {
                    string ns = $"{id}-{label}";
                    await family.CreateAsync(ns, maxEntries: 100);
                    var f = family;
                    Cleanup(() => f.DropAsync(ns));
                    await family.SetAsync(ns, "k", "v", ttl: TimeSpan.FromMinutes(1));
                    Assert.Equal("v", await family.GetAsync(ns, "k"));
                    Assert.Equal(1, (await family.StatsAsync(ns)).Entries);
                }

                // LLM budget with float limits (1e-5 is sent as 0.00001, never as an exponent), and stats
                string budget = id + "-budget";
                await c.LlmBudget.SetAsync(budget, 1_000_000, 0.00001, 3600);
                Cleanup(() => c.LlmBudget.DropAsync(budget));
                Assert.Equal(1e-5, (await c.LlmBudget.StatusAsync(budget)).CostLimitUsd, 12);
                await c.LlmBudget.SetAsync(budget, 1_000_000, 12.5, 3600);
                Assert.True(await c.LlmBudget.ConsumeAsync(budget, 100, 0.000001));
                Assert.Equal(100, (await c.LlmBudget.StatusAsync(budget)).TokensUsed);
                string bucket = id + "-bucket";
                await c.LlmStats.CreateBucketAsync(bucket);
                Cleanup(() => c.LlmStats.DropAsync(bucket));
                await c.LlmStats.RecordAsync(bucket, 10, 20, 0, 0.000002, 120);
                Assert.Equal(1, (await c.LlmStats.GetAsync(bucket)).Requests);

                // prompt registry and embedding dedup
                string prompt = id + "-prompt";
                Assert.Equal(1, await c.PromptRegistry.PutAsync(prompt, "Hello {{name}}"));
                Cleanup(() => c.PromptRegistry.DropAsync(prompt));
                Assert.Equal("Hello {{name}}", (await c.PromptRegistry.GetLatestAsync(prompt))!.Content);
                string dd = id + "-dedup";
                Assert.True(await c.EmbedDedup.PutAsync(dd, "h1", "model", "0.1,0.2"));
                Cleanup(() => c.EmbedDedup.DropAsync(dd));
                Assert.Equal("0.1,0.2", await c.EmbedDedup.GetAsync(dd, "h1"));

                // agent memory: conversation, semantic, working
                string agent = id + "-agent";
                await c.AgentMemory.Conversation.CreateAsync(agent, window: 10);
                Cleanup(() => c.AgentMemory.Conversation.DropAsync(agent));
                await c.AgentMemory.Conversation.AppendAsync(agent, "user", "hi");
                await c.AgentMemory.Conversation.AppendAsync(agent, "assistant", "hello");
                Assert.Equal(2, (await c.AgentMemory.Conversation.GetAsync(agent)).Count);
                await c.AgentMemory.Semantic.CreateAsync(agent, 3);
                Cleanup(() => c.AgentMemory.Semantic.ForgetAsync(agent, "fact1"));
                await c.AgentMemory.Semantic.StoreAsync(agent, "fact1", "the sky is blue", new[] { 1f, 0f, 0.00001f });
                var recalled = await c.AgentMemory.Semantic.RecallAsync(agent, 1, new[] { 1f, 0f, 0f });
                Assert.Equal("fact1", recalled[0].Key);
                await c.AgentMemory.Working.SetAsync(agent, "goal", "answer politely", TimeSpan.FromMinutes(5));
                Cleanup(() => c.AgentMemory.Working.ClearAsync(agent));
                Assert.Equal("answer politely", await c.AgentMemory.Working.GetAsync(agent, "goal"));

                // RAG
                string rag = id + "-rag";
                await c.Rag.CreateAsync(rag, 3);
                Cleanup(() => c.Rag.DropAsync(rag));
                Assert.True(await c.Rag.IngestAsync(rag, "doc1", "HotKV is a fast in-memory data store.", null, new IReadOnlyList<float>[] { new[] { 1f, 0f, 0f } }) >= 1);
                var found = await c.Rag.SearchAsync(rag, "fast data store", 3, 0.5, new[] { 1f, 0f, 0f });
                Assert.NotEmpty(found);
                Assert.Equal("doc1", found[0].DocId);

                // rate limiting
                string rl = id + "-rl";
                await c.RateLimit.CreateAsync(rl, 2, 60_000);
                Cleanup(() => c.RateLimit.DropAsync(rl));
                Assert.True((await c.RateLimit.CheckAsync(rl, "u")).Allowed);
                Assert.True((await c.RateLimit.CheckAsync(rl, "u")).Allowed);
                Assert.False((await c.RateLimit.CheckAsync(rl, "u")).Allowed);
                var gcra = await c.Gcra.LimitAsync(id + ":gcra", periodMs: 1000, burst: 2);
                Assert.False(gcra.Denied);
                await c.Gcra.ResetAsync(id + ":gcra");

                // time series
                string ts = id + "-ts";
                await c.TimeSeries.CreateAsync(ts, retentionMs: 3_600_000);
                Cleanup(() => c.TimeSeries.DeleteAsync(ts));
                long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                await c.TimeSeries.AddAsync(ts, now, 0.000001);
                await c.TimeSeries.AddAsync(ts, now + 1, 42.5);
                var samples = await c.TimeSeries.RangeAsync(ts);
                Assert.Equal(2, samples.Count);
                Assert.Equal(42.5, samples[1].Value);

                // feature store
                string fg = id + "-features";
                await c.FeatureStore.CreateAsync(fg, "remote test", new (string, FeatureType)[] { ("age", FeatureType.Int64), ("score", FeatureType.Double) });
                Cleanup(() => c.FeatureStore.DropAsync(fg));
                await c.FeatureStore.SetAsync(fg, "u1", new Dictionary<string, string> { ["age"] = "30", ["score"] = "0.5" });
                Assert.Equal("30", (await c.FeatureStore.GetAsync(fg, "u1"))!.Values["age"]);

                // vector similarity
                string vi = id + "-vsim";
                await c.Vsim.CreateIndexAsync(vi, 3, VectorMetric.Cosine);
                Cleanup(() => c.Vsim.DropIndexAsync(vi));
                await c.Vsim.SetAsync(vi, "a", new[] { 1f, 0f, 0f });
                await c.Vsim.SetAsync(vi, "b", new[] { 0f, 1f, 0f });
                var nearest = await c.Vsim.SearchAsync(vi, 1, new[] { 0.9f, 0.1f, 0f });
                Assert.Equal("a", nearest[0].Key);
            }
            finally
            {
                foreach (var f in cleanup)
                {
                    try
                    {
                        await f();
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine("cleanup: " + ex.Message);
                    }
                }
            }
        }
    }
}
