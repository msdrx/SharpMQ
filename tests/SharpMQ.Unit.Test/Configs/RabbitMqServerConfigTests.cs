using System.Linq;
using FluentAssertions;
using SharpMQ.Configs;
using Xunit;

namespace SharpMQ.Unit.Test.Configs
{
    public class RabbitMqServerConfigTests
    {
        #region MqHosts: uses configured port

        [Fact]
        public void MqHosts_Should_UseDefaultPort_When_PortNotExplicitlySet()
        {
            // Arrange
            var config = new RabbitMqServerConfig
            {
                Hosts = new[] { "host1", "host2" }
            };

            // Act
            var endpoints = config.MqHosts().ToList();

            // Assert
            endpoints.Should().HaveCount(2);
            endpoints.Should().AllSatisfy(ep => ep.Port.Should().Be(5672));
            endpoints[0].HostName.Should().Be("host1");
            endpoints[1].HostName.Should().Be("host2");
        }

        [Fact]
        public void MqHosts_Should_UseCustomPort_When_PortIsExplicitlySet()
        {
            // Arrange
            var config = new RabbitMqServerConfig
            {
                Hosts = new[] { "host1", "host2" },
                Port = 5673
            };

            // Act
            var endpoints = config.MqHosts().ToList();

            // Assert
            endpoints.Should().HaveCount(2);
            endpoints.Should().AllSatisfy(ep => ep.Port.Should().Be(5673));
        }

        [Fact]
        public void Port_Should_DefaultTo5672()
        {
            // Arrange & Act
            var config = new RabbitMqServerConfig();

            // Assert
            config.Port.Should().Be(5672);
        }

        [Fact]
        public void MqHosts_Should_ReturnEndpointsForEachHost()
        {
            // Arrange
            var config = new RabbitMqServerConfig
            {
                Hosts = new[] { "alpha", "beta", "gamma" },
                Port = 15672
            };

            // Act
            var endpoints = config.MqHosts().ToList();

            // Assert
            endpoints.Should().HaveCount(3);
            endpoints.Select(ep => ep.HostName).Should().ContainInOrder("alpha", "beta", "gamma");
            endpoints.Should().AllSatisfy(ep => ep.Port.Should().Be(15672));
        }

        #endregion
    }
}
