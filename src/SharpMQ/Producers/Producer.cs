using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Extensions;
using SharpMQ.Serializer.Abstractions;
using RabbitMQ.Client;

namespace SharpMQ.Producers
{
    internal class Producer : IProducer
    {
        /// <summary>
        /// Minimum allowed expiration value in milliseconds. Values between 1 and this threshold are rejected.
        /// A value of 0 means no expiration.
        /// </summary>
        internal const long MinExpirationMs = 100;

        private readonly IChannelPool _channelPool;
        private readonly ProducerConfig _config;
        private readonly ILogger<Producer> _logger;

        private readonly RabbitSerializer _serializer;
        private readonly RabbitSerializerOptions _defaultSerializerOptions;
        private readonly ConcurrentDictionary<Type, (string queue, string directExchange)> _messageTypeNamesCache = new ConcurrentDictionary<Type, (string queue, string directExchange)>();

        public Producer(IChannelPool channelPool,
                        ProducerConfig config,
                        ILogger<Producer> logger,
                        RabbitSerializer serializer,
                        RabbitSerializerOptions defaultSerializerOptions = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config), "producer config is null");
            _logger = logger;
            _channelPool = channelPool;

            _serializer = serializer;
            _defaultSerializerOptions = defaultSerializerOptions;
        }

        public async Task PublishAsync<T>(
            string exchange,
            string routingKey,
            T message,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            if (expirationMs >= 1 && expirationMs < MinExpirationMs)
                throw new ArgumentOutOfRangeException(nameof(expirationMs), expirationMs, $"Expiration must be 0 (no expiration) or at least {MinExpirationMs} ms.");

            IModel channel = default;
            try
            {
                channel = await _channelPool.GetChannelAsync(cancellationToken).ConfigureAwait(false);

                var enabled = _config.IsPublisherConfirmsEnabled();

                var props = channel.WithPersistence().WithPriority(priority);
                if (expirationMs >= MinExpirationMs) props.Expiration = expirationMs.ToString();

                channel.BasicPublish(exchange, routingKey, mandatory: true, props, message.ToByteArray(_serializer, serializerOptions ?? _defaultSerializerOptions));

                if (enabled) channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(_config.PublisherConfirms.WaitConfirmsMilliseconds));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Producer Error publishing {MessageType} to exchange={Exchange} routingKey={RoutingKey}", typeof(T).Name, exchange, routingKey);
                throw;
            }
            finally
            {
                await _channelPool.AddOrCloseChannelAsync(channel, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task PublishAsync<T>(string exchange,
            string routingKey,
            IEnumerable<T> messages,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null,
            int batchSize = 20,
            CancellationToken cancellationToken = default)
        {
            if (expirationMs >= 1 && expirationMs < MinExpirationMs)
                throw new ArgumentOutOfRangeException(nameof(expirationMs), expirationMs, $"Expiration must be 0 (no expiration) or at least {MinExpirationMs} ms.");

            IModel channel = default;
            try
            {
                channel = await _channelPool.GetChannelAsync(cancellationToken).ConfigureAwait(false);
                var enabled = _config.IsPublisherConfirmsEnabled();

                var props = channel.WithPersistence().WithPriority(priority);
                if (expirationMs >= MinExpirationMs) props.Expiration = expirationMs.ToString();

                foreach (var batch in messages.Chunk(batchSize))
                {
                    var publishBatch = channel.CreateBasicPublishBatch();

                    foreach (var message in batch)
                    {
                        var body = message.ToReadOnlyMemory(_serializer, serializerOptions ?? _defaultSerializerOptions);

                        publishBatch.Add(exchange, routingKey, mandatory: true, props, body);
                    }

                    publishBatch.Publish();
                    if (enabled) channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(_config.PublisherConfirms.WaitConfirmsMilliseconds));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Producer Error publishing {MessageType} batch to exchange={Exchange} routingKey={RoutingKey}", typeof(T).Name, exchange, routingKey);
                throw;
            }
            finally
            {
                await _channelPool.AddOrCloseChannelAsync(channel, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task PublishAsync<T>(
            T message,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null,
            CancellationToken cancellationToken = default)
        {
            var (queue, directExchange) = GetOrAddMessageTypeName<T>();
            await PublishAsync<T>(directExchange, queue, message, priority, expirationMs, serializerOptions, cancellationToken).ConfigureAwait(false);
        }

        public async Task PublishAsync<T>(IEnumerable<T> messages,
                      int? priority = null,
                      long expirationMs = 0,
                      RabbitSerializerOptions serializerOptions = null,
                      int batchSize = 20,
                      CancellationToken cancellationToken = default)
        {
            var (queue, directExchange) = GetOrAddMessageTypeName<T>();
            await PublishAsync<T>(directExchange, queue, messages, priority, expirationMs, serializerOptions, batchSize, cancellationToken).ConfigureAwait(false);
        }

        private (string queue, string directExchange) GetOrAddMessageTypeName<T>()
        {
            var found = _messageTypeNamesCache.TryGetValue(typeof(T), out var cached);
            if (!found)
            {
                var type = typeof(T);
                var result = (type.FullName, type.FullName.AsDirectExchange());
                _messageTypeNamesCache.TryAdd(type, result);
                return result;
            }
            else
            {
                return cached;
            }
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
                _channelPool?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }
        }
    }
}