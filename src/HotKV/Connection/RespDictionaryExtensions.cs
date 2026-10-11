// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Collections.Generic;

namespace HotKV.Connection
{
    /// <summary>
    /// Convenience accessors for the flat "name, value, name, value, ..."
    /// dictionaries <see cref="RespValue.AsDictionary"/> produces, used
    /// throughout the HotKV exclusive and AI command wrappers to turn a
    /// labeled reply into a typed record.
    /// </summary>
    public static class RespDictionaryExtensions
    {
        public static string GetString(this IReadOnlyDictionary<string, RespValue> dict, string key, string fallback = "") =>
            dict.TryGetValue(key, out var v) ? v.AsString() ?? fallback : fallback;

        public static string? GetNullableString(this IReadOnlyDictionary<string, RespValue> dict, string key) =>
            dict.TryGetValue(key, out var v) ? v.AsString() : null;

        public static long GetInt64(this IReadOnlyDictionary<string, RespValue> dict, string key, long fallback = 0) =>
            dict.TryGetValue(key, out var v) ? v.AsInt64() : fallback;

        public static double GetDouble(this IReadOnlyDictionary<string, RespValue> dict, string key, double fallback = 0) =>
            dict.TryGetValue(key, out var v) ? v.AsDouble() : fallback;

        public static bool GetBool(this IReadOnlyDictionary<string, RespValue> dict, string key) =>
            dict.GetInt64(key) == 1;
    }
}
