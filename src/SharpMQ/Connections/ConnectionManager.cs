using System;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using SharpMQ.Configs;
using RabbitMQ.Client.Exceptions;
using System.Collections.Generic;
using System.Threading;
using SharpMQ.Exceptions;
using System.Linq;

namespace SharpMQ.Connections
{
    internal class ConnectionManager : IConnectionManager
    {
        private readonly object _connectionLocker = new object();

        private readonly int _reconnectCount;
        private readonly int _reconnectIntervalInSeconds;

        private readonly IConnectionFactory _connectionFactory;
        private readonly RabbitMqServerConfig _config;
        private readonly ILogger _logger;
        private volatile IConnection _connection;

        public bool IsDispatchConsumersAsyncEnabled { get; private set; }

        public ConnectionManager(RabbitMqServerConfig config,
                                 ILogger logger,
                                 bool dispatchConsumersAsync,
                                 string clientProvidedNameSuffix = null)
        {
            _config = config;
            _logger = logger;

            _reconnectCount = _config.ReconnectCount ?? ConfigConstants.Default.MAX_RECONNECT_COUNT;
            _reconnectIntervalInSeconds = _config.ReconnectIntervalInSeconds ?? ConfigConstants.Default.RECONNECT_INTERVAL_SECONDS;
            IsDispatchConsumersAsyncEnabled = dispatchConsumersAsync;


            _connectionFactory = new ConnectionFactory
            {
                UserName = _config.UserName,
                Password = _config.Password,
                VirtualHost = _config.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                DispatchConsumersAsync = dispatchConsumersAsync,
                ClientProvidedName = clientProvidedNameSuffix == null ? _config.ClientProvidedName : $"{_config.ClientProvidedName}:{clientProvidedNameSuffix}"
            };

            GetOrCreateConnection();
        }


        public IConnection GetOrCreateConnection()
        {
            if (_connection != null && _connection.IsOpen)
            {
                return _connection;
            }
            lock (_connectionLocker)
            {
                if (_connection != null && _connection.IsOpen)
                {
                    return _connection;
                }
                _connection = CreateConnectionInternal(_connectionFactory, _config.MqHosts(), _logger, _reconnectIntervalInSeconds, _reconnectCount);

                _connection.ConnectionShutdown += OnShutdown;
                _connection.ConnectionBlocked += OnBlocked;
                _connection.ConnectionUnblocked += OnUnblocked;
                _connection.CallbackException += OnCallbackException;

                return _connection;
            }
        }

        private void OnShutdown(object sender, ShutdownEventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Shutdown: {reason}", ea.ToString());
        }

        private void OnBlocked(object sender, ConnectionBlockedEventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Blocked: {Reason}", ea.Reason);
        }

        private void OnUnblocked(object sender, EventArgs ea)
        {
            _logger.LogWarning("RabbitMq Connection Unblocked");
        }

        private void OnCallbackException(object sender, EventArgs ea)
        {
            _logger.LogWarning("RabbitMq Callback Exception");
        }

        private static IConnection CreateConnectionInternal(IConnectionFactory factory, IEnumerable<AmqpTcpEndpoint> hosts, ILogger logger, int reconnectIntervalInSeconds, int reconnectCount)
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
                    Thread.Sleep(TimeSpan.FromSeconds(reconnectIntervalInSeconds));
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

        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if(_connection != null)
                {
                    _connection.ConnectionShutdown -= OnShutdown;
                    _connection.ConnectionBlocked -= OnBlocked;
                    _connection.ConnectionUnblocked -= OnUnblocked;
                    _connection.CallbackException -= OnCallbackException;
                }

                _connection?.Close();
                _connection?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }
        }
    }
}
