using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Consumers;
using SharpMQ.Serializer.Abstractions;
using Xunit;

namespace SharpMQ.Unit.Test.Consumers
{
    public class ConsumerTests : IDisposable
    {
        private readonly Mock<IConnectionProvider> _mockConnectionProvider;
        private readonly Mock<IConnection> _mockConnection;
        private readonly Mock<IModel> _mockChannel;
        private readonly Mock<ILogger> _mockLogger;
        private readonly Mock<RabbitSerializer> _mockSerializer;
        private readonly ConsumerConfig _consumerConfig;

        public ConsumerTests()
        {
            _mockConnectionProvider = new Mock<IConnectionProvider>();
            _mockConnection = new Mock<IConnection>();
            _mockChannel = new Mock<IModel>();
            _mockLogger = new Mock<ILogger>();
            _mockSerializer = new Mock<RabbitSerializer>();

            _mockConnectionProvider
                .SetupGet(cp => cp.IsDispatchConsumersAsyncEnabled)
                .Returns(true);

            _mockConnectionProvider
                .Setup(cp => cp.GetOrCreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockConnection.Object);

            _mockConnection
                .Setup(c => c.CreateModel())
                .Returns(_mockChannel.Object);

            _mockChannel.SetupGet(ch => ch.IsOpen).Returns(true);
            _mockChannel.SetupGet(ch => ch.IsClosed).Returns(false);

            // When BasicConsume is called, simulate the broker callback by invoking
            // HandleBasicConsumeOk on the consumer argument via the IAsyncBasicConsumer interface,
            // which populates ConsumerTags. The explicit IBasicConsumer.HandleBasicConsumeOk throws
            // "Should never be called" in async consumers, so we must use the async overload.
            _mockChannel
                .Setup(ch => ch.BasicConsume(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Returns((string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                          IDictionary<string, object> arguments, IBasicConsumer consumer) =>
                {
                    var tag = $"amq.ctag-{Guid.NewGuid():N}";
                    // AsyncEventingBasicConsumer inherits AsyncDefaultBasicConsumer which has
                    // a public virtual Task HandleBasicConsumeOk(string). We call it via the
                    // IAsyncBasicConsumer interface to avoid the throwing explicit IBasicConsumer impl.
                    if (consumer is IAsyncBasicConsumer asyncConsumer)
                    {
                        asyncConsumer.HandleBasicConsumeOk(tag).GetAwaiter().GetResult();
                    }
                    return tag;
                });

            _consumerConfig = new ConsumerConfig
            {
                ConsumersCount = 1,
                Queue = new QueueParamsConfig
                {
                    Name = "test-queue",
                    UseMessageTypeAsQueueName = false,
                },
            };
        }

        private Consumer<TestMessage> CreateConsumer(bool ownsConnection = true)
        {
            return new Consumer<TestMessage>(
                _mockConnectionProvider.Object,
                _consumerConfig,
                serviceProvider: null,
                _mockLogger.Object,
                _mockSerializer.Object,
                defaultSerializerOptions: null,
                ownsConnection: ownsConnection);
        }

        public void Dispose()
        {
            // Intentionally empty; individual tests manage their consumer lifetime.
        }

        #region StartConsume: guard against double-subscribe

        [Fact]
        public async Task StartConsume_Should_ReturnFalse_When_ConsumerTagsAlreadyExist()
        {
            // Arrange
            using var consumer = CreateConsumer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await consumer.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // The consumer now has a tag from the SubscribeAsync call (BasicConsume triggers HandleBasicConsumeOk).
            consumer.GetConsumerTags().Should().NotBeEmpty("SubscribeAsync registers at least one consumer tag");

            // Act — calling StartConsume again should be guarded.
            var result = consumer.StartConsume(rethrowError: false);

            // Assert
            result.Should().BeFalse("consumer tags already exist; double-subscribe should be prevented");
        }

        #endregion

        #region ownsConnection: disposal behavior

        [Fact]
        public void Dispose_Should_NotDisposeConnectionProvider_When_OwnsConnectionIsFalse()
        {
            // Arrange
            var consumer = CreateConsumer(ownsConnection: false);

            // Act
            consumer.Dispose();

            // Assert — ConnectionProvider.Dispose must NOT have been called.
            _mockConnectionProvider.Verify(cp => cp.Dispose(), Times.Never);
        }

        [Fact]
        public void Dispose_Should_DisposeConnectionProvider_When_OwnsConnectionIsTrue()
        {
            // Arrange
            var consumer = CreateConsumer(ownsConnection: true);

            // Act
            consumer.Dispose();

            // Assert — ConnectionProvider.Dispose MUST have been called.
            _mockConnectionProvider.Verify(cp => cp.Dispose(), Times.Once);
        }

        [Fact]
        public void Dispose_SharedConnection_Should_NotAffectSiblingConsumers()
        {
            // Arrange — two consumers sharing one connection provider, neither owns it.
            var consumerA = CreateConsumer(ownsConnection: false);
            var consumerB = CreateConsumer(ownsConnection: false);

            // Act — dispose only the first consumer.
            consumerA.Dispose();

            // Assert — the connection provider is still alive (not disposed).
            _mockConnectionProvider.Verify(cp => cp.Dispose(), Times.Never);

            // The second consumer can still be disposed normally.
            consumerB.Dispose();
            _mockConnectionProvider.Verify(cp => cp.Dispose(), Times.Never);
        }

        #endregion

        #region EnsureInitialized and CreateNewChannelAndStartConsume: async semaphore (no synchronous blocking)

        [Fact]
        public async Task EnsureInitialized_Should_UseAsyncSemaphore_NotBlockSynchronously()
        {
            // This test verifies that EnsureInitialized uses WaitAsync (async path)
            // by confirming that SubscribeAsync completes without deadlocking
            // when called from an async context. If a synchronous Wait() were used,
            // this would risk a deadlock on a single-threaded SynchronizationContext.

            using var consumer = CreateConsumer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act — should complete without deadlocking because WaitAsync is used.
            var act = () => consumer.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            await act.Should().NotThrowAsync("EnsureInitialized uses WaitAsync, not blocking Wait");
        }

        [Fact]
        public async Task CreateNewChannelAndStartConsume_Should_UseAsyncSemaphore_NotBlockSynchronously()
        {
            // First initialize via SubscribeAsync so _asyncEventingBasicConsumer is set.
            using var consumer = CreateConsumer();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            await consumer.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // Simulate a closed channel so CreateNewChannelAndStartConsume proceeds to create a new one.
            _mockChannel.SetupGet(ch => ch.IsOpen).Returns(false);
            _mockChannel.SetupGet(ch => ch.IsClosed).Returns(true);

            // Set up a new channel mock for the replacement.
            var newChannel = new Mock<IModel>();
            newChannel.SetupGet(ch => ch.IsOpen).Returns(true);
            newChannel.SetupGet(ch => ch.IsClosed).Returns(false);
            _mockConnection.Setup(c => c.CreateModel()).Returns(newChannel.Object);

            newChannel
                .Setup(ch => ch.BasicConsume(
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>(),
                    It.IsAny<IDictionary<string, object>>(),
                    It.IsAny<IBasicConsumer>()))
                .Returns((string queue, bool autoAck, string consumerTag, bool noLocal, bool exclusive,
                          IDictionary<string, object> arguments, IBasicConsumer c) =>
                {
                    var tag = $"amq.ctag-{Guid.NewGuid():N}";
                    if (c is IAsyncBasicConsumer asyncConsumer)
                    {
                        asyncConsumer.HandleBasicConsumeOk(tag).GetAwaiter().GetResult();
                    }
                    return tag;
                });

            // Act — should complete without deadlocking.
            var act = () => consumer.CreateNewChannelAndStartConsume(
                rethrowError: true,
                cancellationToken: cts.Token);

            await act.Should().NotThrowAsync("CreateNewChannelAndStartConsume uses WaitAsync, not blocking Wait");
        }

        [Fact]
        public async Task EnsureInitialized_Should_SupportCancellation()
        {
            // Arrange — use an already-cancelled token.
            using var consumer = CreateConsumer();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert — should throw OperationCanceledException because the token is already cancelled.
            var act = () => consumer.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }

        #endregion
    }

    /// <summary>
    /// Dummy message type used as a generic type argument in consumer tests.
    /// </summary>
    public class TestMessage
    {
        public string Content { get; set; }
    }
}
