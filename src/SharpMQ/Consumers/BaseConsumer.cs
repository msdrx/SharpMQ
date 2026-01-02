using System;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using RabbitMQ.Client;
using System.Threading.Tasks;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Serializer.Abstractions;

namespace SharpMQ.Consumers
{
    internal abstract class BaseConsumer : IDisposable
    {
        protected readonly ushort _prefetchCount;
        protected readonly uint _prefetchSize;

        protected readonly ConsumerConfig _config;
        protected readonly IServiceProvider _serviceProvider;
        protected readonly IConnectionProvider _connectionProvider;
        protected readonly ILogger _logger;
        protected IModel _channel;

        protected readonly RabbitSerializer _serializer;
        protected readonly RabbitSerializerOptions _defaultSerializerOptions;

        protected BaseConsumer(IConnectionProvider connectionProvider,
                               ConsumerConfig config,
                               IServiceProvider serviceProvider,
                               ILogger logger,
                               RabbitSerializer serializer,
                               RabbitSerializerOptions defaultSerializerOptions)
        {
            _config = config;

            _logger = logger;
            _prefetchSize = _config.PrefetchSize ?? ConfigConstants.Default.PREFETCH_SIZE;
            _prefetchCount = _config.PrefechCount ?? ConfigConstants.Default.PREFETCH_COUNT;

            _connectionProvider = connectionProvider;
            _serviceProvider = serviceProvider;
            _serializer = serializer;
            _defaultSerializerOptions = defaultSerializerOptions;
        }
        protected bool IsMaxRetryReached(IBasicProperties basicProperties, out int count)
        {
            object retryCountObj = null;
            basicProperties.Headers?.TryGetValue(ConfigConstants.BasicPropertyHeaders.XRetries, out retryCountObj);

            count = retryCountObj == null ? 0 : (int)retryCountObj;
            return count >= _config.Retry.PerMessageTtlOnRetryMs.Length;
        }


        protected Task Consumer_Registered(object sender, ConsumerEventArgs ea)
        {
            var tags = _serializer.Serialize(ea.ConsumerTags);
            _logger.LogInformation("Consumer_Registered: {tags}", tags);
            return Task.CompletedTask;
        }
        protected Task Consumer_Unregistered(object sender, ConsumerEventArgs ea)
        {
            var tags = _serializer.Serialize(ea.ConsumerTags);
            _logger.LogWarning("Consumer_Unregistered: {tags}", tags);

            return Task.CompletedTask;
        }

        protected Task Consumer_Cancelled(object sender, ConsumerEventArgs ea)
        {
            var tags = _serializer.Serialize(ea.ConsumerTags);
            _logger.LogWarning("Consumer_Cancelled: {tags}", tags);

            return Task.CompletedTask;
        }


        protected Task Consumer_Shutdown(object sender, ShutdownEventArgs ea)
        {
            _logger.LogWarning("Consumer_Shutdown: {info}", ea?.ToString());
            return Task.CompletedTask;
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_channel?.IsOpen ?? false)
            {
                try
                {
                    _channel?.Close();
                }
                catch (ObjectDisposedException)
                {
                    //if already disposed its ok
                }
            }

            try
            {
                _connectionProvider?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }

        }
    }
}