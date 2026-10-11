// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV.Tests.Unit
{
    /// <summary>
    /// A read-only stream over a fixed byte buffer that hands back at most
    /// <see cref="MaxChunk"/> bytes per <see cref="ReadAsync(Memory{byte}, CancellationToken)"/>
    /// call, regardless of how much the caller asked for. Used to prove
    /// <c>RespReader</c> correctly reassembles frames that arrive split
    /// across many small socket reads.
    /// </summary>
    internal sealed class ChunkedStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public int MaxChunk { get; set; } = 1;

        public ChunkedStream(byte[] data)
        {
            _data = data;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int remaining = _data.Length - _position;
            if (remaining <= 0)
            {
                return new ValueTask<int>(0);
            }
            int n = Math.Min(Math.Min(MaxChunk, remaining), buffer.Length);
            new ReadOnlySpan<byte>(_data, _position, n).CopyTo(buffer.Span);
            _position += n;
            return new ValueTask<int>(n);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var task = ReadAsync(new Memory<byte>(buffer, offset, count));
            return task.IsCompleted ? task.Result : task.AsTask().GetAwaiter().GetResult();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
