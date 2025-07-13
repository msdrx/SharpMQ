using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Extensions;
using SharpMQ.Serializer.Abstractions;
using RabbitMQ.Client;
using System.Collections.Generic;

namespace SharpMQ.Producers
{
    internal class Producer : IProducer
    {
        private readonly IChannelPool _channelPoolManager;
        private readonly ProducerConfig _config;
        private readonly ILogger<Producer> _logger;

        private readonly RabbitSerializer _serializer;
        private readonly RabbitSerializerOptions _defaultSerializerOptions;
        private readonly ConcurrentDictionary<Type, (string queue, string directExchange)> _messageTypeNamesCache = new ConcurrentDictionary<Type, (string queue, string directExchange)>();

        public Producer(IChannelPool channelPoolManager,
                        ProducerConfig config,
                        ILogger<Producer> logger,
                        RabbitSerializer serializer,
                        RabbitSerializerOptions defaultSerializerOptions = null)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config), "producer config is null");
            _logger = logger;
            _channelPoolManager = channelPoolManager;

            _serializer = serializer;
            _defaultSerializerOptions = defaultSerializerOptions;
        }


        public void Publish<T>(
            string exchange,
            string routingKey,
            T message,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null)
        {
            IModel channel = default;
            try
            {
                channel = _channelPoolManager.GetChannel();

                var enabled = _config.IsPublisherConfirmsEnabled();
                if (enabled) channel.ConfirmSelect();

                var props = channel.WithPersistens().WithPriority(priority);
                if (expirationMs > 100) props.Expiration = expirationMs.ToString();

                channel.BasicPublish(exchange, routingKey, mandatory: true, props, message.ToByteArray(_serializer, serializerOptions ?? _defaultSerializerOptions));


                if (enabled) channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(_config.PublisherConfirms.WaitConfirmsMilliseconds));

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Producer Error");
                throw;
            }
            finally
            {
                _channelPoolManager.AddOrCloseChannel(channel);
            }
        }

        public void Publish<T>(string exchange,
            string routingKey,
            IEnumerable<T> messages,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null,
            int batchSize = 20)
        {
            IModel channel = default;
            try
            {
                channel = _channelPoolManager.GetChannel();
                var enabled = _config.IsPublisherConfirmsEnabled();
                if (enabled) channel.ConfirmSelect();

                var props = channel.WithPersistens().WithPriority(priority);
                if (expirationMs > 100) props.Expiration = expirationMs.ToString();


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
                _logger.LogError(ex, "Producer Error while batch publish");
                throw;
            }
            finally
            {
                _channelPoolManager.AddOrCloseChannel(channel);
            }
        }

        public void Publish<T>(
            T message,
            int? priority = null,
            long expirationMs = 0,
            RabbitSerializerOptions serializerOptions = null)
        {
            var (queue, directExchange) = GetOrAddMessageTypeName<T>();
            Publish<T>(directExchange, queue, message, priority, expirationMs, serializerOptions);
        }

        public void Publish<T>(IEnumerable<T> messages,
                      int? priority = null,
                      long expirationMs = 0,
                      RabbitSerializerOptions serializerOptions = null,
                      int batchSize = 20)
        {
            var (queue, directExchange) = GetOrAddMessageTypeName<T>();
            Publish<T>(directExchange, queue, messages, priority, expirationMs, serializerOptions, batchSize);
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
                _channelPoolManager?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }
        }
    }
}