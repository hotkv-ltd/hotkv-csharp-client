// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using HotKV.Internal;

namespace HotKV.Connection
{
    /// <summary>
    /// A single physical connection to a HotKV server: socket/TLS transport,
    /// HELLO/AUTH handshake, and the raw RESP read/write primitives that
    /// higher level pieces (the pooled command path, pipelines,
    /// transactions, pub/sub) build on. Not safe for concurrent use by more
    /// than one logical caller at a time; the connection pool and the
    /// pub/sub subscriber each enforce that on their own.
    /// </summary>
    public sealed class HotKVConnection : IAsyncDisposable, IDisposable
    {
        private readonly Stream _stream;
        private readonly TcpClient? _tcpClient;
        private readonly Socket? _unixSocket;
        private readonly ByteBuffer _sendBuffer = new ByteBuffer(512);
        private bool _disposed;

        /// <summary>The raw RESP reader for this connection. Used directly by the pub/sub subscriber loop.</summary>
        public RespReader Reader { get; }

        /// <summary>Negotiated protocol version: 2 or 3.</summary>
        public int ProtocolVersion { get; private set; } = 2;

        /// <summary>UTC time this connection was created.</summary>
        public DateTime CreatedUtc { get; } = DateTime.UtcNow;

        /// <summary>UTC time this connection last completed a round trip. Used by the pool to decide when to health check on borrow.</summary>
        public DateTime LastUsedUtc { get; internal set; } = DateTime.UtcNow;

        /// <summary>Raised for every RESP3 push frame observed by <see cref="ReadReplyAsync"/> while waiting for a command reply.</summary>
        public event Action<RespValue>? OnPush;

        private HotKVConnection(Stream stream, TcpClient? tcpClient, Socket? unixSocket)
        {
            _stream = stream;
            _tcpClient = tcpClient;
            _unixSocket = unixSocket;
            Reader = new RespReader(stream);
        }

