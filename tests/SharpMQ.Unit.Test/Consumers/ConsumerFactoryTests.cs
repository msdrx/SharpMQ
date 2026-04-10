using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SharpMQ.Abstractions;
using SharpMQ.Serializer.Abstractions;
using Xunit;

namespace SharpMQ.Unit.Test.Consumers
{
    public class ConsumerFactoryTests
    {
        #region SubscribeAsync: exception propagation

        [Fact]
        public async Task SubscribeAsync_Should_PropagateException_When_ConsumerSubscribeAsyncThrows()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Subscription failed");

            var failingConsumer = new Mock<IConsumer<TestMessage>>();
            failingConsumer
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(expectedException);

            var consumers = new List<IConsumer<TestMessage>> { failingConsumer.Object };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var act = () => consumers.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // Assert — the exception from the consumer should propagate through Task.WhenAll.
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Subscription failed");
        }

        [Fact]
        public async Task SubscribeAsync_Should_PropagateAggregateException_When_MultipleConsumersThrow()
        {
            // Arrange
            var exception1 = new InvalidOperationException("Consumer 1 failed");
            var exception2 = new InvalidOperationException("Consumer 2 failed");

            var failingConsumer1 = new Mock<IConsumer<TestMessage>>();
            failingConsumer1
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception1);

            var failingConsumer2 = new Mock<IConsumer<TestMessage>>();
            failingConsumer2
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(exception2);

            var consumers = new List<IConsumer<TestMessage>>
            {
                failingConsumer1.Object,
                failingConsumer2.Object
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            Func<Task> act = () => consumers.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // Assert — Task.WhenAll wraps multiple failures; at least one should propagate.
            await act.Should().ThrowAsync<InvalidOperationException>();
        }

        [Fact]
        public async Task SubscribeAsync_Should_SucceedAndReturnTask_When_AllConsumersSucceed()
        {
            // Arrange
            var consumer1 = new Mock<IConsumer<TestMessage>>();
            consumer1
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var consumer2 = new Mock<IConsumer<TestMessage>>();
            consumer2
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var consumers = new List<IConsumer<TestMessage>>
            {
                consumer1.Object,
                consumer2.Object
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            var act = () => consumers.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // Assert — should complete without throwing.
            await act.Should().NotThrowAsync();

            // Both consumers should have been subscribed.
            consumer1.Verify(c => c.SubscribeAsync(
                It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                It.IsAny<RabbitSerializerOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);

            consumer2.Verify(c => c.SubscribeAsync(
                It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                It.IsAny<RabbitSerializerOptions>(),
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task SubscribeAsync_Should_PassCancellationToken_ToEachConsumer()
        {
            // Arrange
            CancellationToken capturedToken = default;

            var consumer = new Mock<IConsumer<TestMessage>>();
            consumer
                .Setup(c => c.SubscribeAsync(
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Task>>(),
                    It.IsAny<Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>>(),
                    It.IsAny<RabbitSerializerOptions>(),
                    It.IsAny<CancellationToken>()))
                .Callback<Func<TestMessage, IServiceProvider, MessageContext, Task>,
                          Func<TestMessage, IServiceProvider, MessageContext, Exception, Task>,
                          RabbitSerializerOptions,
                          CancellationToken>((_, _, _, ct) => capturedToken = ct)
                .Returns(Task.CompletedTask);

            var consumers = new List<IConsumer<TestMessage>> { consumer.Object };
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act
            await consumers.SubscribeAsync(
                onDequeue: (msg, sp, ctx) => Task.CompletedTask,
                onException: null,
                serializerOptions: null,
                cancellationToken: cts.Token);

            // Assert
            capturedToken.Should().Be(cts.Token, "the factory should forward the CancellationToken to each consumer");
        }

        #endregion
    }
}
