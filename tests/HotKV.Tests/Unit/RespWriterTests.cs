// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System.Text;
using HotKV.Connection;
using HotKV.Internal;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class RespWriterTests
    {
        [Fact]
        public void EncodesSimpleCommand()
        {
            var buffer = new ByteBuffer();
            RespWriter.WriteCommand(buffer, new object?[] { "SET", "key", "value" });
            string actual = Encoding.ASCII.GetString(buffer.AsMemory().ToArray());
            Assert.Equal("*3\r\n$3\r\nSET\r\n$3\r\nkey\r\n$5\r\nvalue\r\n", actual);
        }

        [Fact]
        public void EncodesIntegerAndDoubleArgsWithInvariantFormatting()
        {
            var buffer = new ByteBuffer();
            RespWriter.WriteCommand(buffer, new object?[] { "INCRBYFLOAT", "k", 2.5, 7L, -3 });
            string actual = Encoding.ASCII.GetString(buffer.AsMemory().ToArray());
            Assert.Contains("$3\r\n2.5\r\n", actual);
            Assert.Contains("$1\r\n7\r\n", actual);
            Assert.Contains("$2\r\n-3\r\n", actual);
        }

        [Fact]
        public async System.Threading.Tasks.Task EncodesBinaryByteArrayArgumentUnchanged()
        {
            byte[] payload = { 0x00, 0xFF, (byte)'\r', (byte)'\n', 0x10 };
            var buffer = new ByteBuffer();
            RespWriter.WriteCommand(buffer, new object?[] { "SET", "k", payload });

            // Re-parse what we wrote and confirm the payload round trips byte for byte.
            var reader = new RespReader(new System.IO.MemoryStream(buffer.AsMemory().ToArray()));
            var value = await reader.ReadValueAsync();
            var items = value.AsArray();
            Assert.Equal(payload, items[2].AsBytes());
        }

        [Fact]
        public void MultipleQueuedCommandsConcatenateInOneBuffer()
        {
            var buffer = new ByteBuffer();
            RespWriter.WriteCommand(buffer, new object?[] { "PING" });
            RespWriter.WriteCommand(buffer, new object?[] { "PING" });
            string actual = Encoding.ASCII.GetString(buffer.AsMemory().ToArray());
            Assert.Equal("*1\r\n$4\r\nPING\r\n*1\r\n$4\r\nPING\r\n", actual);
        }

        [Fact]
        public void ThrowsOnNullArgument()
        {
            var buffer = new ByteBuffer();
            Assert.Throws<System.ArgumentNullException>(() => RespWriter.WriteCommand(buffer, new object?[] { "SET", null, "v" }));
        }
    }
}
