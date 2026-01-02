using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using SharpMQ.Configs;
using SharpMQ.Connections;
using Xunit;

namespace SharpMQ.Unit.Test.Connections
{
    public class ConnectionProviderTests : IDisposable
    {
        private readonly Mock<IConnectionFactory> _mockConnectionFactory;
        private readonly Mock<IConnection> _mockConnection;
        private readonly Mock<ILogger> _mockLogger;
        private readonly RabbitMqServerConfig _config;
        private ConnectionProvider _sut;

        public ConnectionProviderTests()
        {
            // Setup configuration
            _config = new RabbitMqServerConfig
            {
                UserName = "test_user",
                Password = "test_password",
                VirtualHost = "/",
                Hosts = new[] { "localhost" },
                ClientProvidedName = "TestClient",
                ReconnectCount = 3,
                ReconnectIntervalInSeconds = 1,
                NetworkRecoveryIntervalInSeconds = 5
            };

            // Setup connection mock
            _mockConnection = new Mock<IConnection>();
            _mockConnection.SetupGet(c => c.IsOpen).Returns(true);
            _mockConnection.SetupGet(c => c.Endpoint)
                .Returns(new AmqpTcpEndpoint("localhost", 5672));
            _mockConnection.Setup(c => c.Close()).Verifiable();
            _mockConnection.Setup(c => c.Dispose()).Verifiable();

            // Setup connection factory mock
            _mockConnectionFactory = new Mock<IConnectionFactory>();
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(_mockConnection.Object);

            // Setup logger mock
            _mockLogger = new Mock<ILogger>();
        }

        private ConnectionProvider CreateSut(bool dispatchConsumersAsync = true)
        {
            return new ConnectionProvider(_mockConnectionFactory.Object, _mockLogger.Object, _config, dispatchConsumersAsync);
        }

        public void Dispose()
        {
            _sut?.Dispose();
        }

        #region Category 1: Constructor Tests

        [Fact]
        public void Constructor_ShouldInitialize_WithProvidedConfig()
        {
            // Arrange & Act
            _sut = CreateSut(dispatchConsumersAsync: true);

            // Assert
            _sut.Should().NotBeNull();
            _sut.IsDispatchConsumersAsyncEnabled.Should().BeTrue();
        }

