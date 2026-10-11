// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

namespace HotKV
{
    /// <summary>TLS configuration for a HotKV connection.</summary>
    public sealed class HotKVTlsOptions
    {
        /// <summary>Enables TLS. Set implicitly by the "hotkvs://"/"rediss://" URL schemes.</summary>
        public bool Enabled { get; set; }

        /// <summary>Path to a PEM encoded CA certificate (or bundle) used to validate the server, in addition to the system trust store.</summary>
        public string? CaCertificatePath { get; set; }

        /// <summary>Path to a client certificate for mutual TLS: a PFX/PKCS#12 file, or a PEM certificate when paired with <see cref="ClientCertificateKeyPath"/>.</summary>
        public string? ClientCertificatePath { get; set; }

        /// <summary>Path to the PEM private key matching <see cref="ClientCertificatePath"/>, when the certificate is a separate PEM file (net8.0 only).</summary>
        public string? ClientCertificateKeyPath { get; set; }

        /// <summary>Password protecting a PFX <see cref="ClientCertificatePath"/>, if any.</summary>
        public string? ClientCertificatePassword { get; set; }

        /// <summary>Overrides the host name used for TLS server name indication and certificate validation. Defaults to the connection host.</summary>
        public string? ServerName { get; set; }

        /// <summary>
        /// Disables all server certificate validation. Intended for tests
        /// against a self-signed test server only: never enable this
        /// against a production endpoint.
        /// </summary>
        public bool InsecureSkipVerify { get; set; }

        public HotKVTlsOptions Clone() => new HotKVTlsOptions
        {
            Enabled = Enabled,
            CaCertificatePath = CaCertificatePath,
            ClientCertificatePath = ClientCertificatePath,
            ClientCertificateKeyPath = ClientCertificateKeyPath,
            ClientCertificatePassword = ClientCertificatePassword,
            ServerName = ServerName,
            InsecureSkipVerify = InsecureSkipVerify,
        };
    }
}
