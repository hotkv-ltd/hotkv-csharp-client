// Copyright (c) 2026 HotKV Ltd
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root.

using HotKV;
using Xunit;

namespace HotKV.Tests.Unit
{
    public class ExceptionMappingTests
    {
        [Fact]
        public void NoLicensePrefixMapsToNotLicensedException()
        {
            var ex = HotKVServerException.Create("NOLICENSE", "enterprise feature 'prompt-cache' requires a valid license");
            Assert.IsType<NotLicensedException>(ex);
            Assert.Equal("NOLICENSE", ex.Prefix);
        }

        [Fact]
        public void WrongTypePrefixMapsToWrongTypeException()
        {
            var ex = HotKVServerException.Create("WRONGTYPE", "Operation against a key holding the wrong kind of value");
            Assert.IsType<HotKVWrongTypeException>(ex);
        }

        [Fact]
        public void NoAuthPrefixMapsToAuthenticationException()
        {
            Assert.IsType<HotKVAuthenticationException>(HotKVServerException.Create("NOAUTH", "Authentication required"));
            Assert.IsType<HotKVAuthenticationException>(HotKVServerException.Create("WRONGPASS", "invalid password"));
        }

        [Fact]
        public void MovedPrefixMapsToClusterRedirectWithParsedSlotAndEndpoint()
        {
            var ex = HotKVServerException.Create("MOVED", "3999 127.0.0.1:7001");
            var redirect = Assert.IsType<HotKVClusterRedirectException>(ex);
            Assert.False(redirect.IsAsk);
            Assert.Equal(3999, redirect.Slot);
            Assert.Equal("127.0.0.1:7001", redirect.Endpoint);
        }

        [Fact]
        public void AskPrefixMapsToClusterRedirectMarkedAsAsk()
        {
            var ex = HotKVServerException.Create("ASK", "3999 127.0.0.1:7002");
            var redirect = Assert.IsType<HotKVClusterRedirectException>(ex);
            Assert.True(redirect.IsAsk);
            Assert.Equal("127.0.0.1:7002", redirect.Endpoint);
        }

        [Fact]
        public void UnknownPrefixMapsToPlainServerException()
        {
            var ex = HotKVServerException.Create("ERR", "wrong number of arguments for 'set' command");
            Assert.Equal(typeof(HotKVServerException), ex.GetType());
            Assert.Equal("ERR", ex.Prefix);
            Assert.Equal("wrong number of arguments for 'set' command", ex.RawMessage);
        }

        [Fact]
        public void EveryMappedExceptionIsAHotKVException()
        {
            Assert.IsAssignableFrom<HotKVException>(HotKVServerException.Create("NOLICENSE", "x"));
            Assert.IsAssignableFrom<HotKVException>(HotKVServerException.Create("WRONGTYPE", "x"));
            Assert.IsAssignableFrom<HotKVException>(HotKVServerException.Create("MOVED", "1 h:1"));
        }
    }
}
