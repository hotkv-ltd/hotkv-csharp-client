// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using System;
using HotKV;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class HotKVClientOptionsTests
    {
        [Fact]
        public void ParsesBasicHotkvUrl()
        {
            var options = HotKVClientOptions.Parse("hotkv://127.0.0.1:6399");
            Assert.Equal("127.0.0.1", options.Host);
            Assert.Equal(6399, options.Port);
            Assert.False(options.Tls.Enabled);
            Assert.Null(options.Password);
            Assert.Equal(0, options.Database);
        }

        [Fact]
        public void HotkvsSchemeEnablesTls()
        {
            var options = HotKVClientOptions.Parse("hotkvs://example.com:6400");
            Assert.True(options.Tls.Enabled);
            Assert.Equal("example.com", options.Host);
            Assert.Equal(6400, options.Port);
        }

        [Theory]
        [InlineData("redis://localhost:6379")]
        [InlineData("rediss://localhost:6379")]
        public void AcceptsRedisSchemesForDropInMigration(string url)
        {
            var options = HotKVClientOptions.Parse(url);
            Assert.Equal("localhost", options.Host);
            Assert.Equal(6379, options.Port);
        }

        [Fact]
        public void ParsesUserInfoAsUsernameAndPassword()
        {
            var options = HotKVClientOptions.Parse("hotkv://alice:s3cret@127.0.0.1:6399");
            Assert.Equal("alice", options.Username);
            Assert.Equal("s3cret", options.Password);
        }

        [Theory]
        [InlineData("p@ss:w/rd#%1 +&=?")]
        [InlineData("a:b:c")]
        [InlineData("%3A%40")]
        [InlineData("ünï-çödé")]
        public void PercentEncodedUserInfoRoundTripsIncludingColonsAtSignsAndSlashes(string password)
        {
            string url = "hotkvs://" + Uri.EscapeDataString("us:er@x") + ":" + Uri.EscapeDataString(password) + "@cache.example.com:6400";
            var options = HotKVClientOptions.Parse(url);
            Assert.Equal("us:er@x", options.Username);
            Assert.Equal(password, options.Password);
            Assert.Equal("cache.example.com", options.Host);
            Assert.Equal(6400, options.Port);
            Assert.True(options.Tls.Enabled);

            var passwordOnly = HotKVClientOptions.Parse("hotkvs://:" + Uri.EscapeDataString(password) + "@cache.example.com:6400");
            Assert.Null(passwordOnly.Username);
            Assert.Equal(password, passwordOnly.Password);
        }

        [Fact]
        public void ParsesPasswordOnlyUserInfo()
        {
            var options = HotKVClientOptions.Parse("hotkv://:s3cret@127.0.0.1:6399");
            Assert.Null(options.Username);
            Assert.Equal("s3cret", options.Password);
        }

        [Fact]
        public void ParsesDatabaseIndexFromPath()
        {
            var options = HotKVClientOptions.Parse("hotkv://127.0.0.1:6399/3");
            Assert.Equal(3, options.Database);
        }

        [Fact]
        public void ParsesQueryStringOptions()
        {
            var options = HotKVClientOptions.Parse(
                "hotkv://127.0.0.1:6399?clientName=myapp&connectTimeoutMs=1500&poolSize=20&insecureSkipVerify=true");
            Assert.Equal("myapp", options.ClientName);
            Assert.Equal(TimeSpan.FromMilliseconds(1500), options.ConnectTimeout);
            Assert.Equal(20, options.MaxPoolSize);
            Assert.True(options.Tls.InsecureSkipVerify);
        }

        [Fact]
        public void ParsesUnixSocketUrl()
        {
            var options = HotKVClientOptions.Parse("unix:///tmp/hotkv.sock");
            Assert.Equal("/tmp/hotkv.sock", options.UnixSocketPath);
        }

        [Fact]
        public void RejectsUnsupportedScheme()
        {
            Assert.Throws<ArgumentException>(() => HotKVClientOptions.Parse("ftp://127.0.0.1:21"));
        }

        [Fact]
        public void RejectsEmptyUrl()
        {
            Assert.Throws<ArgumentException>(() => HotKVClientOptions.Parse(""));
        }

        [Fact]
        public void RejectsMalformedUrl()
        {
            Assert.Throws<ArgumentException>(() => HotKVClientOptions.Parse("not a url"));
        }

        [Fact]
        public void CloneProducesIndependentTlsOptions()
        {
            var options = HotKVClientOptions.Parse("hotkv://127.0.0.1:6399");
            var clone = options.Clone();
            clone.Tls.Enabled = true;
            Assert.False(options.Tls.Enabled);
        }
    }
}
