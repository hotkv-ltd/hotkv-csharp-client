// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;
using HotKV.Commands;
using HotKV.Connection;

namespace HotKV
{
    /// <summary>
    /// A handful of synchronous conveniences for scripts, console tools and
    /// simple call sites that do not want to deal with async. The SDK is
    /// async-first throughout; these block on the async path with
    /// <c>.GetAwaiter().GetResult()</c> and should be avoided on threads
    /// that must stay responsive (UI threads, ASP.NET request threads).
    /// </summary>
    public sealed partial class HotKVClient
    {
        /// <summary>Synchronous PING.</summary>
        public string Ping() => this.PingAsync().GetAwaiter().GetResult();

        /// <summary>Synchronous GET.</summary>
        public string? StringGet(string key) => this.StringGetAsync(key).GetAwaiter().GetResult();

        /// <summary>Synchronous SET.</summary>
        public bool StringSet(string key, string value, SetOptions? options = null) =>
            this.StringSetAsync(key, value, options).GetAwaiter().GetResult();

        /// <summary>Synchronous DEL.</summary>
        public long Delete(string key) => this.DeleteAsync(key).GetAwaiter().GetResult();

        /// <summary>Synchronous EXISTS.</summary>
        public bool Exists(string key) => this.ExistsAsync(key).GetAwaiter().GetResult();

        /// <summary>Synchronous generic command escape hatch.</summary>
        public RespValue Command(string command, params object?[] args) => CommandAsync(command, args).GetAwaiter().GetResult();
    }
}
