// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using HotKV.AI;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class CacheKeyBuilderTests
    {
        [Fact]
        public void CanonicalJsonMatchesTheSpecShapeForASimpleRequest()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "openai",
                Model = "gpt-4",
                System = "",
                Messages = new[] { new PromptMessage("user", "hi") },
                Params = new Dictionary<string, string> { ["temperature"] = "0.7" },
            };

            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.Equal(
                "{\"provider\":\"openai\",\"model\":\"gpt-4\",\"system\":\"\",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}],\"params\":{\"temperature\":\"0.7\"}}",
                json);
        }

        [Fact]
        public void KeyMatchesAnIndependentlyComputedSha256OfTheSimpleRequest()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "openai",
                Model = "gpt-4",
                System = "",
                Messages = new[] { new PromptMessage("user", "hi") },
                Params = new Dictionary<string, string> { ["temperature"] = "0.7" },
            };

            // Ground truth computed independently with `sha256sum` over the
            // exact canonical string asserted in the test above.
            const string expected = "b8c8d4b2f6e89ad6680f131b679ad2a74a8c779566ccd2587c64518e1e43184e";
            Assert.Equal(expected, CacheKeyBuilder.BuildPromptCacheKey(request));
            Assert.Equal(64, expected.Length);
        }

        [Fact]
        public void KeyMatchesAnIndependentlyComputedSha256WithSortedParamsAndNonAsciiContent()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "anthropic",
                Model = "claude-sonnet-5",
                System = "be helpful",
                Messages = new[]
                {
                    new PromptMessage("system", "café"),
                    new PromptMessage("user", "line1\nline2"),
                },
                // Deliberately out of order: sorted ascending by byte value
                // should become max_tokens, temperature, top_p.
                Params = new Dictionary<string, string>
                {
                    ["top_p"] = "0.9",
                    ["temperature"] = "0.2",
                    ["max_tokens"] = "256",
                },
            };

            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.Equal(
                "{\"provider\":\"anthropic\",\"model\":\"claude-sonnet-5\",\"system\":\"be helpful\"," +
                "\"messages\":[{\"role\":\"system\",\"content\":\"café\"},{\"role\":\"user\",\"content\":\"line1\\nline2\"}]," +
                "\"params\":{\"max_tokens\":\"256\",\"temperature\":\"0.2\",\"top_p\":\"0.9\"}}",
                json);

            // Ground truth computed independently with Python hashlib and
            // cross-checked with `sha256sum`, both over the UTF-8 bytes of
            // the exact canonical string asserted above.
            const string expected = "ef45f3d061a92ffb8d527a20298053318d41420517a3b7edf0887af22880aba7";
            Assert.Equal(expected, CacheKeyBuilder.BuildPromptCacheKey(request));
        }

        [Fact]
        public void SameLogicalRequestProducesTheSameKeyEveryTime()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "openai",
                Model = "gpt-4o",
                System = "you are terse",
                Messages = new[] { new PromptMessage("user", "what is 2+2") },
                Params = new Dictionary<string, string> { ["temperature"] = "0.0", ["seed"] = "7" },
            };

            string key1 = CacheKeyBuilder.BuildPromptCacheKey(request);
            string key2 = CacheKeyBuilder.BuildPromptCacheKey(request);
            Assert.Equal(key1, key2);
            Assert.Equal(64, key1.Length);
            Assert.Matches("^[0-9a-f]{64}$", key1);
        }

        [Fact]
        public void ParamInsertionOrderDoesNotAffectTheKey()
        {
            var a = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = System.Array.Empty<PromptMessage>(),
                Params = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2", ["c"] = "3" },
            };
            var b = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = System.Array.Empty<PromptMessage>(),
                Params = new Dictionary<string, string> { ["c"] = "3", ["a"] = "1", ["b"] = "2" },
            };

            Assert.Equal(CacheKeyBuilder.BuildPromptCacheKey(a), CacheKeyBuilder.BuildPromptCacheKey(b));
        }

        [Fact]
        public void DifferentMessageOrderProducesADifferentKey()
        {
            var a = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = new[] { new PromptMessage("user", "a"), new PromptMessage("user", "b") },
                Params = new Dictionary<string, string>(),
            };
            var b = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = new[] { new PromptMessage("user", "b"), new PromptMessage("user", "a") },
                Params = new Dictionary<string, string>(),
            };

            Assert.NotEqual(CacheKeyBuilder.BuildPromptCacheKey(a), CacheKeyBuilder.BuildPromptCacheKey(b));
        }

        [Fact]
        public void MissingSystemAndParamsAreStillPresentAsEmpty()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = System.Array.Empty<PromptMessage>(),
            };
            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.Contains("\"system\":\"\"", json);
            Assert.Contains("\"params\":{}", json);
        }

        [Fact]
        public void ControlCharactersBelow0x20AreEscapedAsUnicodeExceptTheNamedOnes()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = new[] { new PromptMessage("user", "a\u0001b\tc\nd") },
            };
            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.Contains("a\\u0001b\\tc\\nd", json);
        }

        [Fact]
        public void QuotesAndBackslashesAreEscaped()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = new[] { new PromptMessage("user", "say \"hi\" \\ ok") },
            };
            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.Contains("say \\\"hi\\\" \\\\ ok", json);
        }

        [Fact]
        public void NoInsignificantWhitespaceAnywhere()
        {
            var request = new PromptCacheKeyRequest
            {
                Provider = "p",
                Model = "m",
                Messages = new[] { new PromptMessage("user", "a"), new PromptMessage("assistant", "b") },
                Params = new Dictionary<string, string> { ["x"] = "1", ["y"] = "2" },
            };
            string json = CacheKeyBuilder.BuildCanonicalJson(request);
            Assert.DoesNotContain(": ", json);
            Assert.DoesNotContain(", ", json);
        }
    }
}
