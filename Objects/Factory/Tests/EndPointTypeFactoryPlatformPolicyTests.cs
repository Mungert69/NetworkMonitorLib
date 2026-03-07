using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Connection;
using NetworkMonitor.Objects.Factory;
using Xunit;

namespace NetworkMonitor.Objects.Factory.Tests
{
    public class EndPointTypeFactoryPlatformPolicyTests
    {
        private static readonly string[] AndroidPuppeteerEndpointTypes =
        {
            "httpfull",
            "sitehash",
            "crawlsite",
            "dailycrawl",
            "dailyhugkeepalive",
            "hugwake"
        };

        private static readonly string[] AndroidPuppeteerCommands =
        {
            "searchweb",
            "searchengage",
            "crawlpage",
            "crawlsite",
            "hugspacewake",
            "hugspacekeepalive"
        };

        private static NetConnectConfig CreateConfig()
        {
            var configuration = new ConfigurationBuilder().Build();
            return new NetConnectConfig(configuration, "TestSection");
        }

        [Fact]
        public void ApplyPlatformCapabilityPolicy_WhenAndroid_AddsExpectedRestrictions()
        {
            var config = CreateConfig();
            config.DisabledEndpointTypes = new List<string> { "icmp" };
            config.DisabledCommands = new List<string> { "ping" };
            config.EndpointTypes = EndPointTypeFactory.GetEnabledEndPoints(config.DisabledEndpointTypes);

            var changed = EndPointTypeFactory.ApplyPlatformCapabilityPolicy(config, "android");

            Assert.True(changed);
            foreach (var endpointType in AndroidPuppeteerEndpointTypes)
            {
                Assert.Contains(endpointType, config.DisabledEndpointTypes, StringComparer.OrdinalIgnoreCase);
                Assert.DoesNotContain(endpointType, config.EndpointTypes, StringComparer.OrdinalIgnoreCase);
            }

            foreach (var command in AndroidPuppeteerCommands)
            {
                Assert.Contains(command, config.DisabledCommands, StringComparer.OrdinalIgnoreCase);
            }
        }

        [Fact]
        public void ApplyPlatformCapabilityPolicy_WhenNotAndroid_DoesNothing()
        {
            var config = CreateConfig();
            config.DisabledEndpointTypes = new List<string> { "icmp" };
            config.DisabledCommands = new List<string> { "ping" };
            config.EndpointTypes = EndPointTypeFactory.GetEnabledEndPoints(config.DisabledEndpointTypes);
            var endpointCountBefore = config.DisabledEndpointTypes.Count;
            var commandCountBefore = config.DisabledCommands.Count;
            var enabledBefore = new List<string>(config.EndpointTypes);

            var changed = EndPointTypeFactory.ApplyPlatformCapabilityPolicy(config, "windows");

            Assert.False(changed);
            Assert.Equal(endpointCountBefore, config.DisabledEndpointTypes.Count);
            Assert.Equal(commandCountBefore, config.DisabledCommands.Count);
            Assert.Equal(enabledBefore, config.EndpointTypes);
        }

        [Fact]
        public void ApplyPlatformCapabilityPolicy_WhenRepeated_IsIdempotent()
        {
            var config = CreateConfig();

            var changedFirst = EndPointTypeFactory.ApplyPlatformCapabilityPolicy(config, "android");
            var endpointCount = config.DisabledEndpointTypes.Count;
            var commandCount = config.DisabledCommands.Count;

            var changedSecond = EndPointTypeFactory.ApplyPlatformCapabilityPolicy(config, "android");

            Assert.True(changedFirst);
            Assert.False(changedSecond);
            Assert.Equal(endpointCount, config.DisabledEndpointTypes.Count);
            Assert.Equal(commandCount, config.DisabledCommands.Count);
            Assert.All(config.DisabledEndpointTypes, value =>
                Assert.Single(config.DisabledEndpointTypes.FindAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))));
            Assert.All(config.DisabledCommands, value =>
                Assert.Single(config.DisabledCommands.FindAll(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
