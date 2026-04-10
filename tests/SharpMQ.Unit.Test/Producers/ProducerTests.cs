using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Producers;
using SharpMQ.Serializer.Abstractions;
using Xunit;

namespace SharpMQ.Unit.Test.Producers
{
    public class ProducerTests : IDisposable
    {
        private readonly Mock<IChannelPool> _mockChannelPool;
        private readonly Mock<IModel> _mockChannel;
        private readonly Mock<ILogger<Producer>> _mockLogger;
        private readonly Mock<RabbitSerializer> _mockSerializer;
        private readonly ProducerConfig _producerConfig;

        public ProducerTests()
        {
            _mockChannelPool = new Mock<IChannelPool>();
            _mockChannel = new Mock<IModel>();
            _mockLogger = new Mock<ILogger<Producer>>();
            _mockSerializer = new Mock<RabbitSerializer>();

            _mockChannelPool
                .Setup(cp => cp.GetChannelAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockChannel.Object);

            _mockChannelPool
                .Setup(cp => cp.AddOrCloseChannelAsync(It.IsAny<IModel>(), It.IsAny<CancellationToken>()))
                .Returns(ValueTask.CompletedTask);

            var basicProps = new Mock<IBasicProperties>();
            _mockChannel
                .Setup(ch => ch.CreateBasicProperties())
                .Returns(basicProps.Object);

            _producerConfig = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 1,
                    MaxPoolSize = 5,
                    WaitTimeoutMs = 5000
                }
            };
        }

        private Producer CreateProducer()
        {
            return new Producer(
                _mockChannelPool.Object,
                _producerConfig,
                _mockLogger.Object,
                _mockSerializer.Object);
        }

        public void Dispose()
        {
            // Intentionally empty; individual tests manage producer lifetime.
        }

        #region PublishAsync: MinExpirationMs validation

        [Theory]
        [InlineData(1)]
        [InlineData(50)]
        [InlineData(99)]
        public async Task PublishAsync_Should_ThrowArgumentOutOfRange_When_ExpirationBetween1AndMinimum(long expirationMs)
        {
            // Arrange
            using var producer = CreateProducer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var act = () => producer.PublishAsync(
                exchange: "test-exchange",
                routingKey: "test-key",
                message: new ProducerTestMessage { Content = "test" },
                expirationMs: expirationMs,
                cancellationToken: cts.Token);

            // Assert
            await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
                .Where(ex => ex.ParamName == "expirationMs");
        }

        [Fact]
        public async Task PublishAsync_Should_NotThrow_When_ExpirationIsZero()
        {
            // Arrange
            using var producer = CreateProducer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var act = () => producer.PublishAsync(
                exchange: "test-exchange",
                routingKey: "test-key",
                message: new ProducerTestMessage { Content = "test" },
                expirationMs: 0,
                cancellationToken: cts.Token);

            // Assert — 0 means "no expiration" and should not throw
            await act.Should().NotThrowAsync<ArgumentOutOfRangeException>();
        }

        [Theory]
        [InlineData(100)]
        [InlineData(500)]
        [InlineData(60000)]
        public async Task PublishAsync_Should_NotThrow_When_ExpirationIsAtOrAboveMinimum(long expirationMs)
        {
            // Arrange
            using var producer = CreateProducer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var act = () => producer.PublishAsync(
                exchange: "test-exchange",
                routingKey: "test-key",
                message: new ProducerTestMessage { Content = "test" },
                expirationMs: expirationMs,
                cancellationToken: cts.Token);

            // Assert
            await act.Should().NotThrowAsync<ArgumentOutOfRangeException>();
        }

        [Fact]
        public void MinExpirationMs_Should_Be100()
        {
            // Assert — verify the constant value is documented correctly
            Producer.MinExpirationMs.Should().Be(100);
        }

        #endregion
    }

    /// <summary>
    /// Dummy message type used as a generic type argument in producer tests.
    /// </summary>
    public class ProducerTestMessage
    {
        public string Content { get; set; }
    }
}
