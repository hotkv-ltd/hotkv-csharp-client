// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace HotKV.AI
{
    /// <summary>One chat-style message in a <see cref="PromptCacheKeyRequest"/>.</summary>
    public readonly struct PromptMessage
    {
        public string Role { get; }
        public string Content { get; }

        public PromptMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    /// <summary>
    /// The normalized shape a <see cref="PromptCache"/> key is derived
    /// from. <see cref="Params"/> values must already be formatted to
    /// strings by the caller (e.g. "0.7", not 0.7) so the same logical
    /// request hashes identically regardless of language or float
    /// formatting.
    /// </summary>
    public sealed class PromptCacheKeyRequest
    {
        public string Provider { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string System { get; set; } = string.Empty;
        public IReadOnlyList<PromptMessage> Messages { get; set; } = Array.Empty<PromptMessage>();
        public IReadOnlyDictionary<string, string> Params { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Builds the canonical, cross-language identical cache key for
    /// <see cref="PromptCache"/>: a compact, sorted-key JSON encoding of the
    /// request, SHA-256 hashed to a lowercase hex string. Every official
    /// HotKV SDK implements this exact same canonical form, so the same logical request produces the same key in
    /// every language.
    /// </summary>
    public static class CacheKeyBuilder
    {
        /// <summary>Computes the PromptCache key: the lowercase hex SHA-256 digest of <see cref="BuildCanonicalJson"/>'s UTF-8 bytes.</summary>
        public static string BuildPromptCacheKey(PromptCacheKeyRequest request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }
            byte[] canonicalBytes = Encoding.UTF8.GetBytes(BuildCanonicalJson(request));
#if NET8_0_OR_GREATER
            byte[] hash = SHA256.HashData(canonicalBytes);
#else
            using var sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(canonicalBytes);
#endif
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                hex.Append(b.ToString("x2"));
            }
            return hex.ToString();
        }

        /// <summary>
        /// Builds the exact canonical JSON string a <see cref="PromptCache"/>
        /// key is hashed from, without hashing it. Exposed mainly for
        /// tests that need to inspect the intermediate form; application
        /// code normally only needs <see cref="BuildPromptCacheKey"/>.
        /// </summary>
        public static string BuildCanonicalJson(PromptCacheKeyRequest request)
        {
            var sb = new StringBuilder();
            sb.Append('{');

            sb.Append("\"provider\":");
            AppendJsonString(sb, request.Provider ?? string.Empty);
            sb.Append(',');

            sb.Append("\"model\":");
            AppendJsonString(sb, request.Model ?? string.Empty);
            sb.Append(',');

            sb.Append("\"system\":");
            AppendJsonString(sb, request.System ?? string.Empty);
            sb.Append(',');

            sb.Append("\"messages\":[");
            var messages = request.Messages ?? Array.Empty<PromptMessage>();
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                sb.Append('{');
                sb.Append("\"role\":");
                AppendJsonString(sb, messages[i].Role ?? string.Empty);
                sb.Append(",\"content\":");
                AppendJsonString(sb, messages[i].Content ?? string.Empty);
                sb.Append('}');
            }
            sb.Append("],");

            sb.Append("\"params\":{");
            var paramsDict = request.Params ?? new Dictionary<string, string>();
            var sortedKeys = new List<string>(paramsDict.Keys);
            sortedKeys.Sort(CompareByUtf8Bytes);
            for (int i = 0; i < sortedKeys.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                AppendJsonString(sb, sortedKeys[i]);
                sb.Append(':');
                AppendJsonString(sb, paramsDict[sortedKeys[i]]);
            }
            sb.Append('}');

            sb.Append('}');
            return sb.ToString();
        }

        /// <summary>
        /// Byte-value ("plain ASCII sort, e.g. strcmp") comparison, matching
        /// every other HotKV SDK's key sort exactly regardless of platform
        /// string collation, including for the rare non-ASCII key.
        /// </summary>
        private static int CompareByUtf8Bytes(string a, string b)
        {
            byte[] ba = Encoding.UTF8.GetBytes(a);
            byte[] bb = Encoding.UTF8.GetBytes(b);
            int len = Math.Min(ba.Length, bb.Length);
            for (int i = 0; i < len; i++)
            {
                int diff = ba[i] - bb[i];
                if (diff != 0)
                {
                    return diff;
                }
            }
            return ba.Length - bb.Length;
        }

        /// <summary>Appends s to sb as a compact JSON string literal (with surrounding quotes): standard escaping for ", \ and control characters below 0x20 as \uXXXX; non-ASCII is left as literal UTF-8.</summary>
        private static void AppendJsonString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
