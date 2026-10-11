// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;

namespace HotKV
{
    /// <summary>
    /// Base type for every exception thrown by the HotKV SDK. Catch this to
    /// handle any HotKV related failure, or catch one of the more specific
    /// subclasses to handle a particular failure mode.
    /// </summary>
    public class HotKVException : Exception
    {
        /// <summary>Creates a new <see cref="HotKVException"/>.</summary>
        public HotKVException(string message) : base(message)
        {
        }

        /// <summary>Creates a new <see cref="HotKVException"/> wrapping an inner exception.</summary>
        public HotKVException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a connection to the server could not be established, or
    /// was lost while a command was in flight.
    /// </summary>
    public class HotKVConnectionException : HotKVException
    {
        public HotKVConnectionException(string message) : base(message)
        {
        }

        public HotKVConnectionException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when a connect, command or pool rental operation exceeds its
    /// configured timeout.
    /// </summary>
    public class HotKVTimeoutException : HotKVException
    {
        public HotKVTimeoutException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Thrown when a reply from the server cannot be parsed as a well formed
    /// RESP2/RESP3 frame, or does not match the shape a command expects.
    /// </summary>
    public class HotKVProtocolException : HotKVException
    {
        public HotKVProtocolException(string message) : base(message)
        {
        }

        public HotKVProtocolException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// Thrown when the server replies with a RESP error. <see cref="Prefix"/>
    /// carries the leading error token (for example "WRONGTYPE", "NOAUTH",
    /// "ERR"), <see cref="RawMessage"/> the full error text without the
    /// leading '-'.
    /// </summary>
    public class HotKVServerException : HotKVException
    {
        /// <summary>The error prefix, e.g. "WRONGTYPE", "NOAUTH", "ERR".</summary>
        public string Prefix { get; }

        /// <summary>The full server error text, without the leading '-' or prefix separator.</summary>
        public string RawMessage { get; }

        public HotKVServerException(string prefix, string rawMessage)
            : base(string.IsNullOrEmpty(prefix) ? rawMessage : prefix + " " + rawMessage)
        {
            Prefix = prefix;
            RawMessage = rawMessage;
        }

        /// <summary>Creates the most specific <see cref="HotKVServerException"/> subclass for a given error prefix.</summary>
        internal static HotKVServerException Create(string prefix, string rawMessage)
        {
            switch (prefix)
            {
                case "NOLICENSE":
                    return new NotLicensedException(prefix, rawMessage);
                case "MOVED":
                case "ASK":
                    return HotKVClusterRedirectException.Parse(prefix, rawMessage);
                case "WRONGTYPE":
                    return new HotKVWrongTypeException(prefix, rawMessage);
                case "NOAUTH":
                case "WRONGPASS":
                    return new HotKVAuthenticationException(prefix, rawMessage);
                default:
                    return new HotKVServerException(prefix, rawMessage);
            }
        }
    }

    /// <summary>
    /// Thrown when a command is rejected because the operation was performed
    /// against a key holding the wrong kind of value (RESP error prefix
    /// "WRONGTYPE").
    /// </summary>
    public class HotKVWrongTypeException : HotKVServerException
    {
        public HotKVWrongTypeException(string prefix, string rawMessage) : base(prefix, rawMessage)
        {
        }
    }

    /// <summary>
    /// Thrown when the server rejects authentication (RESP error prefix
    /// "NOAUTH" or "WRONGPASS").
    /// </summary>
    public class HotKVAuthenticationException : HotKVServerException
    {
        public HotKVAuthenticationException(string prefix, string rawMessage) : base(prefix, rawMessage)
        {
        }
    }

    /// <summary>
    /// Thrown when an enterprise or AI command is
    /// rejected because the server's currently loaded license does not cover
    /// the required feature (RESP error prefix "NOLICENSE"). Tests and
    /// application code can catch this specifically to skip or degrade
    /// gracefully instead of treating it as a hard failure.
    /// </summary>
    public class NotLicensedException : HotKVServerException
    {
        public NotLicensedException(string prefix, string rawMessage) : base(prefix, rawMessage)
        {
        }
    }

    /// <summary>
    /// Thrown when a cluster node replies with a MOVED or ASK redirect.
    /// <see cref="HotKV.Cluster.HotKVCluster"/> catches this internally to follow
    /// redirects; it only escapes to application code when the bounded
    /// redirect count is exceeded or a non-cluster client receives it.
    /// </summary>
    public class HotKVClusterRedirectException : HotKVServerException
    {
        /// <summary>True for an ASK redirect, false for a MOVED redirect.</summary>
        public bool IsAsk { get; }

        /// <summary>The hash slot the key belongs to.</summary>
        public int Slot { get; }

        /// <summary>The "host:port" of the node now owning the slot.</summary>
        public string Endpoint { get; }

        public HotKVClusterRedirectException(string prefix, string rawMessage, bool isAsk, int slot, string endpoint)
            : base(prefix, rawMessage)
        {
            IsAsk = isAsk;
            Slot = slot;
            Endpoint = endpoint;
        }

        internal static HotKVClusterRedirectException Parse(string prefix, string rawMessage)
        {
            // Wire shape: "MOVED <slot> <host>:<port>" / "ASK <slot> <host>:<port>"
            var parts = rawMessage.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            int slot = 0;
            string endpoint = string.Empty;
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out slot);
                endpoint = parts[1];
            }
            return new HotKVClusterRedirectException(prefix, rawMessage, prefix == "ASK", slot, endpoint);
        }
    }
}
