// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace HotKV
{
    /// <summary>
    /// Connection and pooling options for a <see cref="HotKVClient"/>.
    /// Build one directly, or parse a connection URL with <see cref="Parse"/>.
    /// </summary>
    public sealed class HotKVClientOptions
    {
        /// <summary>Server host name or IP address. Ignored when <see cref="UnixSocketPath"/> is set.</summary>
        public string Host { get; set; } = "127.0.0.1";

        /// <summary>Server port. Ignored when <see cref="UnixSocketPath"/> is set.</summary>
        public int Port { get; set; } = 6379;

        /// <summary>Path to a Unix domain socket to connect through instead of TCP, where the platform supports it.</summary>
        public string? UnixSocketPath { get; set; }

        /// <summary>Username for the HELLO/AUTH handshake, or null for password only authentication.</summary>
        public string? Username { get; set; }

        /// <summary>Password for the HELLO/AUTH handshake, or null when the server requires no authentication.</summary>
        public string? Password { get; set; }

        /// <summary>Logical database index selected with SELECT after connecting.</summary>
        public int Database { get; set; }

        /// <summary>Connection name announced via HELLO/CLIENT SETNAME, or null.</summary>
        public string? ClientName { get; set; }

        /// <summary>Timeout for establishing a new TCP/TLS connection and completing the handshake.</summary>
        public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(5);

        /// <summary>Timeout applied to each command awaiting a reply.</summary>
        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(10);

        /// <summary>TLS settings. <see cref="HotKVTlsOptions.Enabled"/> is set automatically by the "hotkvs://"/"rediss://" schemes.</summary>
        public HotKVTlsOptions Tls { get; set; } = new HotKVTlsOptions();

        /// <summary>Maximum number of pooled connections. Rentals block (up to <see cref="ConnectTimeout"/>) once the pool is exhausted.</summary>
        public int MaxPoolSize { get; set; } = 10;

        /// <summary>Connections idle in the pool longer than this are health checked with PING before reuse, and replaced if they fail.</summary>
        public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>Attempts HELLO 3 first, falling back to RESP2 when the server rejects it. Set false to force RESP2.</summary>
        public bool PreferResp3 { get; set; } = true;

        /// <summary>Reconnects and retries once, automatically, when an idempotent read fails because the connection was lost. Writes are never retried automatically.</summary>
        public bool RetryOnConnectionLoss { get; set; } = true;

        /// <summary>
        /// Extra commands sent, each expected to reply OK, right after the
        /// normal HELLO/AUTH/SELECT handshake completes on every new
        /// connection. <see cref="HotKV.Cluster.HotKVCluster"/> uses this to send
        /// READONLY on connections it opens to a replica.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<object?>>? PostConnectCommands { get; set; }

        /// <summary>Creates a shallow copy suitable for handing to a new connection or client.</summary>
        public HotKVClientOptions Clone() => new HotKVClientOptions
        {
            Host = Host,
            Port = Port,
            UnixSocketPath = UnixSocketPath,
            Username = Username,
            Password = Password,
            Database = Database,
            ClientName = ClientName,
            ConnectTimeout = ConnectTimeout,
            CommandTimeout = CommandTimeout,
            Tls = Tls.Clone(),
            MaxPoolSize = MaxPoolSize,
            IdleTimeout = IdleTimeout,
            PreferResp3 = PreferResp3,
            RetryOnConnectionLoss = RetryOnConnectionLoss,
            PostConnectCommands = PostConnectCommands,
        };

        /// <summary>
        /// Parses a connection URL of the form
        /// "hotkv://[user[:password]@]host[:port][/db][?param=value&amp;...]".
        /// "hotkvs://" and "rediss://" enable TLS; "redis://"/"rediss://" are
        /// accepted as aliases for drop-in migration.
        /// Recognised query parameters: clientName, connectTimeoutMs,
        /// commandTimeoutMs, poolSize, idleTimeoutMs, resp3 (true/false),
        /// tls (true/false), insecureSkipVerify (true/false), caFile,
        /// certFile, keyFile, certPassword, serverName.
        /// </summary>
        public static HotKVClientOptions Parse(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                throw new ArgumentException("connection URL must not be empty", nameof(url));
            }

            Uri uri;
            try
            {
                uri = new Uri(url, UriKind.Absolute);
            }
            catch (UriFormatException ex)
            {
                throw new ArgumentException($"invalid HotKV connection URL: '{url}'", nameof(url), ex);
            }

            var options = new HotKVClientOptions();

            bool tls;
            switch (uri.Scheme.ToLowerInvariant())
            {
                case "hotkv":
                case "redis":
                    tls = false;
                    break;
                case "hotkvs":
                case "rediss":
                    tls = true;
                    break;
                case "unix":
                    options.UnixSocketPath = uri.AbsolutePath;
                    tls = false;
                    ApplyUserInfoAndQuery(options, uri, tls);
                    return options;
                default:
                    throw new ArgumentException(
                        $"unsupported URL scheme '{uri.Scheme}': expected hotkv, hotkvs, redis, rediss or unix", nameof(url));
            }

            options.Host = uri.IsDefaultPort && uri.Host.Length == 0 ? "127.0.0.1" : uri.Host;
            if (!uri.IsDefaultPort || uri.Port > 0)
            {
                options.Port = uri.Port > 0 ? uri.Port : options.Port;
            }

            if (uri.AbsolutePath.Length > 1)
            {
                string dbPart = uri.AbsolutePath.TrimStart('/');
                int slash = dbPart.IndexOf('/');
                if (slash >= 0)
                {
                    dbPart = dbPart.Substring(0, slash);
                }
                if (dbPart.Length > 0)
                {
                    if (!int.TryParse(dbPart, NumberStyles.None, CultureInfo.InvariantCulture, out int db))
                    {
                        throw new ArgumentException($"invalid database index in URL path: '{dbPart}'", nameof(url));
                    }
                    options.Database = db;
                }
            }

            ApplyUserInfoAndQuery(options, uri, tls);
            return options;
        }

        private static void ApplyUserInfoAndQuery(HotKVClientOptions options, Uri uri, bool tls)
        {
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                // Split on the first literal ':' BEFORE percent-decoding, so a user name or password
                // containing an encoded colon (%3A) keeps it.
                string userInfo = uri.UserInfo;
                int colon = userInfo.IndexOf(':');
                if (colon >= 0)
                {
                    string user = Uri.UnescapeDataString(userInfo.Substring(0, colon));
                    options.Username = user.Length > 0 ? user : null;
                    options.Password = Uri.UnescapeDataString(userInfo.Substring(colon + 1));
                }
                else
                {
                    options.Password = Uri.UnescapeDataString(userInfo);
                }
            }

            options.Tls.Enabled = tls;

            foreach (var (key, value) in ParseQuery(uri.Query))
            {
                switch (key.ToLowerInvariant())
                {
                    case "clientname":
                        options.ClientName = value;
                        break;
                    case "username":
                        options.Username = value;
                        break;
                    case "password":
                        options.Password = value;
                        break;
                    case "db":
                    case "database":
                        options.Database = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "connecttimeoutms":
                        options.ConnectTimeout = TimeSpan.FromMilliseconds(double.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case "commandtimeoutms":
                        options.CommandTimeout = TimeSpan.FromMilliseconds(double.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case "poolsize":
                        options.MaxPoolSize = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "idletimeoutms":
                        options.IdleTimeout = TimeSpan.FromMilliseconds(double.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case "resp3":
                        options.PreferResp3 = ParseBool(value);
                        break;
                    case "retryreads":
                        options.RetryOnConnectionLoss = ParseBool(value);
                        break;
                    case "tls":
                        options.Tls.Enabled = ParseBool(value);
                        break;
                    case "insecureskipverify":
                        options.Tls.InsecureSkipVerify = ParseBool(value);
                        break;
                    case "cafile":
                        options.Tls.CaCertificatePath = value;
                        break;
                    case "certfile":
                        options.Tls.ClientCertificatePath = value;
                        break;
                    case "keyfile":
                        options.Tls.ClientCertificateKeyPath = value;
                        break;
                    case "certpassword":
                        options.Tls.ClientCertificatePassword = value;
                        break;
                    case "servername":
                        options.Tls.ServerName = value;
                        break;
                }
            }
        }

        private static bool ParseBool(string value) =>
            value == "1" || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<(string Key, string Value)> ParseQuery(string query)
        {
            if (string.IsNullOrEmpty(query))
            {
                yield break;
            }
            string q = query.StartsWith("?", StringComparison.Ordinal) ? query.Substring(1) : query;
            foreach (var part in q.Split('&'))
            {
                if (part.Length == 0)
                {
                    continue;
                }
                int eq = part.IndexOf('=');
                string key = eq >= 0 ? part.Substring(0, eq) : part;
                string value = eq >= 0 ? Uri.UnescapeDataString(part.Substring(eq + 1)) : string.Empty;
                yield return (Uri.UnescapeDataString(key), value);
            }
        }
    }
}
