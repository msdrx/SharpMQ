using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RabbitMQ.Client;
using SharpMQ.Connections;
using Xunit;

namespace SharpMQ.Unit.Test.Connections
{
    public class ChannelPoolTests : IDisposable
    {
        private readonly Mock<IConnectionProvider> _mockConnectionProvider;
        private readonly Mock<IConnection> _mockConnection;
        private int _channelCreationCount;

        public ChannelPoolTests()
        {
            _mockConnectionProvider = new Mock<IConnectionProvider>();
            _mockConnection = new Mock<IConnection>();

            _mockConnectionProvider
                .Setup(cp => cp.GetOrCreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(_mockConnection.Object);

            // Default: each CreateModel call returns a fresh healthy channel mock and increments the counter.
            _mockConnection
                .Setup(c => c.CreateModel())
                .Returns(() =>
                {
                    Interlocked.Increment(ref _channelCreationCount);
                    var ch = new Mock<IModel>();
                    ch.SetupGet(c => c.IsOpen).Returns(true);
                    ch.SetupGet(c => c.IsClosed).Returns(false);
                    return ch.Object;
                });
        }

        private ChannelPool CreatePool(int minPoolSize = 0, int maxPoolSize = 5, int waitTimeoutMs = 5000)
        {
            return new ChannelPool(
                _mockConnectionProvider.Object,
                minPoolSize: minPoolSize,
                maxPoolSize: maxPoolSize,
                waitTimeoutMs: waitTimeoutMs,
                enablePublisherConfirms: false);
        }

        public void Dispose()
        {
            // Intentionally empty; individual tests manage pool lifetime.
        }

        #region Concurrent access: pool never exceeds maxPoolSize

        [Fact]
        public async Task GetChannelAsync_ConcurrentAccess_Should_NeverExceedMaxPoolSize()
        {
            // Arrange
            //
            // The pool's semaphore design: GetChannelAsync always acquires a permit;
            // AddOrCloseChannelAsync releases the permit only when the channel is unhealthy/disposed.
            // Healthy returned channels keep their permit (they sit in the pool awaiting reuse).
            // This means total outstanding permits = channels in use + channels sitting in pool.
            //
            // To test concurrent access without exhausting permits, we make channels appear
            // unhealthy on return so permits are always released back.
            const int maxPoolSize = 3;
            const int concurrentCallers = 10;

            using var pool = CreatePool(minPoolSize: 0, maxPoolSize: maxPoolSize, waitTimeoutMs: 30_000);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            var peakOutstanding = 0;
            var currentOutstanding = 0;

            // Act — spawn concurrent callers that each get a channel, hold it briefly, then return it.
            var tasks = Enumerable.Range(0, concurrentCallers).Select(async _ =>
            {
                var channel = await pool.GetChannelAsync(cts.Token);

                var current = Interlocked.Increment(ref currentOutstanding);

                // Track peak concurrency.
                int snapshot;
                do
                {
                    snapshot = Volatile.Read(ref peakOutstanding);
                } while (current > snapshot && Interlocked.CompareExchange(ref peakOutstanding, current, snapshot) != snapshot);

                // Simulate brief work before returning the channel.
                await Task.Delay(20, cts.Token);

                Interlocked.Decrement(ref currentOutstanding);

                // Mark channel as unhealthy before returning so the permit is released.
                var channelMock = Mock.Get(channel);
                channelMock.SetupGet(ch => ch.IsOpen).Returns(false);
                channelMock.SetupGet(ch => ch.IsClosed).Returns(true);

                await pool.AddOrCloseChannelAsync(channel, cts.Token);
            }).ToArray();

            await Task.WhenAll(tasks);

            // Assert — the semaphore gate ensures at most maxPoolSize channels are outstanding at once.
            peakOutstanding.Should().BeLessThanOrEqualTo(maxPoolSize,
                "the pool semaphore gate should limit concurrent outstanding channels to maxPoolSize");

            // All callers should have completed successfully.
            tasks.Should().AllSatisfy(t => t.IsCompletedSuccessfully.Should().BeTrue());
        }

        [Fact]
        public async Task GetChannelAsync_Should_ThrowWhenPoolExhausted_AndTimeoutExpires()
        {
            // Arrange — pool of size 1 with a very short timeout.
            const int maxPoolSize = 1;
            const int shortTimeoutMs = 200;

            using var pool = CreatePool(minPoolSize: 0, maxPoolSize: maxPoolSize, waitTimeoutMs: shortTimeoutMs);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Acquire the only permit.
            var channel = await pool.GetChannelAsync(cts.Token);

            // Act — second caller should time out because the only permit is held.
            var act = () => pool.GetChannelAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*pool exhausted*");

            // Cleanup — mark unhealthy and return so permit is released for pool disposal.
            var channelMock = Mock.Get(channel);
            channelMock.SetupGet(ch => ch.IsOpen).Returns(false);
            channelMock.SetupGet(ch => ch.IsClosed).Returns(true);
            await pool.AddOrCloseChannelAsync(channel, cts.Token);
        }

        [Fact]
        public async Task GetChannelAsync_Should_ReuseReturnedChannels()
        {
            // Arrange — use a single specific channel mock that stays healthy.
            var specificChannel = new Mock<IModel>();
            specificChannel.SetupGet(ch => ch.IsOpen).Returns(true);
            specificChannel.SetupGet(ch => ch.IsClosed).Returns(false);

            _mockConnection
                .Setup(c => c.CreateModel())
                .Returns(specificChannel.Object);

            using var pool = CreatePool(minPoolSize: 0, maxPoolSize: 5, waitTimeoutMs: 5000);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act — get a channel, return it (healthy, stays in pool), get another.
            var first = await pool.GetChannelAsync(cts.Token);
            await pool.AddOrCloseChannelAsync(first, cts.Token);
            var second = await pool.GetChannelAsync(cts.Token);

            // Assert — should be the same instance (reused from pool).
            second.Should().BeSameAs(first);

            // Cleanup — mark unhealthy and return.
            specificChannel.SetupGet(ch => ch.IsOpen).Returns(false);
            specificChannel.SetupGet(ch => ch.IsClosed).Returns(true);
            await pool.AddOrCloseChannelAsync(second, cts.Token);
        }

        [Fact]
        public async Task AddOrCloseChannelAsync_Should_DiscardUnhealthyChannel()
        {
            // Arrange
            using var pool = CreatePool(minPoolSize: 0, maxPoolSize: 5, waitTimeoutMs: 5000);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            var channel = await pool.GetChannelAsync(cts.Token);

            // Make the channel appear unhealthy before returning it.
            var channelMock = Mock.Get(channel);
            channelMock.SetupGet(ch => ch.IsOpen).Returns(false);
            channelMock.SetupGet(ch => ch.IsClosed).Returns(true);

            // Act — return the unhealthy channel (permit should be released).
            await pool.AddOrCloseChannelAsync(channel, cts.Token);

            // Get a new channel — it should be a freshly created one since the old one was discarded.
            var freshChannel = await pool.GetChannelAsync(cts.Token);
            freshChannel.Should().NotBeSameAs(channel);

            // Cleanup
            var freshMock = Mock.Get(freshChannel);
            freshMock.SetupGet(ch => ch.IsOpen).Returns(false);
            freshMock.SetupGet(ch => ch.IsClosed).Returns(true);
            await pool.AddOrCloseChannelAsync(freshChannel, cts.Token);
        }

        [Fact]
        public async Task GetChannelAsync_ConcurrentGetAndReturn_Should_MaintainPoolIntegrity()
        {
            // Arrange — stress test: many rapid get/return cycles in parallel.
            // Channels are marked unhealthy on return so permits are released.
            const int maxPoolSize = 4;
            const int iterations = 50;
            const int parallelism = 8;

            using var pool = CreatePool(minPoolSize: 0, maxPoolSize: maxPoolSize, waitTimeoutMs: 30_000);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            // Act
            var tasks = Enumerable.Range(0, parallelism).Select(async _ =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    var ch = await pool.GetChannelAsync(cts.Token);
                    await Task.Yield(); // Force async continuation to maximize contention.

                    // Mark unhealthy so the permit is released on return.
                    var chMock = Mock.Get(ch);
                    chMock.SetupGet(c => c.IsOpen).Returns(false);
                    chMock.SetupGet(c => c.IsClosed).Returns(true);

                    await pool.AddOrCloseChannelAsync(ch, cts.Token);
                }
            }).ToArray();

            // Assert — all iterations complete without exception or deadlock.
            var act = () => Task.WhenAll(tasks);
            await act.Should().NotThrowAsync("concurrent get/return cycles should maintain pool integrity");
        }

        #endregion

        #region Pool initialization

        [Fact]
        public async Task GetChannelAsync_Should_InitializeMinPoolSizeChannels()
        {
            // Arrange
            const int minPoolSize = 3;
            _channelCreationCount = 0;

            using var pool = CreatePool(minPoolSize: minPoolSize, maxPoolSize: 5, waitTimeoutMs: 5000);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Act — trigger initialization by requesting a channel.
            var channel = await pool.GetChannelAsync(cts.Token);

            // Assert — at least minPoolSize channels should have been created during initialization.
            _channelCreationCount.Should().BeGreaterThanOrEqualTo(minPoolSize,
                "EnsurePoolInitialized should pre-create minPoolSize channels");

            // Cleanup — mark unhealthy and return.
            var channelMock = Mock.Get(channel);
            channelMock.SetupGet(ch => ch.IsOpen).Returns(false);
            channelMock.SetupGet(ch => ch.IsClosed).Returns(true);
            await pool.AddOrCloseChannelAsync(channel, cts.Token);
        }

        #endregion
    }
}
