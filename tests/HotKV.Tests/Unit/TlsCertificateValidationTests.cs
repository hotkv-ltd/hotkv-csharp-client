// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using HotKV.Connection;
using Xunit;

namespace HotKV.Tests.Unit
{
    /// <summary>
    /// The server certificate decision (<see cref="HotKVConnection.ValidateServerCertificate"/>) with generated
    /// certificates: the configured CA must be the root, the host name is always checked, and an expired or
    /// foreign-CA certificate is never accepted.
    /// </summary>
    public class TlsCertificateValidationTests
    {
        private static X509Certificate2 NewCa(string name)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            using var created = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow.AddDays(30));
            return new X509Certificate2(created.Export(X509ContentType.Pfx));
        }

        private static X509Certificate2 NewServerCert(X509Certificate2 ca, DateTimeOffset notBefore, DateTimeOffset notAfter)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            req.CertificateExtensions.Add(san.Build());
            req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
            req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, false));
            var serial = new byte[8];
            RandomNumberGenerator.Fill(serial);
            serial[0] &= 0x7F;
            using var signed = req.Create(ca, notBefore, notAfter, serial);
            return new X509Certificate2(signed.Export(X509ContentType.Cert));
        }

        private static X509Certificate2Collection Only(X509Certificate2 ca)
        {
            var c = new X509Certificate2Collection();
            c.Add(new X509Certificate2(ca.Export(X509ContentType.Cert)));
            return c;
        }

        [Fact]
        public void CertificateSignedByTheConfiguredCaIsAccepted()
        {
            using var ca = NewCa("test-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
            Assert.True(HotKVConnection.ValidateServerCertificate(false, Only(ca), server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void HostNameMismatchIsRejectedEvenWhenTheCaMatches()
        {
            using var ca = NewCa("test-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, Only(ca), server, null,
                SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
        }

        [Fact]
        public void ExpiredCertificateIsRejectedEvenWhenTheCaMatches()
        {
            using var ca = NewCa("test-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-2));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, Only(ca), server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void CertificateFromAnotherCaIsRejected()
        {
            using var ca = NewCa("real-ca");
            using var other = NewCa("other-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, Only(other), server, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void SelfSignedCertificateWithAnUnrelatedCaIsRejected()
        {
            using var ca = NewCa("test-ca");
            using var selfSigned = NewCa("localhost");
            Assert.False(HotKVConnection.ValidateServerCertificate(false, Only(ca), selfSigned, null, SslPolicyErrors.RemoteCertificateChainErrors));
        }

        [Fact]
        public void WithoutAnyCaTheSystemVerdictIsUsed()
        {
            using var ca = NewCa("test-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
            Assert.True(HotKVConnection.ValidateServerCertificate(false, null, server, null, SslPolicyErrors.None));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, null, server, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, null, server, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        }

        [Fact]
        public void InsecureSkipVerifyAcceptsAnythingAndMissingCertificatesAreRejected()
        {
            using var ca = NewCa("test-ca");
            using var server = NewServerCert(ca, DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-2));
            Assert.True(HotKVConnection.ValidateServerCertificate(true, null, server, null, SslPolicyErrors.RemoteCertificateChainErrors | SslPolicyErrors.RemoteCertificateNameMismatch));
            Assert.False(HotKVConnection.ValidateServerCertificate(false, Only(ca), null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
        }
    }
}