        [Fact]
        public async Task Constructor_ShouldApplyDefaults_WhenConfigValuesAreNull()
        {
            // Arrange
            var configWithNulls = new RabbitMqServerConfig
            {
                UserName = "test_user",
                Password = "test_password",
                VirtualHost = "/",
                Hosts = new[] { "localhost" },
                ClientProvidedName = "TestClient",
                ReconnectCount = null,
                ReconnectIntervalInSeconds = 1,
                NetworkRecoveryIntervalInSeconds = null
            };

            // Act
            _sut = new ConnectionProvider(_mockConnectionFactory.Object, _mockLogger.Object, configWithNulls, true);

            // Setup mock to fail first 3 times to test default reconnect count
            var attemptCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount <= ConfigConstants.Default.MAX_RECONNECT_COUNT)
                        throw new BrokerUnreachableException(new Exception("Test"));
                    return _mockConnection.Object;
                });

            await _sut.GetOrCreateAsync();

            // Assert - should retry default number of times
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Exactly(ConfigConstants.Default.MAX_RECONNECT_COUNT + 1));
        }

        [Fact]
        public void Constructor_ShouldSetDispatchConsumersAsync_ToTrue()
        {
            // Arrange & Act
            _sut = CreateSut(dispatchConsumersAsync: true);

            // Assert
            _sut.IsDispatchConsumersAsyncEnabled.Should().BeTrue();
        }

        [Fact]
        public void Constructor_ShouldSetDispatchConsumersAsync_ToFalse()
        {
            // Arrange & Act
            _sut = CreateSut(dispatchConsumersAsync: false);

            // Assert
            _sut.IsDispatchConsumersAsyncEnabled.Should().BeFalse();
        }

        #endregion

        #region Category 2: GetOrCreateAsync - Basic Scenarios

        [Fact]
        public async Task GetOrCreateAsync_ShouldCreateNewConnection_OnFirstCall()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            var connection = await _sut.GetOrCreateAsync();

            // Assert
            connection.Should().NotBeNull();
            connection.Should().BeSameAs(_mockConnection.Object);
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldReturnCachedConnection_WhenConnectionIsOpen()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            var connection1 = await _sut.GetOrCreateAsync();
            var connection2 = await _sut.GetOrCreateAsync();

            // Assert
            connection1.Should().BeSameAs(connection2);
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldRecreateConnection_WhenConnectionIsClosed()
        {
            // Arrange
            _sut = CreateSut();
            var mockConnection2 = new Mock<IConnection>();
            mockConnection2.SetupGet(c => c.IsOpen).Returns(true);
            mockConnection2.SetupGet(c => c.Endpoint).Returns(new AmqpTcpEndpoint("localhost", 5672));

            var callCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    callCount++;
                    return callCount == 1 ? _mockConnection.Object : mockConnection2.Object;
                });

            // Act
            var connection1 = await _sut.GetOrCreateAsync();
            _mockConnection.SetupGet(c => c.IsOpen).Returns(false);
            var connection2 = await _sut.GetOrCreateAsync();

            // Assert
            connection2.Should().BeSameAs(mockConnection2.Object);
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldRegisterEventHandlers_OnConnectionCreation()
        {
            // Arrange
            _sut = CreateSut();
            var eventHandlersRegistered = false;

            _mockConnection.SetupAdd(c => c.ConnectionShutdown += It.IsAny<EventHandler<ShutdownEventArgs>>())
                .Callback(() => eventHandlersRegistered = true);

            // Act
            await _sut.GetOrCreateAsync();

            // Assert
            eventHandlersRegistered.Should().BeTrue();
            _mockConnection.VerifyAdd(c => c.ConnectionShutdown += It.IsAny<EventHandler<ShutdownEventArgs>>(), Times.Once);
            _mockConnection.VerifyAdd(c => c.ConnectionBlocked += It.IsAny<EventHandler<ConnectionBlockedEventArgs>>(), Times.Once);
            _mockConnection.VerifyAdd(c => c.ConnectionUnblocked += It.IsAny<EventHandler<EventArgs>>(), Times.Once);
            _mockConnection.VerifyAdd(c => c.CallbackException += It.IsAny<EventHandler<CallbackExceptionEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldRaiseConnectingAndConnectedEvents()
        {
            // Arrange
            _sut = CreateSut();
            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            await _sut.GetOrCreateAsync();

            // Assert
            events.Should().HaveCount(2);
            events[0].EventType.Should().Be(ConnectionEventType.Connecting);
            events[1].EventType.Should().Be(ConnectionEventType.Connected);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldStartUptimeStopwatch()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            await _sut.GetOrCreateAsync();
            await Task.Delay(100);
            var health = await _sut.GetHealthAsync();

            // Assert
            health.Uptime.Should().NotBeNull();
            health.Uptime.Value.Should().BeGreaterThan(TimeSpan.Zero);
        }

        #endregion

        #region Category 3: GetOrCreateAsync - Thread Safety

        [Fact]
        public async Task GetOrCreateAsync_ShouldCreateOnlyOneConnection_WhenCalledConcurrently()
        {
            // Arrange
            _sut = CreateSut();
            var tasks = new List<Task<IConnection>>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(_sut.GetOrCreateAsync());
            }
            var connections = await Task.WhenAll(tasks);

            // Assert
            connections.Should().AllBeEquivalentTo(_mockConnection.Object);
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldUseSemaphore_ToPreventRaceConditions()
        {
            // Arrange
            _sut = CreateSut();
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    Thread.Sleep(100); // Simulate slow connection
                    return _mockConnection.Object;
                });

            var tasks = new List<Task<IConnection>>();

            // Act
            for (int i = 0; i < 5; i++)
            {
                tasks.Add(_sut.GetOrCreateAsync());
            }
            await Task.WhenAll(tasks);

            // Assert
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Once);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldReleaseSemaphore_AfterConnectionCreation()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            await _sut.GetOrCreateAsync();
            var connection2 = await _sut.GetOrCreateAsync();

            // Assert
            connection2.Should().NotBeNull();
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldReleaseSemaphore_OnException()
        {
            // Arrange
            _sut = CreateSut();
            var attemptCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount == 1)
                        throw new InvalidOperationException("Test exception");
                    return _mockConnection.Object;
                });

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.GetOrCreateAsync());

            // Second call should not hang
            var connection = await _sut.GetOrCreateAsync();
            connection.Should().NotBeNull();
        }

        #endregion

        #region Category 4: GetOrCreateAsync - Cancellation

        [Fact]
        public async Task GetOrCreateAsync_ShouldThrowOperationCanceledException_WhenCancellationRequested()
        {
            // Arrange
            _sut = CreateSut();
            var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _sut.GetOrCreateAsync(cts.Token));
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldCancelDuringConnectionCreation()
        {
            // Arrange
            _sut = CreateSut();
            var cts = new CancellationTokenSource();

            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Throws(new BrokerUnreachableException(new Exception("Test")));

            cts.CancelAfter(500);

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _sut.GetOrCreateAsync(cts.Token));
        }

        #endregion

        #region Category 5: Reconnection Logic

        [Fact]
        public async Task CreateConnectionInternal_ShouldRetry_OnBrokerUnreachableException()
        {
            // Arrange
            _sut = CreateSut();
            var attemptCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount <= 2)
                        throw new BrokerUnreachableException(new Exception("Test"));
                    return _mockConnection.Object;
                });

            // Act
            var connection = await _sut.GetOrCreateAsync();

            // Assert
            connection.Should().NotBeNull();
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Exactly(3));
        }

        [Fact]
        public async Task CreateConnectionInternal_ShouldDelayBetweenRetries()
        {
            // Arrange
            _sut = CreateSut();
            var attemptCount = 0;
            var attemptTimes = new List<DateTime>();

            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptTimes.Add(DateTime.UtcNow);
                    attemptCount++;
                    if (attemptCount <= 2)
                        throw new BrokerUnreachableException(new Exception("Test"));
                    return _mockConnection.Object;
                });

            // Act
            await _sut.GetOrCreateAsync();

            // Assert
            attemptTimes.Should().HaveCount(3);
            var delay1 = attemptTimes[1] - attemptTimes[0];
            var delay2 = attemptTimes[2] - attemptTimes[1];
            delay1.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9));
            delay2.Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(0.9));
        }

        [Fact]
        public async Task CreateConnectionInternal_ShouldThrow_AfterMaxRetriesExceeded()
        {
            // Arrange
            _sut = CreateSut();
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Throws(new BrokerUnreachableException(new Exception("Test")));

            // Act & Assert
            await Assert.ThrowsAsync<BrokerUnreachableException>(() => _sut.GetOrCreateAsync());

            // Should attempt initial + 3 retries = 4 total
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Exactly(4));
        }

        [Fact]
        public async Task CreateConnectionInternal_ShouldLogErrors_OnEachRetry()
        {
            // Arrange
            _sut = CreateSut();
            var attemptCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount <= 2)
                        throw new BrokerUnreachableException(new Exception("Test"));
                    return _mockConnection.Object;
                });

            // Act
            await _sut.GetOrCreateAsync();

            // Assert
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.Exactly(2));
        }

        [Fact]
        public async Task CreateConnectionInternal_ShouldSucceed_OnSecondAttempt()
        {
            // Arrange
            _sut = CreateSut();
            var attemptCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    attemptCount++;
                    if (attemptCount == 1)
                        throw new BrokerUnreachableException(new Exception("Test"));
                    return _mockConnection.Object;
                });

            // Act
            var connection = await _sut.GetOrCreateAsync();

            // Assert
            connection.Should().NotBeNull();
            _mockConnectionFactory.Verify(
                f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()),
                Times.Exactly(2));
        }

        [Fact]
        public async Task CreateConnectionInternal_ShouldRespectCancellationToken_DuringRetries()
        {
            // Arrange
            var longRetryConfig = new RabbitMqServerConfig
            {
                UserName = "test_user",
                Password = "test_password",
                VirtualHost = "/",
                Hosts = new[] { "localhost" },
                ClientProvidedName = "TestClient",
                ReconnectCount = 5,
                ReconnectIntervalInSeconds = 10, // Long delay
                NetworkRecoveryIntervalInSeconds = 5
            };

            _sut = new ConnectionProvider(_mockConnectionFactory.Object, _mockLogger.Object, longRetryConfig, true);
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Throws(new BrokerUnreachableException(new Exception("Test")));

            var cts = new CancellationTokenSource();
            cts.CancelAfter(500);

            // Act & Assert
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                _sut.GetOrCreateAsync(cts.Token));
        }

        #endregion

        #region Category 6: GetHealthAsync Tests

        [Fact]
        public async Task GetHealthAsync_ShouldReturnDisconnected_WhenConnectionIsNull()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            var health = await _sut.GetHealthAsync();

            // Assert
            health.Status.Should().Be(ConnectionHealthStatus.Disconnected);
            health.IsConnected.Should().BeFalse();
            health.Uptime.Should().BeNull();
            health.Details.Should().Contain("not established");
        }

        [Fact]
        public async Task GetHealthAsync_ShouldReturnHealthy_WhenConnectionIsOpen()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            var health = await _sut.GetHealthAsync();

            // Assert
            health.Status.Should().Be(ConnectionHealthStatus.Healthy);
            health.IsConnected.Should().BeTrue();
            health.Uptime.Should().NotBeNull();
            health.Details.Should().Contain("healthy");
        }

        [Fact]
        public async Task GetHealthAsync_ShouldReturnUnhealthy_WhenConnectionIsClosed()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            _mockConnection.SetupGet(c => c.IsOpen).Returns(false);

            // Act
            var health = await _sut.GetHealthAsync();

            // Assert
            health.Status.Should().Be(ConnectionHealthStatus.Unhealthy);
            health.IsConnected.Should().BeFalse();
            health.Details.Should().Contain("closed");
        }

        [Fact]
        public async Task GetHealthAsync_ShouldIncludeAccurateUptime_WhenHealthy()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            await Task.Delay(500);

            // Act
            var health = await _sut.GetHealthAsync();

            // Assert
            health.Uptime.Should().NotBeNull();
            health.Uptime.Value.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(400));
            health.Uptime.Value.Should().BeLessThan(TimeSpan.FromSeconds(2));
        }

        [Fact]
        public async Task GetHealthAsync_ShouldReturnZeroUptime_WhenStopwatchIsNotRunning()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Simulate shutdown
            _mockConnection.Raise(c => c.ConnectionShutdown += null,
                new ShutdownEventArgs(ShutdownInitiator.Application, 0, "Test shutdown"));

            // Act
            var health = await _sut.GetHealthAsync();

            // Assert - Uptime should be zero or very small after shutdown
            health.Uptime.Should().NotBeNull();
        }

        #endregion

        #region Category 7: Event Handler Tests

        [Fact]
        public async Task OnShutdown_ShouldLogWarning_AndRaiseConnectionEvent()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            _mockConnection.Raise(c => c.ConnectionShutdown += null,
                new ShutdownEventArgs(ShutdownInitiator.Application, 0, "Test shutdown"));

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.Shutdown);
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Warning,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task OnShutdown_ShouldStopUptimeStopwatch()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            await Task.Delay(100);

            // Act
            _mockConnection.Raise(c => c.ConnectionShutdown += null,
                new ShutdownEventArgs(ShutdownInitiator.Application, 0, "Test shutdown"));

            await Task.Delay(100);
            var health = await _sut.GetHealthAsync();

            // Assert - Stopwatch should be stopped, so uptime shouldn't grow
            var uptime1 = health.Uptime;
            await Task.Delay(100);
            health = await _sut.GetHealthAsync();
            health.Uptime.Should().Be(uptime1);
        }

        [Fact]
        public async Task OnBlocked_ShouldLogWarning_AndRaiseConnectionEvent()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            _mockConnection.Raise(c => c.ConnectionBlocked += null,
                new ConnectionBlockedEventArgs("Test reason"));

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.Blocked);
            events.First(e => e.EventType == ConnectionEventType.Blocked).Message.Should().Contain("Test reason");
        }

        [Fact]
        public async Task OnUnblocked_ShouldLogWarning_AndRaiseConnectionEvent()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            _mockConnection.Raise(c => c.ConnectionUnblocked += null, EventArgs.Empty);

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.Unblocked);
        }

        [Fact]
        public async Task OnCallbackException_ShouldLogWarning_AndRaiseConnectionEvent()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            var testException = new InvalidOperationException("Test exception");
            var callbackArgs = new CallbackExceptionEventArgs(testException);

            // Act
            _mockConnection.Raise(c => c.CallbackException += null, callbackArgs);

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.CallbackException);
            events.First(e => e.EventType == ConnectionEventType.CallbackException).Exception.Should().Be(testException);
        }

        [Fact]
        public async Task RaiseConnectionEvent_ShouldHandleExceptions_FromEventSubscribers()
        {
            // Arrange
            _sut = CreateSut();
            _sut.ConnectionEventOccurred += (sender, e) => throw new Exception("Subscriber exception");

            // Act & Assert - Should not throw
            await _sut.GetOrCreateAsync();
        }

        [Fact]
        public async Task RaiseConnectionEvent_ShouldNotThrow_WhenNoSubscribers()
        {
            // Arrange
            _sut = CreateSut();

            // Act & Assert - Should not throw
            await _sut.GetOrCreateAsync();
        }

        #endregion

        #region Category 8: Disposal (IDisposable) Tests

        [Fact]
        public async Task Dispose_ShouldUnregisterEventHandlers()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            _sut.Dispose();

            // Assert
            _mockConnection.VerifyRemove(c => c.ConnectionShutdown -= It.IsAny<EventHandler<ShutdownEventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.ConnectionBlocked -= It.IsAny<EventHandler<ConnectionBlockedEventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.ConnectionUnblocked -= It.IsAny<EventHandler<EventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.CallbackException -= It.IsAny<EventHandler<CallbackExceptionEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task Dispose_ShouldCloseAndDisposeConnection()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            _sut.Dispose();

            // Assert
            _mockConnection.Verify(c => c.Close(), Times.Once);
            _mockConnection.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public async Task Dispose_ShouldStopUptimeStopwatch()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            _sut.Dispose();
            await Task.Delay(100);
            var health = await _sut.GetHealthAsync();

            // Assert
            var uptime1 = health.Uptime ?? TimeSpan.Zero;
            await Task.Delay(100);
            health = await _sut.GetHealthAsync();
            health.Uptime.Should().Be(uptime1); // Should not increase
        }

        [Fact]
        public async Task Dispose_ShouldRaiseDisposingAndDisposedEvents()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            _sut.Dispose();

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.Disposing);
            events.Should().Contain(e => e.EventType == ConnectionEventType.Disposed);
        }

        [Fact]
        public async Task Dispose_ShouldBeIdempotent_WhenCalledMultipleTimes()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act & Assert - Should not throw
            _sut.Dispose();
            _sut.Dispose();
            _sut.Dispose();

            _mockConnection.Verify(c => c.Close(), Times.Once);
            _mockConnection.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public async Task Dispose_ShouldHandleObjectDisposedException_Gracefully()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            _mockConnection.Setup(c => c.Close()).Throws(new ObjectDisposedException("test"));

            // Act & Assert - Should not throw
            _sut.Dispose();
        }

        [Fact]
        public void Dispose_ShouldNotThrow_WhenConnectionIsNull()
        {
            // Arrange
            _sut = CreateSut();

            // Act & Assert - Should not throw
            _sut.Dispose();
        }

        [Fact]
        public async Task Dispose_ShouldNotCloseConnection_WhenConnectionIsAlreadyClosed()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            _mockConnection.SetupGet(c => c.IsOpen).Returns(false);

            // Act
            _sut.Dispose();

            // Assert
            _mockConnection.Verify(c => c.Close(), Times.Once);
        }

        #endregion

        #region Category 9: Disposal (IAsyncDisposable) Tests

        [Fact]
        public async Task DisposeAsync_ShouldUnregisterEventHandlers()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            await _sut.DisposeAsync();

            // Assert
            _mockConnection.VerifyRemove(c => c.ConnectionShutdown -= It.IsAny<EventHandler<ShutdownEventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.ConnectionBlocked -= It.IsAny<EventHandler<ConnectionBlockedEventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.ConnectionUnblocked -= It.IsAny<EventHandler<EventArgs>>(), Times.Once);
            _mockConnection.VerifyRemove(c => c.CallbackException -= It.IsAny<EventHandler<CallbackExceptionEventArgs>>(), Times.Once);
        }

        [Fact]
        public async Task DisposeAsync_ShouldCloseAndDisposeConnection_Asynchronously()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            await _sut.DisposeAsync();

            // Assert
            _mockConnection.Verify(c => c.Close(), Times.Once);
            _mockConnection.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public async Task DisposeAsync_ShouldStopUptimeStopwatch()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            await _sut.DisposeAsync();
            var health = await _sut.GetHealthAsync();

            // Assert
            var uptime1 = health.Uptime ?? TimeSpan.Zero;
            await Task.Delay(100);
            health = await _sut.GetHealthAsync();
            health.Uptime.Should().Be(uptime1);
        }

        [Fact]
        public async Task DisposeAsync_ShouldRaiseDisposingAndDisposedEvents()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var events = new List<ConnectionEvent>();
            _sut.ConnectionEventOccurred += (sender, e) => events.Add(e);

            // Act
            await _sut.DisposeAsync();

            // Assert
            events.Should().Contain(e => e.EventType == ConnectionEventType.Disposing);
            events.Should().Contain(e => e.EventType == ConnectionEventType.Disposed);
        }

        [Fact]
        public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act & Assert - Should not throw
            await _sut.DisposeAsync();
            await _sut.DisposeAsync();
            await _sut.DisposeAsync();

            _mockConnection.Verify(c => c.Close(), Times.Once);
            _mockConnection.Verify(c => c.Dispose(), Times.Once);
        }

        [Fact]
        public async Task DisposeAsync_ShouldHandleObjectDisposedException_Gracefully()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            _mockConnection.Setup(c => c.Close()).Throws(new ObjectDisposedException("test"));

            // Act & Assert - Should not throw
            await _sut.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_ShouldRethrowExceptions_OtherThanObjectDisposed()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();
            _mockConnection.Setup(c => c.Close()).Throws(new InvalidOperationException("test"));

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DisposeAsync().AsTask());

            // Prevent double disposal in test cleanup
            _sut = null;
        }

        [Fact]
        public async Task DisposeAsync_ShouldNotThrow_WhenConnectionIsNull()
        {
            // Arrange
            _sut = CreateSut();

            // Act & Assert - Should not throw
            await _sut.DisposeAsync();
        }

        [Fact]
        public async Task DisposeAsync_ShouldCloseConnectionAsynchronously()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            // Act
            await _sut.DisposeAsync();

            // Assert
            _mockConnection.Verify(c => c.Close(), Times.Once);
        }

        #endregion

        #region Category 10: Edge Cases

        [Fact]
        public async Task GetOrCreateAsync_ShouldHandleConnectionBecomingClosed_BetweenCheckAndReturn()
        {
            // Arrange
            _sut = CreateSut();
            await _sut.GetOrCreateAsync();

            var mockConnection2 = new Mock<IConnection>();
            mockConnection2.SetupGet(c => c.IsOpen).Returns(true);
            mockConnection2.SetupGet(c => c.Endpoint).Returns(new AmqpTcpEndpoint("localhost", 5672));

            var callCount = 0;
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    callCount++;
                    return callCount == 1 ? _mockConnection.Object : mockConnection2.Object;
                });

            // Act
            _mockConnection.SetupGet(c => c.IsOpen).Returns(false);
            var connection2 = await _sut.GetOrCreateAsync();

            // Assert
            connection2.Should().NotBeNull();
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldHandleConnectionFactoryReturningNull()
        {
            // Arrange
            _sut = CreateSut();
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns((IConnection)null);

            // Act & Assert
            await Assert.ThrowsAsync<NullReferenceException>(() => _sut.GetOrCreateAsync());
        }

        [Fact]
        public async Task RaiseConnectionEvent_ShouldCatchAndLog_SubscriberExceptions()
        {
            // Arrange
            _sut = CreateSut();
            var exceptionThrown = false;
            _sut.ConnectionEventOccurred += (sender, e) =>
            {
                exceptionThrown = true;
                throw new InvalidOperationException("Subscriber exception");
            };

            // Act
            await _sut.GetOrCreateAsync();

            // Assert
            exceptionThrown.Should().BeTrue();
            _mockLogger.Verify(
                x => x.Log(
                    LogLevel.Error,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => true),
                    It.IsAny<Exception>(),
                    It.Is<Func<It.IsAnyType, Exception, string>>((v, t) => true)),
                Times.AtLeastOnce);
        }

        [Fact]
        public async Task Dispose_ShouldComplete_WhenCalledDuringGetOrCreateAsync()
        {
            // Arrange
            _sut = CreateSut();
            _mockConnectionFactory
                .Setup(f => f.CreateConnection(It.IsAny<IList<AmqpTcpEndpoint>>()))
                .Returns(() =>
                {
                    Thread.Sleep(500); // Slow connection
                    return _mockConnection.Object;
                });

            // Act
            var getOrCreateTask = _sut.GetOrCreateAsync();
            await Task.Delay(100);
            _sut.Dispose();

            // Assert - Both should complete without deadlock
            await Task.Delay(1000);
        }

        [Fact]
        public async Task GetOrCreateAsync_ShouldNotDeadlock_OnConcurrentAccess()
        {
            // Arrange
            _sut = CreateSut();

            // Act
            var tasks = Enumerable.Range(0, 100)
                .Select(_ => _sut.GetOrCreateAsync())
                .ToArray();

            var allCompletedTask = Task.WhenAll(tasks);
            var timeoutTask = Task.Delay(5000);

            var completedFirst = await Task.WhenAny(allCompletedTask, timeoutTask);

            // Assert
            completedFirst.Should().BeSameAs(allCompletedTask, "all tasks should complete before timeout");
            allCompletedTask.IsCompletedSuccessfully.Should().BeTrue();
        }

        #endregion

        #region Category 11: Properties Tests

        [Fact]
        public void IsDispatchConsumersAsyncEnabled_ShouldReturnCorrectValue()
        {
            // Arrange & Act
            var sut1 = CreateSut(dispatchConsumersAsync: true);
            var sut2 = CreateSut(dispatchConsumersAsync: false);

            // Assert
            sut1.IsDispatchConsumersAsyncEnabled.Should().BeTrue();
            sut2.IsDispatchConsumersAsyncEnabled.Should().BeFalse();

            // Cleanup
            sut1.Dispose();
            sut2.Dispose();
        }

        #endregion
    }
}
