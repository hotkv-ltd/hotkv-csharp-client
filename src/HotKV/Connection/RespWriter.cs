// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HotKV.Internal;

namespace HotKV.Connection
{
    /// <summary>
    /// Encodes commands into the RESP "array of bulk strings" wire format
    /// clients always use to talk to the server, regardless of whether the
    /// connection negotiated RESP2 or RESP3.
    /// </summary>
    public static class RespWriter
    {
        /// <summary>Appends one command (already flattened into its wire tokens) to <paramref name="buffer"/>.</summary>
        internal static void WriteCommand(ByteBuffer buffer, IReadOnlyList<object?> args)
        {
            // Encode every argument first: an argument that cannot be encoded (a null,
            // NaN, ...) must throw before anything is appended, otherwise a half
            // written command would corrupt the connection's pending send buffer.
            var encoded = new byte[args.Count][];
            for (int i = 0; i < encoded.Length; i++)
            {
                encoded[i] = ToBytes(args[i]);
            }
            buffer.WriteByte((byte)'*');
            buffer.WriteAsciiDigits(args.Count);
            buffer.WriteCrlf();
            for (int i = 0; i < encoded.Length; i++)
            {
                byte[] bytes = encoded[i];
                buffer.WriteByte((byte)'$');
                buffer.WriteAsciiDigits(bytes.Length);
                buffer.WriteCrlf();
                buffer.Write(bytes);
                buffer.WriteCrlf();
            }
        }

        /// <summary>
        /// Throws the exception encoding would throw (null argument, NaN, an infinity where the
        /// command takes none) without encoding anything, so a typed call, a queued pipeline command
        /// or a queued transaction command fails at the call site, before any I/O.
        /// </summary>
        internal static void Validate(IReadOnlyList<object?> args)
        {
            for (int i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case null:
                        throw new ArgumentNullException(nameof(args), "command arguments cannot be null; leave optional arguments out of the call instead");
                    case double d:
                        NumberFormat.Double(d);
                        break;
                    case float f:
                        NumberFormat.Single(f);
                        break;
                }
            }
        }

        /// <summary>Converts one command argument to its wire bytes. Binary safe for byte[] and ReadOnlyMemory{byte}.</summary>
        public static byte[] ToBytes(object? arg)
        {
            switch (arg)
            {
                case null:
                    throw new ArgumentNullException(nameof(arg), "command arguments cannot be null; leave optional arguments out of the call instead");
                case byte[] bytes:
                    return bytes;
                case ReadOnlyMemory<byte> mem:
                    return mem.ToArray();
                case string s:
                    return Encoding.UTF8.GetBytes(s);
                case int i:
                    return Encoding.ASCII.GetBytes(i.ToString(CultureInfo.InvariantCulture));
                case long l:
                    return Encoding.ASCII.GetBytes(l.ToString(CultureInfo.InvariantCulture));
                case ulong ul:
                    return Encoding.ASCII.GetBytes(ul.ToString(CultureInfo.InvariantCulture));
                case double d:
                    return Encoding.ASCII.GetBytes(NumberFormat.Double(d));
                case float f:
                    return Encoding.ASCII.GetBytes(NumberFormat.Single(f));
                case bool b:
                    return Encoding.ASCII.GetBytes(b ? "1" : "0");
                default:
                    return Encoding.UTF8.GetBytes(Convert.ToString(arg, CultureInfo.InvariantCulture) ?? string.Empty);
            }
        }
    }
}
