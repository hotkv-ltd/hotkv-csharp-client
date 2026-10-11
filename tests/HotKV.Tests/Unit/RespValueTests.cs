// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using HotKV.Connection;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class RespValueTests
    {
        [Fact]
        public void IntegerCoercesToStringAndDouble()
        {
            var v = RespValue.Integer(42);
            Assert.Equal("42", v.AsString());
            Assert.Equal(42.0, v.AsDouble());
            Assert.True(v.AsBoolean());
        }

        [Fact]
        public void ZeroIntegerIsFalseBoolean()
        {
            Assert.False(RespValue.Integer(0).AsBoolean());
        }

        [Fact]
        public void BulkStringParsesAsDoubleAndInt()
        {
            var v = RespValue.Bulk(System.Text.Encoding.UTF8.GetBytes("3.5"));
            Assert.Equal(3.5, v.AsDouble());
        }

        [Fact]
        public void NullValueAsStringAndBytesAreNull()
        {
            Assert.Null(RespValue.Null.AsString());
            Assert.Null(RespValue.Null.AsBytes());
            Assert.Null(RespValue.Null.AsNullableInt64());
            Assert.Null(RespValue.Null.AsNullableDouble());
            Assert.False(RespValue.Null.AsBoolean());
        }

        [Fact]
        public void AsPairsReadsFlatArrayTwoAtATime()
        {
            var array = RespValue.Array(new[]
            {
                RespValue.Simple("a"), RespValue.Integer(1),
                RespValue.Simple("b"), RespValue.Integer(2),
            });
            var pairs = new System.Collections.Generic.List<(string, long)>();
            foreach (var (k, v) in array.AsPairs())
            {
                pairs.Add((k.AsString()!, v.AsInt64()));
            }
            Assert.Equal(2, pairs.Count);
            Assert.Equal(("a", 1L), pairs[0]);
            Assert.Equal(("b", 2L), pairs[1]);
        }

        [Fact]
        public void AsDictionaryWorksForBothMapAndFlatArrayShapes()
        {
            var flatArray = RespValue.Array(new[] { RespValue.Simple("x"), RespValue.Integer(9) });
            Assert.Equal(9L, flatArray.AsDictionary()["x"].AsInt64());

            var map = RespValue.MapOf(new[] { RespValue.Simple("x"), RespValue.Integer(9) });
            Assert.Equal(9L, map.AsDictionary()["x"].AsInt64());
        }

        [Fact]
        public void SplitErrorSeparatesPrefixFromMessage()
        {
            var (prefix, message) = RespValue.SplitError("WRONGTYPE Operation against a key holding the wrong kind of value");
            Assert.Equal("WRONGTYPE", prefix);
            Assert.Equal("Operation against a key holding the wrong kind of value", message);
        }

        [Fact]
        public void SplitErrorWithNoSpaceReturnsWholeStringAsPrefix()
        {
            var (prefix, message) = RespValue.SplitError("NOOP");
            Assert.Equal("NOOP", prefix);
            Assert.Equal(string.Empty, message);
        }

        [Fact]
        public void FormatDoubleHandlesSpecialValues()
        {
            Assert.Equal("inf", RespValue.FormatDouble(double.PositiveInfinity));
            Assert.Equal("-inf", RespValue.FormatDouble(double.NegativeInfinity));
            Assert.Equal("nan", RespValue.FormatDouble(double.NaN));
        }
    }
}
