// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;

namespace HotKV.Commands
{
    /// <summary>
    /// Decides whether a command is safe to retry automatically after a
    /// connection loss. Only reads that cannot corrupt state are retried;
    /// writes never are unless the caller retries them explicitly.
    /// </summary>
    internal static class CommandClassification
    {
        private static readonly HashSet<string> ReadCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "MGET", "GETRANGE", "SUBSTR", "STRLEN", "EXISTS", "TYPE", "TTL", "PTTL", "DBSIZE", "KEYS",
            "SCAN", "HSCAN", "SSCAN", "ZSCAN", "LLEN", "LRANGE", "LINDEX", "LPOS", "SMEMBERS", "SISMEMBER",
            "SMISMEMBER", "SCARD", "SINTER", "SUNION", "SDIFF", "SRANDMEMBER", "HGET", "HMGET", "HGETALL",
            "HKEYS", "HVALS", "HLEN", "HEXISTS", "HSTRLEN", "HRANDFIELD", "ZSCORE", "ZMSCORE", "ZRANGE",
            "ZRANGEBYSCORE", "ZRANGEBYLEX", "ZREVRANGE", "ZREVRANGEBYSCORE", "ZREVRANGEBYLEX", "ZCARD",
            "ZCOUNT", "ZLEXCOUNT", "ZRANK", "ZREVRANK", "ZRANDMEMBER", "ZDIFF", "ZINTER", "ZUNION", "XRANGE",
            "XREVRANGE", "XLEN", "XREAD", "XPENDING", "XINFO", "GEOPOS", "GEODIST", "GEOHASH", "GEOSEARCH",
            "BITCOUNT", "BITPOS", "GETBIT", "BITFIELD_RO", "PFCOUNT", "PING", "ECHO", "INFO", "TIME",
            "CONFIG", "CLIENT", "RANDOMKEY", "OBJECT", "MEMORY", "LASTSAVE", "LOLWUT", "SCRIPT", "DUMP",
            "COMMAND", "HELLO",
        };

        /// <summary>Command name suffixes (after the last '.') treated as read only across every HotKV/AI command family.</summary>
        private static readonly HashSet<string> ReadSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "GET", "GETEX", "STATS", "LIST", "INFO", "CHECK", "MEMBERS", "INTER", "COUNT", "EXISTS",
            "SEARCH", "SCHEMA", "VERSIONS", "STATUS", "PEEK", "RANGE", "AGG", "LATEST", "WINDOW", "RECALL",
            "QUERY", "TOP",
        };

        public static bool IsIdempotentRead(string? commandName)
        {
            if (string.IsNullOrEmpty(commandName))
            {
                return false;
            }
            if (ReadCommands.Contains(commandName))
            {
                return true;
            }
            int dot = commandName!.LastIndexOf('.');
            if (dot < 0 || dot == commandName.Length - 1)
            {
                return false;
            }
            return ReadSuffixes.Contains(commandName.Substring(dot + 1));
        }
    }
}
