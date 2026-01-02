using System;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using SharpMQ.Configs;
using RabbitMQ.Client.Exceptions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpMQ.Exceptions;
using System.Linq;
using System.Diagnostics;

namespace SharpMQ.Connections
{
    public class ConnectionProvider : IConnectionProvider
    {
        private readonly SemaphoreSlim _connectionSemaphore = new SemaphoreSlim(1, 1);

        private readonly int _reconnectCount;
        private readonly int _reconnectIntervalInSeconds;

        private readonly IConnectionFactory _connectionFactory;
        private readonly RabbitMqServerConfig _config;
        private readonly ILogger _logger;
        private volatile IConnection _connection;
        private bool _disposed;
        private DateTimeOffset? _connectionEstablishedTime;
        private readonly Stopwatch _uptimeStopwatch = new Stopwatch();

        public bool IsDispatchConsumersAsyncEnabled { get; private set; }

        /// <summary>
        /// Event raised when a connection lifecycle event occurs
        /// </summary>
        public event EventHandler<ConnectionEvent> ConnectionEventOccurred;



        public ConnectionProvider(IConnectionFactory connectionFactory,
                                    ILogger logger,
                                    RabbitMqServerConfig config,
                                    bool dispatchConsumersAsync)
        {
            _config = config;
            _config.Validate();

            _logger = logger;

            _reconnectCount = _config.ReconnectCount ?? ConfigConstants.Default.MAX_RECONNECT_COUNT;
            _reconnectIntervalInSeconds = _config.ReconnectIntervalInSeconds ?? ConfigConstants.Default.RECONNECT_INTERVAL_SECONDS;
            IsDispatchConsumersAsyncEnabled = dispatchConsumersAsync;

            _connectionFactory = connectionFactory;

            // Connection will be created lazily on first use
        }

        public static ConnectionProvider Create(RabbitMqServerConfig config,
                                                ILogger<ConnectionProvider> logger,
                                                bool dispatchConsumersAsync,
                                                string clientProvidedNameSuffix = null)
        {
            var networkRecoveryInterval = config.NetworkRecoveryIntervalInSeconds ?? ConfigConstants.Default.NETWORK_RECOVERY_INTERVAL_SECONDS;

            var connectionFactory = new ConnectionFactory
            {
                UserName = config.UserName,
                Password = config.Password,
                VirtualHost = config.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(networkRecoveryInterval),
                DispatchConsumersAsync = dispatchConsumersAsync,
                ClientProvidedName = clientProvidedNameSuffix == null ? config.ClientProvidedName : $"{config.ClientProvidedName}:{clientProvidedNameSuffix}"
            };

            return new ConnectionProvider(connectionFactory, logger, config, dispatchConsumersAsync);
        }


        public async Task<IConnection> GetOrCreateAsync(CancellationToken cancellationToken = default)
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }

