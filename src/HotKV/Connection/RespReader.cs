// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HotKV.Connection
{
    /// <summary>
    /// Incremental, allocation-conscious RESP2/RESP3 reply parser over a
    /// <see cref="Stream"/>. Understands every RESP3 type (simple string,
    /// error, integer, bulk string, array, null, boolean, double, big
    /// number, blob error, verbatim string, map, set, push, attribute) and
    /// works unchanged when the connection stayed on RESP2, since RESP2
    /// replies use a strict subset of the same framing. Not thread safe:
    /// one reader per connection, read sequentially.
    /// </summary>
    public sealed class RespReader
    {
        private readonly Stream _stream;
        private byte[] _buf;
        private int _start;
        private int _end;

        public RespReader(Stream stream, int initialBufferSize = 8192)
        {
            _stream = stream ?? throw new ArgumentNullException(nameof(stream));
            _buf = new byte[Math.Max(256, initialBufferSize)];
        }

        /// <summary>
        /// Reads and returns the next top level RESP value from the stream.
        /// RESP3 attribute frames are parsed and discarded transparently:
        /// the value that follows the attribute is returned instead.
        /// </summary>
        public async ValueTask<RespValue> ReadValueAsync(CancellationToken cancellationToken = default)
        {
            byte marker = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            switch (marker)
            {
                case (byte)'+':
                    return RespValue.Simple(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
                case (byte)'-':
                    return RespValue.Error(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
                case (byte)':':
                    return RespValue.Integer(ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false)));
                case (byte)'$':
                    return await ReadBulkStringAsync(cancellationToken).ConfigureAwait(false);
                case (byte)'*':
                    return await ReadAggregateAsync(RespType.Array, cancellationToken).ConfigureAwait(false);
                case (byte)'_':
                    await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    return RespValue.Null;
                case (byte)'#':
                {
                    var s = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
                    if (s != "t" && s != "f")
                    {
                        throw new HotKVProtocolException($"malformed RESP boolean: '{s}'");
                    }
                    return RespValue.Boolean(s == "t");
                }
                case (byte)',':
                    return RespValue.Double(ParseRespDouble(await ReadLineAsync(cancellationToken).ConfigureAwait(false)));
                case (byte)'(':
                    return RespValue.BigNumber(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
                case (byte)'!':
                    return await ReadBlobErrorAsync(cancellationToken).ConfigureAwait(false);
                case (byte)'=':
                    return await ReadVerbatimStringAsync(cancellationToken).ConfigureAwait(false);
                case (byte)'%':
                    return await ReadMapAsync(cancellationToken).ConfigureAwait(false);
                case (byte)'~':
                    return await ReadAggregateAsync(RespType.Set, cancellationToken).ConfigureAwait(false);
                case (byte)'>':
                    return await ReadAggregateAsync(RespType.Push, cancellationToken).ConfigureAwait(false);
                case (byte)'|':
                    // Attribute frame: read and discard its pairs, then transparently
                    // return the value it decorates.
                    await ReadMapAsync(cancellationToken).ConfigureAwait(false);
                    return await ReadValueAsync(cancellationToken).ConfigureAwait(false);
                default:
                    throw new HotKVProtocolException($"unexpected RESP type byte 0x{marker:x2} ('{(char)marker}')");
            }
        }

        private async ValueTask<RespValue> ReadBulkStringAsync(CancellationToken cancellationToken)
        {
            long len = ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
            if (len < 0)
            {
                return RespValue.Null;
            }
            var bytes = await ReadExactAsync((int)len, cancellationToken).ConfigureAwait(false);
            return RespValue.Bulk(bytes);
        }

        private async ValueTask<RespValue> ReadBlobErrorAsync(CancellationToken cancellationToken)
        {
            long len = ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
            var bytes = await ReadExactAsync((int)len, cancellationToken).ConfigureAwait(false);
            return RespValue.BlobError(Encoding.UTF8.GetString(bytes));
        }

        private async ValueTask<RespValue> ReadVerbatimStringAsync(CancellationToken cancellationToken)
        {
            long len = ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
            var bytes = await ReadExactAsync((int)len, cancellationToken).ConfigureAwait(false);
            if (bytes.Length < 4 || bytes[3] != (byte)':')
            {
                throw new HotKVProtocolException("malformed RESP3 verbatim string: missing 3 character format prefix");
            }
            string format = Encoding.ASCII.GetString(bytes, 0, 3);
            var content = new byte[bytes.Length - 4];
            Array.Copy(bytes, 4, content, 0, content.Length);
            return RespValue.Verbatim(format, content);
        }

        private async ValueTask<RespValue> ReadAggregateAsync(RespType type, CancellationToken cancellationToken)
        {
            long count = ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
            if (count < 0)
            {
                return RespValue.Null;
            }
            var items = new RespValue[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = await ReadValueAsync(cancellationToken).ConfigureAwait(false);
            }
            return type switch
            {
                RespType.Set => RespValue.SetOf(items),
                RespType.Push => RespValue.PushOf(items),
                _ => RespValue.Array(items),
            };
        }

        private async ValueTask<RespValue> ReadMapAsync(CancellationToken cancellationToken)
        {
            long pairs = ParseInt64(await ReadLineAsync(cancellationToken).ConfigureAwait(false));
            if (pairs < 0)
            {
                return RespValue.Null;
            }
            var items = new RespValue[pairs * 2];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = await ReadValueAsync(cancellationToken).ConfigureAwait(false);
            }
            return RespValue.MapOf(items);
        }

        internal static long ParseInt64(string s)
        {
            if (!long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
            {
                throw new HotKVProtocolException($"malformed RESP integer: '{s}'");
            }
            return value;
        }

        internal static double ParseRespDouble(string s)
        {
            if (string.Equals(s, "inf", StringComparison.Ordinal) || string.Equals(s, "+inf", StringComparison.Ordinal))
            {
                return double.PositiveInfinity;
            }
            if (string.Equals(s, "-inf", StringComparison.Ordinal))
            {
                return double.NegativeInfinity;
            }
            if (string.Equals(s, "nan", StringComparison.Ordinal))
            {
                return double.NaN;
            }
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                throw new HotKVProtocolException($"malformed RESP double: '{s}'");
            }
            return value;
        }

        private async ValueTask<byte> ReadByteAsync(CancellationToken cancellationToken)
        {
            if (_start >= _end)
            {
                await FillMoreAsync(cancellationToken).ConfigureAwait(false);
            }
            return _buf[_start++];
        }

        private async ValueTask<string> ReadLineAsync(CancellationToken cancellationToken)
        {
            int idx = FindCrlf();
            while (idx < 0)
            {
                await FillMoreAsync(cancellationToken).ConfigureAwait(false);
                idx = FindCrlf();
            }
            string s = Encoding.UTF8.GetString(_buf, _start, idx - _start);
            _start = idx + 2;
            return s;
        }

        private async ValueTask<byte[]> ReadExactAsync(int n, CancellationToken cancellationToken)
        {
            if (n < 0)
            {
                throw new HotKVProtocolException($"malformed RESP length: {n}");
            }
            var result = new byte[n];
            int copied = 0;
            while (copied < n)
            {
                if (_start >= _end)
                {
                    await FillMoreAsync(cancellationToken).ConfigureAwait(false);
                }
                int avail = Math.Min(_end - _start, n - copied);
                Array.Copy(_buf, _start, result, copied, avail);
                _start += avail;
                copied += avail;
            }
            // Every bulk-style payload is followed by a trailing CRLF.
            byte cr = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            byte lf = await ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (cr != (byte)'\r' || lf != (byte)'\n')
            {
                throw new HotKVProtocolException("malformed RESP frame: expected trailing CRLF after payload");
            }
            return result;
        }

        private int FindCrlf()
        {
            for (int i = _start; i < _end - 1; i++)
            {
                if (_buf[i] == (byte)'\r' && _buf[i + 1] == (byte)'\n')
                {
                    return i;
                }
            }
            return -1;
        }

        private async ValueTask FillMoreAsync(CancellationToken cancellationToken)
        {
            if (_start > 0 && _end == _buf.Length)
            {
                Array.Copy(_buf, _start, _buf, 0, _end - _start);
                _end -= _start;
                _start = 0;
            }
            if (_end == _buf.Length)
            {
                var bigger = new byte[_buf.Length * 2];
                Array.Copy(_buf, 0, bigger, 0, _end);
                _buf = bigger;
            }
            int n = await _stream.ReadAsync(_buf.AsMemory(_end, _buf.Length - _end), cancellationToken).ConfigureAwait(false);
            if (n == 0)
            {
                throw new HotKVConnectionException("connection closed by the server while reading a reply");
            }
            _end += n;
        }
    }
}
