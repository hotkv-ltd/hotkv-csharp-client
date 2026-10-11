// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using System.Threading.Tasks;
using HotKV;
using HotKV.Commands;
using Xunit;

namespace HotKV.Tests.Integration
{
    /// <summary>
    /// Password, ACL user and TLS connections against dedicated test servers. Each test is skipped unless its
    /// environment variable is set (the credentials are test values of throwaway servers and are only ever
    /// passed through the environment):
    /// <list type="bullet">
    /// <item><c>HOTKV_TEST_AUTH_URL</c>: a password protected plain server, e.g. <c>hotkv://:the-password@127.0.0.1:6379</c>.</item>
    /// <item><c>HOTKV_TEST_TLS_URL</c> and <c>HOTKV_TEST_TLS_CA</c>: the TLS port of the same server (<c>hotkvs://:the-password@127.0.0.1:6380</c>) and the path of the CA file that signed its certificate (SAN 127.0.0.1 and localhost).</item>
    /// </list>
    /// </summary>
    public class AuthAndTlsTests
    {
        private static string? AuthUrl => Environment.GetEnvironmentVariable("HOTKV_TEST_AUTH_URL");
        private static string? TlsUrl => Environment.GetEnvironmentVariable("HOTKV_TEST_TLS_URL");
        private static string? TlsCa => Environment.GetEnvironmentVariable("HOTKV_TEST_TLS_CA");

        private static HotKVClientOptions AuthOptions()
        {
            Skip.If(string.IsNullOrWhiteSpace(AuthUrl), "HOTKV_TEST_AUTH_URL is not set");
            return HotKVClientOptions.Parse(AuthUrl!);
        }

        private static HotKVClientOptions TlsOptions()
        {
            Skip.If(string.IsNullOrWhiteSpace(TlsUrl) || string.IsNullOrWhiteSpace(TlsCa), "HOTKV_TEST_TLS_URL / HOTKV_TEST_TLS_CA are not set");
            var o = HotKVClientOptions.Parse(TlsUrl!);
            Assert.True(o.Tls.Enabled, "the hotkvs:// scheme must enable TLS");
            return o;
        }

        [SkippableFact]
        public async Task CorrectPasswordConnectsAndWorks()
        {
            var o = AuthOptions();
            await using var client = await HotKVClient.ConnectAsync(o);
            Assert.Equal("PONG", await client.PingAsync());
            string key = "hkvtest:auth:" + Guid.NewGuid().ToString("N");
            await client.StringSetAsync(key, "v");
            Assert.Equal("v", await client.StringGetAsync(key));
            await client.DeleteAsync(key);
        }

        [SkippableFact]
        public async Task WrongPasswordIsACleanAuthenticationError()
        {
            var o = AuthOptions();
            o.Password = "definitely-not-the-password";
            var ex = await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(o);
                await client.PingAsync();
            });
            Assert.True(ex.Prefix == "WRONGPASS" || ex.Prefix == "NOAUTH" || ex.Prefix == "ERR", ex.Prefix);
            // the error text never contains the password that was sent
            Assert.DoesNotContain("definitely-not-the-password", ex.Message);
        }

        [SkippableFact]
        public async Task MissingPasswordIsRejected()
        {
            var o = AuthOptions();
            o.Password = null;
            o.Username = null;
            await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(o);
                await client.PingAsync();
            });
        }

        [SkippableFact]
        public async Task AclUserWithItsOwnPassword()
        {
            var o = AuthOptions();
            string user = "csharp" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string pw = "pw-" + Guid.NewGuid().ToString("N");
            await using var admin = await HotKVClient.ConnectAsync(o);
            try
            {
                // read-only user restricted to one key prefix
                await admin.CommandAsync("ACL", "SETUSER", user, "on", ">" + pw, "~allowed:*", "+get", "+set", "+ping");

                var uo = HotKVClientOptions.Parse(AuthUrl!);
                uo.Username = user;
                uo.Password = pw;
                await using var limited = await HotKVClient.ConnectAsync(uo);
                Assert.Equal("PONG", await limited.PingAsync());
                await limited.StringSetAsync("allowed:k", "1");
                Assert.Equal("1", await limited.StringGetAsync("allowed:k"));
                var denied = await Assert.ThrowsAsync<HotKVServerException>(async () => await limited.StringSetAsync("other:k", "1"));
                Assert.Equal("NOPERM", denied.Prefix);
                var deniedCmd = await Assert.ThrowsAsync<HotKVServerException>(async () => await limited.DeleteAsync("allowed:k"));
                Assert.Equal("NOPERM", deniedCmd.Prefix);

                uo.Password = "wrong";
                await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
                {
                    await using var bad = await HotKVClient.ConnectAsync(uo);
                    await bad.PingAsync();
                });
            }
            finally
            {
                await admin.DeleteAsync("allowed:k");
                await admin.CommandAsync("ACL", "DELUSER", user);
            }
        }

        [SkippableFact]
        public async Task TlsWithTheRightCaWorks()
        {
            var o = TlsOptions();
            o.Tls.CaCertificatePath = TlsCa;
            await using var client = await HotKVClient.ConnectAsync(o);
            Assert.Equal("PONG", await client.PingAsync());
            string key = "hkvtest:tls:" + Guid.NewGuid().ToString("N");
            var big = new string('x', 300_000);
            await client.StringSetAsync(key, big);
            Assert.Equal(big, await client.StringGetAsync(key));
            await client.DeleteAsync(key);
        }

        [SkippableFact]
        public async Task TlsWithAnUntrustedCertificateFailsUnlessVerificationIsSkipped()
        {
            var o = TlsOptions();
            // no CA configured: the self-signed test CA is not in the system trust store
            await Assert.ThrowsAsync<HotKVConnectionException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(o);
                await client.PingAsync();
            });

            var insecure = TlsOptions();
            insecure.Tls.InsecureSkipVerify = true;
            await using var ok = await HotKVClient.ConnectAsync(insecure);
            Assert.Equal("PONG", await ok.PingAsync());
        }

        [SkippableFact]
        public async Task TlsRejectsAServerNameThatIsNotInTheCertificate()
        {
            var o = TlsOptions();
            o.Tls.CaCertificatePath = TlsCa;
            o.Tls.ServerName = "not-in-the-certificate.example";
            await Assert.ThrowsAsync<HotKVConnectionException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(o);
                await client.PingAsync();
            });
            o.Tls.ServerName = "localhost"; // is in the certificate (SAN)
            await using var good = await HotKVClient.ConnectAsync(o);
            Assert.Equal("PONG", await good.PingAsync());
        }

        [SkippableFact]
        public async Task TlsWrongPasswordIsStillAnAuthenticationError()
        {
            var o = TlsOptions();
            o.Tls.CaCertificatePath = TlsCa;
            o.Password = "nope";
            await Assert.ThrowsAsync<HotKVAuthenticationException>(async () =>
            {
                await using var client = await HotKVClient.ConnectAsync(o);
                await client.PingAsync();
            });
        }
    }
}
