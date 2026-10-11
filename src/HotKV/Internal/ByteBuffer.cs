// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;

namespace HotKV.Internal
{
    /// <summary>
    /// A small growable byte buffer used to assemble outgoing RESP frames
    /// (one, or many when pipelining) before a single write to the socket.
    /// Not thread safe.
    /// </summary>
    internal sealed class ByteBuffer
    {
        private byte[] _buf;
        private int _len;

        public ByteBuffer(int capacity = 256)
        {
            _buf = new byte[Math.Max(64, capacity)];
        }

        public int Length => _len;

        public byte[] Underlying => _buf;

        public void Reset() => _len = 0;

        public void WriteByte(byte b)
        {
            EnsureCapacity(1);
            _buf[_len++] = b;
        }

        public void Write(byte[] data)
        {
            Write(data, 0, data.Length);
        }

        public void Write(byte[] data, int offset, int count)
        {
            EnsureCapacity(count);
            Array.Copy(data, offset, _buf, _len, count);
            _len += count;
        }

        public void WriteAsciiDigits(long value)
        {
            // Fast path for non-negative small numbers used for RESP counts and
            // bulk lengths; falls back to invariant formatting for the rest.
            WriteAscii(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        public void WriteAscii(string s)
        {
            EnsureCapacity(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                _buf[_len++] = (byte)s[i];
            }
        }

        public void WriteCrlf()
        {
            EnsureCapacity(2);
            _buf[_len++] = (byte)'\r';
            _buf[_len++] = (byte)'\n';
        }

        public ReadOnlyMemory<byte> AsMemory() => new ReadOnlyMemory<byte>(_buf, 0, _len);

        private void EnsureCapacity(int extra)
        {
            if (_len + extra <= _buf.Length)
            {
                return;
            }
            int newSize = _buf.Length * 2;
            while (newSize < _len + extra)
            {
                newSize *= 2;
            }
            var bigger = new byte[newSize];
            Array.Copy(_buf, bigger, _len);
            _buf = bigger;
        }
    }
}
