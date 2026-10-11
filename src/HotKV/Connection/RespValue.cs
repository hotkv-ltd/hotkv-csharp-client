// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HotKV.Connection
{
    /// <summary>The wire type of a parsed RESP2/RESP3 reply.</summary>
    public enum RespType
    {
        SimpleString,
        Error,
        Integer,
        BulkString,
        Array,
        Null,
        Boolean,
        Double,
        BigNumber,
        BlobError,
        VerbatimString,
        Map,
        Set,
        Push,
    }

    /// <summary>
    /// A single parsed RESP2/RESP3 value. This is the SDK's protocol level
    /// representation; typed command wrappers convert it into idiomatic .NET
    /// types. Values are immutable once constructed.
    /// </summary>
    public sealed class RespValue
    {
        /// <summary>The single shared instance representing a RESP null (bulk, array or RESP3 '_').</summary>
        public static readonly RespValue Null = new RespValue(RespType.Null);

        public RespType Type { get; }
        private readonly byte[]? _bytes;
        private readonly long _int;
        private readonly double _dbl;
        private readonly IReadOnlyList<RespValue>? _items;
        private readonly string? _verbatimFormat;

        private RespValue(RespType type)
        {
            Type = type;
        }

        private RespValue(RespType type, byte[] bytes, string? verbatimFormat = null)
        {
            Type = type;
            _bytes = bytes;
            _verbatimFormat = verbatimFormat;
        }

        private RespValue(RespType type, long value)
        {
            Type = type;
            _int = value;
        }

        private RespValue(RespType type, double value)
        {
            Type = type;
            _dbl = value;
        }

        private RespValue(RespType type, IReadOnlyList<RespValue> items)
        {
            Type = type;
            _items = items;
        }

        public static RespValue Simple(string text) => new RespValue(RespType.SimpleString, Encoding.UTF8.GetBytes(text));
        public static RespValue Error(string rawMessage) => new RespValue(RespType.Error, Encoding.UTF8.GetBytes(rawMessage));
        public static RespValue BlobError(string rawMessage) => new RespValue(RespType.BlobError, Encoding.UTF8.GetBytes(rawMessage));
        public static RespValue Integer(long value) => new RespValue(RespType.Integer, value);
        public static RespValue Bulk(byte[] value) => new RespValue(RespType.BulkString, value);
        public static RespValue Verbatim(string format, byte[] value) => new RespValue(RespType.VerbatimString, value, format);
        public static RespValue Boolean(bool value) => new RespValue(RespType.Boolean, value ? 1L : 0L);
        public static RespValue Double(double value) => new RespValue(RespType.Double, value);
        public static RespValue BigNumber(string digits) => new RespValue(RespType.BigNumber, Encoding.ASCII.GetBytes(digits));
        public static RespValue Array(IReadOnlyList<RespValue> items) => new RespValue(RespType.Array, items);
        public static RespValue SetOf(IReadOnlyList<RespValue> items) => new RespValue(RespType.Set, items);
        public static RespValue MapOf(IReadOnlyList<RespValue> flatPairs) => new RespValue(RespType.Map, flatPairs);
        public static RespValue PushOf(IReadOnlyList<RespValue> items) => new RespValue(RespType.Push, items);

        /// <summary>True for a RESP null (bulk string, array or RESP3 '_'). Maps to .NET null.</summary>
        public bool IsNull => Type == RespType.Null;

        /// <summary>True for an Error or BlobError reply.</summary>
        public bool IsError => Type == RespType.Error || Type == RespType.BlobError;

        /// <summary>True for Array, Set, Map or Push (anything backed by a child item list).</summary>
        public bool IsAggregate => Type == RespType.Array || Type == RespType.Set || Type == RespType.Map || Type == RespType.Push;

        /// <summary>The raw error text (without the leading '-'/'!' marker), for an Error/BlobError value.</summary>
        public string ErrorText => Type == RespType.Error || Type == RespType.BlobError
            ? Encoding.UTF8.GetString(_bytes!)
            : throw new HotKVProtocolException($"expected an error reply, got {Type}");

        /// <summary>Splits a raw RESP error string into its leading token prefix and the rest of the message.</summary>
        public static (string Prefix, string Message) SplitError(string raw)
        {
            int space = raw.IndexOf(' ');
            if (space < 0)
            {
                return (raw, string.Empty);
            }
            return (raw.Substring(0, space), raw.Substring(space + 1));
        }

        /// <summary>The raw bytes of a BulkString/VerbatimString/BigNumber value, or null.</summary>
        public byte[]? RawBytes => _bytes;

        /// <summary>For a VerbatimString, the 3-character format tag (e.g. "txt", "mkd").</summary>
        public string? VerbatimFormat => _verbatimFormat;

        /// <summary>Converts this value to a UTF-8 string, or null for a RESP null.</summary>
        public string? AsString()
        {
            switch (Type)
            {
                case RespType.Null:
                    return null;
                case RespType.SimpleString:
                case RespType.BulkString:
                case RespType.VerbatimString:
                case RespType.BigNumber:
                    return Encoding.UTF8.GetString(_bytes!);
                case RespType.Integer:
                    return _int.ToString(CultureInfo.InvariantCulture);
                case RespType.Double:
                    return FormatDouble(_dbl);
                case RespType.Boolean:
                    return _int != 0 ? "1" : "0";
                case RespType.Error:
                case RespType.BlobError:
                    throw HotKVServerException.Create(SplitError(ErrorText).Prefix, SplitError(ErrorText).Message);
                default:
                    throw new HotKVProtocolException($"cannot convert {Type} to a string");
            }
        }

        /// <summary>Converts this value to raw bytes, or null for a RESP null. Binary safe.</summary>
        public byte[]? AsBytes()
        {
            switch (Type)
            {
                case RespType.Null:
                    return null;
                case RespType.BulkString:
                case RespType.VerbatimString:
                case RespType.BigNumber:
                    return _bytes!;
                case RespType.SimpleString:
                    return _bytes!;
                default:
                    var s = AsString();
                    return s is null ? null : Encoding.UTF8.GetBytes(s);
            }
        }

        /// <summary>Converts this value to a 64 bit integer.</summary>
        public long AsInt64()
        {
            switch (Type)
            {
                case RespType.Integer:
                    return _int;
                case RespType.Boolean:
                    return _int;
                case RespType.Double:
                    return (long)_dbl;
                case RespType.BulkString:
                case RespType.SimpleString:
                case RespType.VerbatimString:
                case RespType.BigNumber:
                    return long.Parse(Encoding.UTF8.GetString(_bytes!), NumberStyles.Integer, CultureInfo.InvariantCulture);
                case RespType.Null:
                    throw new HotKVProtocolException("cannot convert a null reply to an integer");
                default:
                    throw new HotKVProtocolException($"cannot convert {Type} to an integer");
            }
        }

        /// <summary>Converts this value to a nullable 64 bit integer, null for a RESP null.</summary>
        public long? AsNullableInt64() => Type == RespType.Null ? (long?)null : AsInt64();

        /// <summary>Converts this value to a double, accepting RESP3 doubles, bulk strings and integers.</summary>
        public double AsDouble()
        {
            switch (Type)
            {
                case RespType.Double:
                    return _dbl;
                case RespType.Integer:
                    return _int;
                case RespType.Boolean:
                    return _int;
                case RespType.BulkString:
                case RespType.SimpleString:
                case RespType.VerbatimString:
                case RespType.BigNumber:
                    var s = Encoding.UTF8.GetString(_bytes!);
                    if (string.Equals(s, "inf", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "+inf", StringComparison.OrdinalIgnoreCase))
                    {
                        return double.PositiveInfinity;
                    }
                    if (string.Equals(s, "-inf", StringComparison.OrdinalIgnoreCase))
                    {
                        return double.NegativeInfinity;
                    }
                    return double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
                case RespType.Null:
                    throw new HotKVProtocolException("cannot convert a null reply to a double");
                default:
                    throw new HotKVProtocolException($"cannot convert {Type} to a double");
            }
        }

        /// <summary>Converts this value to a nullable double, null for a RESP null.</summary>
        public double? AsNullableDouble() => Type == RespType.Null ? (double?)null : AsDouble();

        /// <summary>Converts this value to a boolean: RESP3 booleans, and integers/strings where "1"/"OK"/non-zero mean true.</summary>
        public bool AsBoolean()
        {
            switch (Type)
            {
                case RespType.Boolean:
                    return _int != 0;
                case RespType.Integer:
                    return _int != 0;
                case RespType.SimpleString:
                    return true;
                case RespType.BulkString:
                    var s = AsString();
                    return s == "1";
                case RespType.Null:
                    return false;
                default:
                    throw new HotKVProtocolException($"cannot convert {Type} to a boolean");
            }
        }

        /// <summary>The child items of an Array/Set/Map/Push value.</summary>
        public IReadOnlyList<RespValue> AsArray()
        {
            if (Type == RespType.Null)
            {
                return System.Array.Empty<RespValue>();
            }
            if (!IsAggregate)
            {
                throw new HotKVProtocolException($"cannot convert {Type} to an array");
            }
            return _items!;
        }

        /// <summary>The child items of an Array/Set/Map/Push value, or null for a RESP null.</summary>
        public IReadOnlyList<RespValue>? AsNullableArray() => Type == RespType.Null ? null : AsArray();

        /// <summary>Converts each element of an Array/Set/Map/Push value to a string via <see cref="AsString"/>.</summary>
        public IReadOnlyList<string?> AsStringArray() => AsArray().Select(v => v.AsString()).ToList();

        /// <summary>Converts each element to a non-null string, skipping RESP nulls. Convenient for name-list replies.</summary>
        public IReadOnlyList<string> AsNonNullStringArray() => AsArray().Select(v => v.AsString()).Where(s => s is not null).Select(s => s!).ToList();

        /// <summary>
        /// Reads the child items of a Map value (already flattened) or an Array
        /// value holding "name, value, name, value, ..." pairs, two at a time.
        /// This unifies HotKV's "flat array" reply shape with a true RESP3 map.
        /// </summary>
        public IEnumerable<(RespValue Key, RespValue Value)> AsPairs()
        {
            var items = AsArray();
            for (int i = 0; i + 1 < items.Count; i += 2)
            {
                yield return (items[i], items[i + 1]);
            }
        }

        /// <summary>
        /// Reads a "name keyed collection" reply that some commands (XREAD,
        /// XREADGROUP) send as a true RESP3 map but as an array of
        /// 2-element [name, value] sub-arrays under RESP2. Unlike
        /// <see cref="AsPairs"/> (flat "k, v, k, v, ..." arrays), the RESP2
        /// shape handled here nests each pair in its own sub-array.
        /// </summary>
        public IEnumerable<(RespValue Key, RespValue Value)> AsMapOrPairArray()
        {
            if (Type == RespType.Map)
            {
                foreach (var pair in AsPairs())
                {
                    yield return pair;
                }
                yield break;
            }
            foreach (var item in AsArray())
            {
                var pair = item.AsArray();
                yield return (pair[0], pair[1]);
            }
        }

        /// <summary>
        /// Reads a paired reply that has no native RESP3 map form but still
        /// upgrades from RESP2's flat "member, value, member, value, ..."
        /// array to RESP3's array of 2-element [member, value] sub-arrays
        /// (e.g. ZRANGE ... WITHSCORES, ZPOPMIN/ZPOPMAX, ZRANDMEMBER ...
        /// WITHSCORES, HRANDFIELD ... WITHVALUES). Unlike
        /// <see cref="AsMapOrPairArray"/>, there is no Map-typed reply to
        /// check for here; the two shapes are told apart by whether the
        /// first element is itself an aggregate value.
        /// </summary>
        public IEnumerable<(RespValue Key, RespValue Value)> AsPairedArray()
        {
            var items = AsArray();
            if (items.Count > 0 && items[0].IsAggregate)
            {
                foreach (var item in items)
                {
                    var pair = item.AsArray();
                    yield return (pair[0], pair[1]);
                }
                yield break;
            }
            for (int i = 0; i + 1 < items.Count; i += 2)
            {
                yield return (items[i], items[i + 1]);
            }
        }

        /// <summary>Reads flat name/value pairs as a string keyed dictionary.</summary>
        public IReadOnlyDictionary<string, RespValue> AsDictionary()
        {
            var dict = new Dictionary<string, RespValue>();
            foreach (var (key, value) in AsPairs())
            {
                var k = key.AsString();
                if (k is not null)
                {
                    dict[k] = value;
                }
            }
            return dict;
        }

        internal static string FormatDouble(double value)
        {
            if (double.IsPositiveInfinity(value))
            {
                return "inf";
            }
            if (double.IsNegativeInfinity(value))
            {
                return "-inf";
            }
            if (double.IsNaN(value))
            {
                return "nan";
            }
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            try
            {
                return Type + ":" + (AsString() ?? "(null)");
            }
            catch (HotKVProtocolException)
            {
                return Type.ToString();
            }
        }
    }
}