            await _connectionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            
            try
            {
                if (_connection != null && _connection.IsOpen)
                {
                    return _connection;
                }

                RaiseConnectionEvent(ConnectionEventType.Connecting, "Establishing connection to RabbitMQ");

                _connection = await CreateConnectionInternalAsync(_connectionFactory, _config.MqHosts(), _logger, _reconnectIntervalInSeconds, _reconnectCount, cancellationToken).ConfigureAwait(false);

                _connection.ConnectionShutdown += OnShutdown;
                _connection.ConnectionBlocked += OnBlocked;
                _connection.ConnectionUnblocked += OnUnblocked;
                _connection.CallbackException += OnCallbackException;

                _connectionEstablishedTime = DateTimeOffset.UtcNow;
                _uptimeStopwatch.Restart();

                RaiseConnectionEvent(ConnectionEventType.Connected, $"Successfully connected to RabbitMQ: {_connection.Endpoint}");

                return _connection;
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private void OnShutdown(object sender, ShutdownEventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Shutdown: {reason}", ea.ToString());
            _uptimeStopwatch.Stop();
            RaiseConnectionEvent(ConnectionEventType.Shutdown, $"Connection shutdown: {ea}");
        }

        private void OnBlocked(object sender, ConnectionBlockedEventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Blocked: {Reason}", ea.Reason);
            RaiseConnectionEvent(ConnectionEventType.Blocked, $"Connection blocked: {ea.Reason}");
        }

        private void OnUnblocked(object sender, EventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Unblocked");
            RaiseConnectionEvent(ConnectionEventType.Unblocked, "Connection unblocked");
        }

        private void OnCallbackException(object sender, EventArgs ea)
        {
            _logger.LogWarning("RabbitMq Callback Exception");
            var callbackEx = ea as CallbackExceptionEventArgs;
            RaiseConnectionEvent(ConnectionEventType.CallbackException, "Callback exception occurred", callbackEx?.Exception);
        }

        /// <summary>
        /// Gets the current health status of the connection
        /// </summary>
        public Task<ConnectionHealth> GetHealthAsync(CancellationToken cancellationToken = default)
        {
            if (_connection == null)
            {
                return Task.FromResult(ConnectionHealth.Disconnected());
            }

            if (_connection.IsOpen)
            {
                var uptime = _uptimeStopwatch.IsRunning ? _uptimeStopwatch.Elapsed : TimeSpan.Zero;
                return Task.FromResult(ConnectionHealth.Healthy(uptime));
            }

            return Task.FromResult(ConnectionHealth.Unhealthy("Connection is closed"));
        }

        /// <summary>
        /// Raises a connection lifecycle event
        /// </summary>
        private void RaiseConnectionEvent(ConnectionEventType eventType, string message, Exception exception = null)
        {
            try
            {
                var connectionEvent = new ConnectionEvent(eventType, message, exception);
                ConnectionEventOccurred?.Invoke(this, connectionEvent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error raising connection event: {EventType}", eventType);
            }
        }

        private static async Task<IConnection> CreateConnectionInternalAsync(IConnectionFactory factory, IEnumerable<AmqpTcpEndpoint> hosts, ILogger logger, int reconnectIntervalInSeconds, int reconnectCount, CancellationToken cancellationToken)
        {
            int tryCount = 0;
            do
            {
                try
                {
                    return factory.CreateConnection(hosts.ToList());
                }
                catch (BrokerUnreachableException ex)
                {
                    logger.LogError(ex, "Error while try to connect one of rabbitmq hosts. reconnectCount: {reconnectCount}, Hosts: {hosts} ", tryCount, string.Join(",", hosts.ToList()));

                    tryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(reconnectIntervalInSeconds), cancellationToken).ConfigureAwait(false);
                    if (tryCount > reconnectCount)
                    {
                        throw;
                    }
                }
            }
            while (reconnectCount >= tryCount);
            throw new RabbitMqException("Error while try to connect one of rabbitmq hosts: " + string.Join(",", hosts.ToList()));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        protected virtual async ValueTask DisposeAsyncCore()
        {
            if (_disposed) return;

            RaiseConnectionEvent(ConnectionEventType.Disposing, "Connection manager is being disposed");

            try
            {
                if (_connection != null)
                {
                    _connection.ConnectionShutdown -= OnShutdown;
                    _connection.ConnectionBlocked -= OnBlocked;
                    _connection.ConnectionUnblocked -= OnUnblocked;
                    _connection.CallbackException -= OnCallbackException;
                }

                if (_connection != null && _connection.IsOpen)
                {
                    await Task.Run(() =>
                    {
                        _connection.Close();
                    }).ConfigureAwait(false);
                }

                _connection?.Dispose();
                _connectionSemaphore?.Dispose();
                _uptimeStopwatch?.Stop();

                _disposed = true;

                RaiseConnectionEvent(ConnectionEventType.Disposed, "Connection manager disposed");
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during async disposal");
                throw;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                RaiseConnectionEvent(ConnectionEventType.Disposing, "Connection manager is being disposed");

                try
                {
                    if (_connection != null)
                    {
                        _connection.ConnectionShutdown -= OnShutdown;
                        _connection.ConnectionBlocked -= OnBlocked;
                        _connection.ConnectionUnblocked -= OnUnblocked;
                        _connection.CallbackException -= OnCallbackException;
                    }

                    _connection?.Close();
                    _connection?.Dispose();
                    _connectionSemaphore?.Dispose();
                    _uptimeStopwatch?.Stop();

                    RaiseConnectionEvent(ConnectionEventType.Disposed, "Connection manager disposed");
                }
                catch (ObjectDisposedException)
                {
                    //if already disposed its ok
                }

                _disposed = true;
            }
        }
    }
}
