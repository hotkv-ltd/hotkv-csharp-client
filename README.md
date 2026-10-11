# HotKV .NET Client

[![CI](https://github.com/hotkv-ltd/hotkv-csharp-client/actions/workflows/ci.yml/badge.svg)](https://github.com/hotkv-ltd/hotkv-csharp-client/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)

A high-performance .NET client for [HotKV](https://hotkv.com), a RESP2/RESP3-compatible
in-memory data store with built-in AI and LLM-serving features (semantic and prompt
caching, agent memory, RAG, feature store, rate limiting, time series and more).

- Async-first (`Task`/`ValueTask`, `CancellationToken` on every I/O call), with a
  handful of synchronous conveniences for scripts and simple call sites.
- RESP2 and RESP3 wire support (HELLO 3 negotiated by default, with automatic
  fallback to RESP2).
- Pooled connections, pipelines, MULTI/EXEC transactions, pub/sub, and a
  cluster client with CRC16 slot routing and MOVED/ASK redirect handling.
- Typed wrappers for the common command families (strings, keys, lists, sets,
  hashes, sorted sets, streams, geo, bitmaps, HyperLogLog, pub/sub, Lua scripting,
  basic server commands) and for the HotKV exclusive and AI command families listed
  under [Command coverage](#command-coverage). Every other command is one call away
  through the generic `CommandAsync` method.
- Zero runtime dependencies beyond the .NET base class library.

Targets `net8.0` and `netstandard2.1`, and HotKV server 0.3.0. The AI and
enterprise command families need a server with a valid license (without one the
server answers `NOLICENSE`, surfaced as `NotLicensedException`); the core data
store works unlicensed.

## Install

```
dotnet add package HotKV.Client
```

## Quick start

```csharp
using HotKV;
using HotKV.Commands;

await using var client = await HotKVClient.ConnectAsync("hotkv://localhost:6379");

await client.StringSetAsync("greeting", "hello from .NET");
string? value = await client.StringGetAsync("greeting");
Console.WriteLine(value);
```

`HotKVClient` implements `ICommandExecutor`; the typed commands of the core
families (strings, keys, lists, sets, hashes, sorted sets, streams, geo, bitmaps,
HyperLogLog, server, scripting, SCAN) are extension methods on that interface, so
the same methods work identically on the client itself, on a pipeline (`IBatch`)
and inside a transaction (`ITransaction`).

## Connecting: options, auth, TLS, pooling

`ConnectAsync` accepts a URL (`hotkv://`, `hotkvs://` for TLS, or `redis://`/
`rediss://` for drop-in migration) or a `HotKVClientOptions` instance:

```csharp
var options = new HotKVClientOptions
{
    Host = "cache.internal",
    Port = 6400,
    Username = "app",
    Password = Environment.GetEnvironmentVariable("HOTKV_PASSWORD"),
    Database = 2,
    ClientName = "orders-service",
    ConnectTimeout = TimeSpan.FromSeconds(5),
    CommandTimeout = TimeSpan.FromSeconds(10),
    MaxPoolSize = 20,
    IdleTimeout = TimeSpan.FromMinutes(1),
    Tls = new HotKVTlsOptions
    {
        Enabled = true,
        CaCertificatePath = "/etc/ssl/hotkv-ca.pem",
        // InsecureSkipVerify = true is for tests against a self-signed
        // server only: never enable it against a production endpoint.
    },
};

await using var client = await HotKVClient.ConnectAsync(options);
```

Or from a URL, with query string overrides:

```
hotkvs://app:secret@cache.internal:6400/2?clientName=orders-service&poolSize=20
```

The client keeps a bounded pool of connections (`MaxPoolSize`). A connection idle
longer than `IdleTimeout` is health checked with `PING` before reuse and silently
replaced if the check fails. Idempotent reads are retried once, automatically, on a
connection lost mid-command; writes are never retried automatically.

The escape hatch: `client.CommandAsync("MY.COMMAND", arg1, arg2)`, or the lower
level `ICommandExecutor.ExecuteAsync(...)`, send any command, including ones this
SDK has no typed wrapper for. Arguments may be strings, numbers, `byte[]` (binary
safe) and so on; the reply comes back as a `RespValue` and a server error reply is
thrown as a `HotKVServerException`.

```csharp
await client.CommandAsync("BF.RESERVE", "seen", "0.01", 1000);
bool added = (await client.CommandAsync("BF.ADD", "seen", "user:1")).AsInt64() == 1;
var values = (await client.CommandAsync("HGETDEL", "h", "FIELDS", 2, "a", "b")).AsArray();
```

TLS connections verify the server certificate: the host name must match the
certificate (use `HotKVTlsOptions.ServerName` when you connect by an address that is
not in it), and when `CaCertificatePath` is set the chain must end at one of the
certificates in that file (PEM bundles are supported; the system trust store is not
consulted). `InsecureSkipVerify` turns all of that off and is for tests only.

## Command coverage

The SDK has typed methods for most of the core families and for these HotKV
exclusive and licensed AI families: TAG, GCRA, VSIM (index, vectors and snapshots),
nanosecond TTL (NEXPIRE and friends), HOTKEYS, SCACHE and PCACHE (with the
`SemanticCache`/`PromptCache` get-or-compute helpers), TOOL.CACHE, NCACHE, GUARD,
RERANK, LLMSTATS (including the price table), LLMBUDGET, PROMPT, EMBED.DEDUP, AMEM,
RAG, RATELIMIT, TS, FEATURE (including SETAT), OBSERVE and ALERTS.

It does **not** have typed methods for the following; they are reachable through
`CommandAsync`, and the ones listed under "live checked" have been run against a
real server through it:

- `CLIENT TRACKING`, `CLIENT CACHING`, `CLIENT GETREDIR`, `CLIENT TRACKINGINFO`
  (live checked).
- The Bloom filter, Count-Min Sketch and Top-K families `BF.*`, `CMS.*`, `TOPK.*`
  (live checked).
- The newer additions `HGETDEL`, `HGETEX`, `HSETEX`, `MSETEX`, `DELEX` (live
  checked), `HIMPORT`, `XCFGSET`, and the cluster mode only `SFLUSH`, `CLUSTERSCAN`
  and `TRIMSLOTS` (only the "cluster support disabled" error was checked, on a
  standalone server).
- The enterprise administration families `TENANT.*`, `IAM.*`, `AUDIT.*`,
  `ENCRYPTION.*`, `REPLICATION.*` and the `*.SYNC` commands (not checked).
- Blocking list moves (`BLMOVE`, `BLMPOP`, ...), `LMPOP`, `ZMPOP`, `SORT`, `OBJECT`,
  `MEMORY`, `SLOWLOG`, `LATENCY`, `ACL`, `FUNCTION`/`FCALL`, the legacy `GEORADIUS`
  family, `ZRANGEBYSCORE`-style aliases of `ZRANGE`, `CLUSTER` administration and
  other server administration commands.

Any command not covered by the typed families above goes through `CommandAsync`.
Cluster support (`HotKVCluster`) is covered by unit tests with a fake server, not by tests against a
real multi-node cluster.

## Numbers on the wire

All floating point arguments are written as plain decimals with the shortest digits
that round-trip, under the invariant culture, never in exponent form and never with
a leading `+` (`1e-5` is sent as `0.00001`, `1e21` as `1000000000000000000000`).
NaN and infinities are rejected with an `ArgumentException` before anything is sent,
except for sorted set scores and weights, which send `inf` and `-inf`. Money amounts
that the server parses as strict fixed-point (the `LLMBUDGET.SET` cost limit and
`LLMSTATS.PRICE SET` prices) are rounded to six fractional digits and sent as digits
and a point only; a negative amount, one too large for 64-bit micro-dollars, or a
positive amount that would round to zero (which the server reads as "no limit") is
rejected with an `ArgumentException`. Vector methods also accept `IReadOnlyList<float>`,
which sends the float32 shortest digits.

## Pipelines

Queue several commands and send them together in one round trip. Each queued call
returns a `Task` that completes once `ExecuteAsync` runs; an error on one command
does not fail the others:

```csharp
IBatch batch = client.CreateBatch();
var t1 = batch.StringSetAsync("a", "1");
var t2 = batch.IncrementByAsync("counter", 5);
var t3 = batch.StringGetAsync("a");

await batch.ExecuteAsync();

Console.WriteLine(await t1); // true
Console.WriteLine(await t2); // 5
Console.WriteLine(await t3); // "1"
```

## Transactions

```csharp
await using ITransaction tx = await client.CreateTransactionAsync();
await tx.WatchAsync(new[] { "account:1", "account:2" });

var fromTask = tx.DecrementByAsync("account:1", 50);
var toTask = tx.IncrementByAsync("account:2", 50);

bool committed = await tx.ExecuteAsync();
if (!committed)
{
    // A watched key changed before EXEC; retry the whole operation.
}
```

Queuing every command up front and sending MULTI, the queued commands and EXEC as
one pipelined round trip (rather than one write per queued command) keeps a
transaction to a single network round trip. Dispose the transaction (an
`await using` block is the natural shape) to release its dedicated connection;
disposing before `ExecuteAsync` discards it and clears any WATCH.

## Pub/Sub

```csharp
await using var subscriber = await client.CreateSubscriberAsync();
await subscriber.SubscribeAsync("orders");

// Iterator style:
await foreach (var message in subscriber.Messages())
{
    if (message.Kind == HotKVMessageKind.Message)
    {
        Console.WriteLine($"{message.Channel}: {message.Payload}");
    }
}

// Or callback style:
subscriber.OnMessage += message => Console.WriteLine(message.Payload);
```

`PSubscribeAsync`/`SSubscribeAsync` (pattern and shard subscriptions) and the
matching unsubscribe methods are also available; publish with
`client.PublishAsync(channel, message)` or `client.ShardPublishAsync(...)`.
RESP3 push frames are handled transparently on the same connection.

## Cluster

```csharp
await using var cluster = await HotKVCluster.ConnectAsync(new[]
{
    "hotkv://node-a:6400",
    "hotkv://node-b:6400",
});

await cluster.StringSetAsync("user:42", "..."); // routed by CRC16 slot of "user:42"
```

`HotKVCluster` discovers the slot map from `CLUSTER SLOTS`, routes each command by
the CRC16 hash slot of its key (honoring `{hash tag}` pinning), follows `MOVED`
redirects by refreshing the map and `ASK` redirects by sending `ASKING` first, and
bounds how many redirects one command follows. It routes using the command's first
argument as the key by default; for a multi-key command whose key is elsewhere, use
`ExecuteWithRoutingKeyAsync` directly. Set `ReadFromReplicas = true` to round robin
reads across replicas (each opened with `READONLY`); writes always go to the
primary.

## HotKV exclusive commands

Reachable directly on the client, typed and documented:

```csharp
await client.Tags.SetAsync("doc:1", new[] { "reviewed", "urgent" });
var tagged = await client.Tags.MembersAsync("urgent");

await client.NExpireAsync("session:abc", relativeNanoseconds: 30_000_000_000); // nanosecond TTL
long remainingNs = await client.NTtlAsync("session:abc");

var hot = await client.HotKeysTopAsync(10);

var limited = await client.Gcra.LimitAsync("api:user:42", periodMs: 1000, burst: 10);
if (limited.Denied) { /* retry after limited.RetryAfterMs */ }

await client.Vsim.CreateIndexAsync("docs", dimensions: 768, VectorMetric.Cosine);
await client.Vsim.SetAsync("docs", "doc:1", embeddingVector);
var nearest = await client.Vsim.SearchAsync("docs", k: 5, queryVector);
```

## AI caching and agent commands

These command families are available as typed sub-APIs (each needs the matching
license feature on the server): `client.SemanticCache`,
`client.PromptCache`, `client.AgentMemory` (conversation, semantic, working,
episodic memory, cross-memory search and rolling summaries), `client.Rag`,
`client.LlmStats`, `client.LlmBudget`, `client.PromptRegistry`, `client.ToolCache`,
`client.NegativeCache`, `client.GuardCache`, `client.RerankCache`,
`client.EmbedDedup`, `client.RateLimit`, `client.TimeSeries`, `client.FeatureStore`,
`client.Observe`.

### Caching an LLM call: the miss-only pattern

`PromptCache` builds a deterministic cache key from the provider, model, system
prompt, messages and generation parameters (the exact canonical form, and so the
key, is identical across every official HotKV SDK) and calls your function only on
a miss. Reporting `Model` on the returned `LlmUsage` attaches it to the stored
entry, so a later hit's tokens and cost show up in `StatsAsync().UsageSaved`:

```csharp
using HotKV.AI;

await client.PromptCache.CreateNamespaceAsync("chat-completions", ttl: TimeSpan.FromHours(1));

var request = new PromptCacheKeyRequest
{
    Provider = "openai",
    Model = "gpt-6-sol",
    System = "You are a terse assistant.",
    Messages = new[] { new PromptMessage("user", "What is the capital of France?") },
    Params = new Dictionary<string, string> { ["temperature"] = "0.0" }, // format numbers yourself
};

var result = await client.PromptCache.GetOrCallAsync("chat-completions", request, async ct =>
{
    // This delegate only runs on a cache miss: the real LLM call happens
    // here, never on a hit.
    ChatResponse response = await myLlmClient.CompleteAsync(request, ct);
    return new LlmCallResult(response.Text, new LlmUsage("gpt-6-sol", response.InputTokens, response.OutputTokens));
});

Console.WriteLine(result.Hit ? "served from cache" : "called the model");
Console.WriteLine(result.Response);

var stats = await client.PromptCache.StatsAsync("chat-completions");
Console.WriteLine($"tokens saved so far: {stats.UsageSaved.InputTokens}");
```

`SemanticCache` does the same for embedding similarity lookups: it never computes
embeddings itself, it looks up the nearest cached answer above a similarity
threshold and only calls your function on a miss, returning the same
`LlmCallResult` shape as `PromptCache`'s:

```csharp
await client.SemanticCache.CreateNamespaceAsync("support-answers", dimensions: 1536, threshold: 0.92);

float[] queryEmbedding = await myEmbeddingClient.EmbedAsync(question);
var answer = await client.SemanticCache.GetOrComputeAsync(
    "support-answers", question, queryEmbedding,
    async ct =>
    {
        string text = await myLlmClient.AnswerAsync(question, ct);
        return new LlmCallResult(text);
    });
```

Both helpers are provider neutral: nothing in this SDK depends on any specific
LLM or embedding provider's client library.

### Token usage, cache savings and LLM cost tracking

`PromptCache.SetAsync`, `SemanticCache.SetAsync`, the keyed caches' `SetAsync`
(`client.ToolCache`/`NegativeCache`/`GuardCache`/`RerankCache`) and
`client.LlmBudget.ConsumeAsync` all take a trailing `Usage` (`Model` required,
token counts optional): recording the original call's token usage lets a later
cache hit attribute the tokens, and once the model is priced, the cost, it did
not have to spend. Their `StatsAsync` replies expose those savings as
`UsageSaved` alongside the existing counters.

`LLMSTATS.RECORD` with a model prices a call from HotKV's built-in price table
instead of a caller supplied cost; `LlmStats.PriceSetAsync` adds or overrides a
model's price, and `LlmBudget.ConsumeAsync` with a model enforces a budget
priced the same way:

```csharp
// Custom or fine-tuned model: 3 USD / 1M input tokens, 15 USD / 1M output.
await client.LlmStats.PriceSetAsync("acme-custom-v1", 3, 15);

await client.LlmStats.CreateBucketAsync("chatbot-usage");
await client.LlmStats.RecordAsync("chatbot-usage", 500, 120, 0, 0, 340, model: "acme-custom-v1");

var usage = await client.LlmStats.GetAsync("chatbot-usage");
Console.WriteLine($"spent ${usage.CostUsd:F6} so far");

bool allowed = await client.LlmBudget.ConsumeAsync("chatbot-daily", 0, 0,
    new Usage("acme-custom-v1") { Input = 500, Output = 120 });
if (!allowed)
{
    Console.WriteLine("daily LLM budget exceeded");
}
```

## Error handling

Every server error maps to a typed exception under `HotKVException`:

```csharp
try
{
    await client.ListLeftPushAsync("a-string-key", "x");
}
catch (HotKVWrongTypeException)
{
    // RESP error prefix WRONGTYPE
}
catch (NotLicensedException ex)
{
    // RESP error prefix NOLICENSE: an enterprise/AI command the current
    // license does not cover. ex.RawMessage has the server's detail.
}
catch (HotKVClusterRedirectException ex)
{
    // MOVED/ASK; HotKVCluster follows these automatically, this only
    // escapes on a non-cluster client or once the redirect bound is hit.
}
catch (HotKVConnectionException)
{
    // Connection lost or could not be established.
}
catch (HotKVTimeoutException)
{
    // ConnectTimeout/CommandTimeout exceeded.
}
catch (HotKVServerException ex)
{
    // Any other server error reply; ex.Prefix is the RESP error token
    // (e.g. "ERR"), ex.RawMessage the text after it.
}
```

A RESP null bulk reply or null array maps to .NET `null` (or `default`), never an
exception.

## Testing

```
dotnet test
```

Unit tests (RESP encoding/decoding, URL parsing, number formatting under foreign
cultures, TLS certificate decisions, CRC16 slot hashing, cache key determinism, exact
wire arguments of the typed commands) need no server. Integration tests run when the
`HOTKV_TEST_URL` environment variable is set (for example
`hotkv://127.0.0.1:6399`; they use the keyspace, so point them at a throwaway
instance) and are skipped with a clear reason otherwise. A command of an enterprise
or AI family the connected server has no license for is skipped (with
`NotLicensedException`'s detail) rather than failed, so against an unlicensed server
those families are only checked by the wire-argument unit tests.

More environment variables enable further suites:

| Variable | Enables |
|---|---|
| `HOTKV_TEST_AUTH_URL` | Password and ACL user tests against a password protected server |
| `HOTKV_TEST_TLS_URL`, `HOTKV_TEST_TLS_CA` | TLS tests against the TLS port of that server and the CA file that signed its certificate |
| `HOTKV_REMOTE_HOST`, `HOTKV_REMOTE_PORT`, `HOTKV_REMOTE_CA`, `HOTKV_REMOTE_PASSWORD` | The internet customer scenario against a TLS and password protected instance (unique key prefix, cleans up after itself) |

## Contributing

Bug reports and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Support

- Bugs and feature requests: [GitHub issues](https://github.com/hotkv-ltd/hotkv-csharp-client/issues)
- Questions and commercial support: support@hotkv.com
- Security vulnerabilities: see [SECURITY.md](SECURITY.md)

## License

Licensed under the Apache License, Version 2.0: see [LICENSE](LICENSE) and [NOTICE](NOTICE).

## Trademarks and affiliation

"HotKV" is a trademark of HotKV Ltd; the license does not grant rights to use it.
The HotKV server is a separate commercial product, and its enterprise commands need a HotKV license.

HotKV is an independent product of HotKV Ltd. It speaks the RESP protocol and implements
many Redis commands so that existing tools and client habits carry over, but it is not Redis,
Valkey, Dragonfly or KeyDB, and this SDK is built for and tested against HotKV. HotKV Ltd is not
affiliated with, endorsed by or sponsored by Redis Ltd., the Valkey project, DragonflyDB or KeyDB.
Redis is a registered trademark of Redis Ltd. Valkey, Dragonfly, KeyDB and all other product and
company names are trademarks of their respective owners; they are used here only to describe
protocol and command compatibility.
