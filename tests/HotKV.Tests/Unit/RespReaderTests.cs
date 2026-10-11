// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using HotKV.Connection;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class RespReaderTests
    {
        private static RespReader ReaderFor(byte[] bytes) => new RespReader(new MemoryStream(bytes));

        private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

        [Fact]
        public async Task ReadsSimpleString()
        {
            var reader = ReaderFor(Ascii("+OK\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.SimpleString, value.Type);
            Assert.Equal("OK", value.AsString());
        }

        [Fact]
        public async Task ReadsError()
        {
            var reader = ReaderFor(Ascii("-ERR something bad\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.True(value.IsError);
            Assert.Equal("ERR something bad", value.ErrorText);
            var (prefix, message) = RespValue.SplitError(value.ErrorText);
            Assert.Equal("ERR", prefix);
            Assert.Equal("something bad", message);
        }

        [Theory]
        [InlineData(":1000\r\n", 1000L)]
        [InlineData(":-42\r\n", -42L)]
        [InlineData(":0\r\n", 0L)]
        public async Task ReadsInteger(string frame, long expected)
        {
            var reader = ReaderFor(Ascii(frame));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Integer, value.Type);
            Assert.Equal(expected, value.AsInt64());
        }

        [Fact]
        public async Task ReadsBulkString()
        {
            var reader = ReaderFor(Ascii("$5\r\nhello\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.BulkString, value.Type);
            Assert.Equal("hello", value.AsString());
        }

        [Fact]
        public async Task ReadsBinaryBulkStringWithEmbeddedCrlfAndNulBytes()
        {
            byte[] payload = { 0x00, 0x01, (byte)'\r', (byte)'\n', 0xFF, 0x02 };
            byte[] frame = BuildBulk(payload);
            var reader = ReaderFor(frame);
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.BulkString, value.Type);
            Assert.Equal(payload, value.AsBytes());
        }

        private static byte[] BuildBulk(byte[] payload)
        {
            using var ms = new MemoryStream();
            var header = Encoding.ASCII.GetBytes("$" + payload.Length + "\r\n");
            ms.Write(header, 0, header.Length);
            ms.Write(payload, 0, payload.Length);
            var trailer = Encoding.ASCII.GetBytes("\r\n");
            ms.Write(trailer, 0, trailer.Length);
            return ms.ToArray();
        }

        [Fact]
        public async Task ReadsNullBulkStringAsNull()
        {
            var reader = ReaderFor(Ascii("$-1\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.True(value.IsNull);
            Assert.Null(value.AsString());
        }

        [Fact]
        public async Task ReadsNullArrayAsNull()
        {
            var reader = ReaderFor(Ascii("*-1\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.True(value.IsNull);
        }

        [Fact]
        public async Task ReadsResp3Null()
        {
            var reader = ReaderFor(Ascii("_\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.True(value.IsNull);
        }

        [Fact]
        public async Task ReadsArray()
        {
            var reader = ReaderFor(Ascii("*2\r\n$3\r\nfoo\r\n$3\r\nbar\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Array, value.Type);
            var items = value.AsArray();
            Assert.Equal(2, items.Count);
            Assert.Equal("foo", items[0].AsString());
            Assert.Equal("bar", items[1].AsString());
        }

        [Fact]
        public async Task ReadsNestedArray()
        {
            var reader = ReaderFor(Ascii("*2\r\n*2\r\n:1\r\n:2\r\n$1\r\nx\r\n"));
            var value = await reader.ReadValueAsync();
            var items = value.AsArray();
            Assert.Equal(2, items.Count);
            var inner = items[0].AsArray();
            Assert.Equal(1L, inner[0].AsInt64());
            Assert.Equal(2L, inner[1].AsInt64());
            Assert.Equal("x", items[1].AsString());
        }

        [Theory]
        [InlineData("#t\r\n", true)]
        [InlineData("#f\r\n", false)]
        public async Task ReadsBoolean(string frame, bool expected)
        {
            var reader = ReaderFor(Ascii(frame));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Boolean, value.Type);
            Assert.Equal(expected, value.AsBoolean());
        }

        [Fact]
        public async Task ReadsDouble()
        {
            var reader = ReaderFor(Ascii(",3.14\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Double, value.Type);
            Assert.Equal(3.14, value.AsDouble(), 10);
        }

        [Fact]
        public async Task ReadsDoubleInfinityAndNan()
        {
            Assert.True(double.IsPositiveInfinity((await ReaderFor(Ascii(",inf\r\n")).ReadValueAsync()).AsDouble()));
            Assert.True(double.IsNegativeInfinity((await ReaderFor(Ascii(",-inf\r\n")).ReadValueAsync()).AsDouble()));
            Assert.True(double.IsNaN((await ReaderFor(Ascii(",nan\r\n")).ReadValueAsync()).AsDouble()));
        }

        [Fact]
        public async Task ReadsBigNumber()
        {
            const string digits = "3492890328409238509324850943850943825024385";
            var reader = ReaderFor(Ascii("(" + digits + "\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.BigNumber, value.Type);
            Assert.Equal(digits, value.AsString());
        }

        [Fact]
        public async Task ReadsBlobError()
        {
            var reader = ReaderFor(Ascii("!21\r\nSYNTAX invalid syntax\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.True(value.IsError);
            Assert.Equal("SYNTAX invalid syntax", value.ErrorText);
        }

        [Fact]
        public async Task ReadsVerbatimString()
        {
            var reader = ReaderFor(Ascii("=15\r\ntxt:Some string\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.VerbatimString, value.Type);
            Assert.Equal("txt", value.VerbatimFormat);
            Assert.Equal("Some string", value.AsString());
        }

        [Fact]
        public async Task ReadsMapAsFlatPairsAndDictionary()
        {
            var reader = ReaderFor(Ascii("%2\r\n+key1\r\n:1\r\n+key2\r\n:2\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Map, value.Type);
            var dict = value.AsDictionary();
            Assert.Equal(2, dict.Count);
            Assert.Equal(1L, dict["key1"].AsInt64());
            Assert.Equal(2L, dict["key2"].AsInt64());
        }

        [Fact]
        public async Task ReadsSet()
        {
            var reader = ReaderFor(Ascii("~2\r\n+a\r\n+b\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Set, value.Type);
            Assert.Equal(2, value.AsArray().Count);
        }

        [Fact]
        public async Task ReadsPush()
        {
            var reader = ReaderFor(Ascii(">2\r\n+message\r\n+hello\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.Push, value.Type);
            Assert.Equal("message", value.AsArray()[0].AsString());
        }

        [Fact]
        public async Task AttributeFrameIsTransparentlySkipped()
        {
            // An attribute decorates the value that follows it; the reader
            // should return that following value, not the attribute itself.
            var reader = ReaderFor(Ascii("|1\r\n+key\r\n+val\r\n+OK\r\n"));
            var value = await reader.ReadValueAsync();
            Assert.Equal(RespType.SimpleString, value.Type);
            Assert.Equal("OK", value.AsString());
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        public async Task ReassemblesFramesSplitAcrossManySmallReads(int chunkSize)
        {
            byte[] frame = Ascii("*3\r\n$3\r\nfoo\r\n:42\r\n$5\r\nhello\r\n");
            var stream = new ChunkedStream(frame) { MaxChunk = chunkSize };
            var reader = new RespReader(stream, initialBufferSize: 4);

            var value = await reader.ReadValueAsync();
            var items = value.AsArray();
            Assert.Equal(3, items.Count);
            Assert.Equal("foo", items[0].AsString());
            Assert.Equal(42L, items[1].AsInt64());
            Assert.Equal("hello", items[2].AsString());
        }

        [Fact]
        public async Task ReassemblesLargeBinaryBulkStringAcrossSmallReads()
        {
            var payload = new byte[5000];
            new Random(42).NextBytes(payload);
            byte[] frame = BuildBulk(payload);
            var stream = new ChunkedStream(frame) { MaxChunk = 3 };
            var reader = new RespReader(stream, initialBufferSize: 8);

            var value = await reader.ReadValueAsync();
            Assert.Equal(payload, value.AsBytes());
        }

        [Fact]
        public async Task ThrowsProtocolExceptionOnUnknownTypeByte()
        {
            var reader = ReaderFor(Ascii("?nope\r\n"));
            await Assert.ThrowsAsync<HotKVProtocolException>(() => reader.ReadValueAsync().AsTask());
        }

        [Fact]
        public async Task ThrowsConnectionExceptionOnUnexpectedEof()
        {
            var reader = ReaderFor(Ascii("$5\r\nhel"));
            await Assert.ThrowsAsync<HotKVConnectionException>(() => reader.ReadValueAsync().AsTask());
        }
    }
}