        /// <summary>Opens a transport connection and completes the HELLO/AUTH handshake per <paramref name="options"/>.</summary>
        public static async Task<HotKVConnection> ConnectAsync(HotKVClientOptions options, CancellationToken cancellationToken = default)
        {
            if (options is null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(options.ConnectTimeout);

            Stream stream;
            TcpClient? tcpClient = null;
            Socket? unixSocket = null;
            try
            {
                if (!string.IsNullOrEmpty(options.UnixSocketPath))
                {
                    unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await unixSocket.ConnectAsync(new UnixDomainSocketEndPoint(options.UnixSocketPath!)).WithCancellation(connectCts.Token).ConfigureAwait(false);
                    }
                    catch (PlatformNotSupportedException ex)
                    {
                        throw new HotKVConnectionException("Unix domain sockets are not supported on this platform", ex);
                    }
                    stream = new NetworkStream(unixSocket, ownsSocket: true);
                }
                else
                {
                    tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(options.Host, options.Port).WithCancellation(connectCts.Token).ConfigureAwait(false);
                    tcpClient.NoDelay = true;
                    stream = tcpClient.GetStream();
                }

                if (options.Tls.Enabled)
                {
                    stream = await WrapTlsAsync(stream, options, connectCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested == false)
            {
                tcpClient?.Dispose();
                unixSocket?.Dispose();
                throw new HotKVTimeoutException($"connecting to {DescribeEndpoint(options)} timed out after {options.ConnectTimeout}");
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                tcpClient?.Dispose();
                unixSocket?.Dispose();
                throw new HotKVConnectionException($"failed to connect to {DescribeEndpoint(options)}: {ex.Message}", ex);
            }

            var connection = new HotKVConnection(stream, tcpClient, unixSocket);
            try
            {
                await connection.HandshakeAsync(options, connectCts.Token).ConfigureAwait(false);
            }
            catch
            {
                connection.Dispose();
                throw;
            }
            return connection;
        }

        private static string DescribeEndpoint(HotKVClientOptions options) =>
            options.UnixSocketPath is { Length: > 0 } path ? $"unix:{path}" : $"{options.Host}:{options.Port}";

        private static async Task<Stream> WrapTlsAsync(Stream inner, HotKVClientOptions options, CancellationToken cancellationToken)
        {
            var tls = options.Tls;
            X509Certificate2Collection? caCerts = string.IsNullOrEmpty(tls.CaCertificatePath) ? null : LoadCaCertificates(tls.CaCertificatePath!);

            RemoteCertificateValidationCallback validation = (_, certificate, chain, errors) =>
                ValidateServerCertificate(tls.InsecureSkipVerify, caCerts, certificate, chain, errors);

            var sslStream = new SslStream(inner, leaveInnerStreamOpen: false, validation);
            string targetHost = tls.ServerName ?? options.Host;

            var clientCertificates = new X509CertificateCollection();
            if (!string.IsNullOrEmpty(tls.ClientCertificatePath))
            {
                clientCertificates.Add(LoadClientCertificate(tls));
            }

            try
            {
                await sslStream.AuthenticateAsClientAsync(
                    targetHost,
                    clientCertificates,
                    SslProtocols.None,
                    checkCertificateRevocation: false).WithCancellation(cancellationToken).ConfigureAwait(false);
            }
            catch (AuthenticationException ex)
            {
                throw new HotKVConnectionException($"TLS handshake with {targetHost} failed: {ex.Message}", ex);
            }

            return sslStream;
        }

        /// <summary>
        /// Decides whether to trust the server certificate. The host name is always checked (a certificate for another
        /// name is rejected even when it is signed by the configured CA); with a CA file configured, the chain must
        /// end at one of that file's certificates (the system trust store is not consulted) and must be otherwise
        /// valid (not expired, signatures good).
        /// </summary>
        internal static bool ValidateServerCertificate(bool insecureSkipVerify, X509Certificate2Collection? caCerts, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
        {
            if (insecureSkipVerify)
            {
                return true;
            }
            if (certificate is null || (errors & SslPolicyErrors.RemoteCertificateNotAvailable) != 0)
            {
                return false;
            }
            if ((errors & SslPolicyErrors.RemoteCertificateNameMismatch) != 0)
            {
                return false;
            }
            if (caCerts is null || caCerts.Count == 0)
            {
                return errors == SslPolicyErrors.None;
            }

            using var leaf = new X509Certificate2(certificate);
            using var custom = new X509Chain();
            custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            if (chain is not null)
            {
                // intermediates the server sent along with its certificate
                foreach (var element in chain.ChainElements)
                {
                    custom.ChainPolicy.ExtraStore.Add(element.Certificate);
                }
            }
            foreach (var ca in caCerts)
            {
                custom.ChainPolicy.ExtraStore.Add(ca);
            }
#if NET5_0_OR_GREATER
            custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (var ca in caCerts)
            {
                custom.ChainPolicy.CustomTrustStore.Add(ca);
            }
            return custom.Build(leaf);
#else
            // No custom trust store on netstandard2.1: accept an unknown root, but then require that the root is one of ours.
            custom.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            if (!custom.Build(leaf) || custom.ChainElements.Count == 0)
            {
                return false;
            }
            string rootThumbprint = custom.ChainElements[custom.ChainElements.Count - 1].Certificate.Thumbprint;
            foreach (var ca in caCerts)
            {
                if (string.Equals(ca.Thumbprint, rootThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
#endif
        }

        /// <summary>Loads every certificate of a PEM bundle, or the single certificate of a DER/PKCS#12 free file.</summary>
        private static X509Certificate2Collection LoadCaCertificates(string path)
        {
            var result = new X509Certificate2Collection();
            string text = File.ReadAllText(path);
            const string begin = "-----BEGIN CERTIFICATE-----";
            const string endMarker = "-----END CERTIFICATE-----";
            int pos = 0;
            bool any = false;
            while (true)
            {
                int b = text.IndexOf(begin, pos, StringComparison.Ordinal);
                if (b < 0)
                {
                    break;
                }
                int e = text.IndexOf(endMarker, b, StringComparison.Ordinal);
                if (e < 0)
                {
                    break;
                }
                string base64 = text.Substring(b + begin.Length, e - b - begin.Length);
                result.Add(new X509Certificate2(Convert.FromBase64String(base64.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace(" ", string.Empty))));
                any = true;
                pos = e + endMarker.Length;
            }
            if (!any)
            {
                result.Add(new X509Certificate2(path));
            }
            return result;
        }

        private static X509Certificate2 LoadClientCertificate(HotKVTlsOptions tls)
        {
#if NET8_0_OR_GREATER
            if (!string.IsNullOrEmpty(tls.ClientCertificateKeyPath))
            {
                return X509Certificate2.CreateFromPemFile(tls.ClientCertificatePath!, tls.ClientCertificateKeyPath);
            }
#endif
            return string.IsNullOrEmpty(tls.ClientCertificatePassword)
                ? new X509Certificate2(tls.ClientCertificatePath!)
                : new X509Certificate2(tls.ClientCertificatePath!, tls.ClientCertificatePassword);
        }

        private async Task HandshakeAsync(HotKVClientOptions options, CancellationToken cancellationToken)
        {
            bool helloSucceeded = false;
            if (options.PreferResp3)
            {
                var helloArgs = new List<object?> { "HELLO", "3" };
                AppendAuthAndName(helloArgs, options);
                await WriteCommandAsync(helloArgs, cancellationToken).ConfigureAwait(false);
                RespValue reply = await Reader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
                if (!reply.IsError)
                {
                    ProtocolVersion = 3;
                    helloSucceeded = true;
                }
                // A server that does not understand HELLO/RESP3 replies with an
                // error; fall through to the RESP2 legacy handshake below.
            }

            if (!helloSucceeded)
            {
                ProtocolVersion = 2;
                if (!string.IsNullOrEmpty(options.Password))
                {
                    var authArgs = new List<object?> { "AUTH" };
                    if (!string.IsNullOrEmpty(options.Username))
                    {
                        authArgs.Add(options.Username!);
                    }
                    authArgs.Add(options.Password!);
                    await ExpectOkAsync(authArgs, cancellationToken).ConfigureAwait(false);
                }
                if (!string.IsNullOrEmpty(options.ClientName))
                {
                    await ExpectOkAsync(new List<object?> { "CLIENT", "SETNAME", options.ClientName! }, cancellationToken).ConfigureAwait(false);
                }
            }

            if (options.Database != 0)
            {
                await ExpectOkAsync(new List<object?> { "SELECT", options.Database }, cancellationToken).ConfigureAwait(false);
            }

            if (options.PostConnectCommands is { Count: > 0 } postConnect)
            {
                foreach (var command in postConnect)
                {
                    await ExpectOkAsync(new List<object?>(command), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private static void AppendAuthAndName(List<object?> args, HotKVClientOptions options)
        {
            if (!string.IsNullOrEmpty(options.Password))
            {
                args.Add("AUTH");
                args.Add(string.IsNullOrEmpty(options.Username) ? "default" : options.Username!);
                args.Add(options.Password!);
            }
            if (!string.IsNullOrEmpty(options.ClientName))
            {
                args.Add("SETNAME");
                args.Add(options.ClientName!);
            }
        }

        private async Task ExpectOkAsync(List<object?> args, CancellationToken cancellationToken)
        {
            RespValue reply = await ExecuteAsync(args, cancellationToken).ConfigureAwait(false);
            if (reply.IsError)
            {
                var (prefix, message) = RespValue.SplitError(reply.ErrorText);
                throw HotKVServerException.Create(prefix, message);
            }
        }

        /// <summary>Encodes and immediately writes one command to the socket.</summary>
        public async ValueTask WriteCommandAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            QueueCommand(args);
            await FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Encodes one command into the pending send buffer without writing it yet. Used to build pipelines and transactions.</summary>
        public void QueueCommand(IReadOnlyList<object?> args)
        {
            ThrowIfDisposed();
            RespWriter.WriteCommand(_sendBuffer, args);
        }

        /// <summary>Writes every buffered, queued command to the socket in one call.</summary>
        public async ValueTask FlushAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_sendBuffer.Length == 0)
            {
                return;
            }
            try
            {
                await _stream.WriteAsync(_sendBuffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
            {
                throw new HotKVConnectionException($"connection lost while writing: {ex.Message}", ex);
            }
            finally
            {
                _sendBuffer.Reset();
            }
        }

        /// <summary>
        /// Reads the next command reply, transparently forwarding any RESP3
        /// push frames encountered along the way to <see cref="OnPush"/> and
        /// continuing to read until a non-push reply arrives.
        /// </summary>
        public async ValueTask<RespValue> ReadReplyAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            while (true)
            {
                RespValue value;
                try
                {
                    value = await Reader.ReadValueAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
                {
                    throw new HotKVConnectionException($"connection lost while reading a reply: {ex.Message}", ex);
                }
                if (value.Type == RespType.Push)
                {
                    OnPush?.Invoke(value);
                    continue;
                }
                LastUsedUtc = DateTime.UtcNow;
                return value;
            }
        }

        /// <summary>Sends one command and returns its reply. Throws <see cref="HotKVServerException"/> (or a subclass) if the server replied with an error.</summary>
        public async ValueTask<RespValue> ExecuteAsync(IReadOnlyList<object?> args, CancellationToken cancellationToken = default)
        {
            await WriteCommandAsync(args, cancellationToken).ConfigureAwait(false);
            RespValue reply = await ReadReplyAsync(cancellationToken).ConfigureAwait(false);
            if (reply.IsError)
            {
                var (prefix, message) = RespValue.SplitError(reply.ErrorText);
                throw HotKVServerException.Create(prefix, message);
            }
            return reply;
        }

        /// <summary>Sends a PING and returns true if the server replied without error, false if the connection appears broken.</summary>
        public async Task<bool> TryPingAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await ExecuteAsync(new object?[] { "PING" }, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (HotKVException)
            {
                return false;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(HotKVConnection));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try { _stream.Dispose(); } catch (IOException) { }
            _tcpClient?.Dispose();
            _unixSocket?.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return default;
        }
    }
}
