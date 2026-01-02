using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;
using System.Threading.Tasks;
using SharpMQ.Abstractions;
using SharpMQ.Connections;
using SharpMQ.Configs;
using SharpMQ.Serializer.Abstractions;
using SharpMQ.Exceptions;
using SharpMQ.Extensions;

namespace SharpMQ.Consumers
{
    internal class Consumer<T> : BaseConsumer, IConsumer<T> where T : class
    {
        private AsyncEventingBasicConsumer _asyncEventingBasicConsumer;
        private readonly SemaphoreSlim _channelSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _lockConsumer = new object();
        private bool _isInitialized;

        public Consumer(IConnectionProvider connectionProvider,
                        ConsumerConfig config,
                        IServiceProvider serviceProvider,
                        ILogger logger,
                        RabbitSerializer serializer,
                        RabbitSerializerOptions defaultSerializerOptions = null)
                        : base(connectionProvider, config, serviceProvider, logger, serializer, defaultSerializerOptions)
        {
            // Lazy initialization - channel and consumer will be created on first use
            _isInitialized = false;
        }

        public async Task SubscribeAsync(
            Func<T, IServiceProvider, MessageContext, Task> onDequeue,
            Func<T, IServiceProvider, MessageContext, Exception, Task> onException,
            RabbitSerializerOptions serializerOptions = null)
        {
            if (!_connectionProvider.IsDispatchConsumersAsyncEnabled)
            {
                throw new ConsumerException("DispatchConsumersAsync is disabled when consumer built");
            }

            await EnsureInitialized();

            _asyncEventingBasicConsumer.Registered += Consumer_Registered;
            _asyncEventingBasicConsumer.Unregistered += Consumer_Unregistered;

            _asyncEventingBasicConsumer.ConsumerCancelled += Consumer_Cancelled;
            _asyncEventingBasicConsumer.Shutdown += Consumer_Shutdown;

            _asyncEventingBasicConsumer.Received += async (s, ea) =>
            {
                bool isLastTry = true;
                MessageContext msgContext = default;
                T message = default;

                try
                {
                    message = ea.Body.Span.ToObject<T>(_serializer, serializerOptions ?? _defaultSerializerOptions);

                    isLastTry = !_config.IsRetryEnabled() || IsMaxRetryReached(ea.BasicProperties, out _);
                    msgContext = new MessageContext(s, isLastTry, ea?.ToMessageBasicDeliverEventArgs());

                    if (_serviceProvider == null)
                    {
                        await onDequeue(message, null, msgContext);
                    }
                    else
                    {
                        using (var scope = _serviceProvider.CreateScope())
                        {
                            await onDequeue(message, scope.ServiceProvider, msgContext);
                        }
                    }

                    _channel.BasicAck(ea.DeliveryTag, multiple: false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "RabbitMQ Consumer error: isLastTryOnError={isLastTryOnError} {tag}", isLastTry, msgContext.BasicDeliverEventArgs.ConsumerTag);
                    await OnException(msgContext, ea, message, ex, onException);
                }

            };

            _asyncEventingBasicConsumer.StartConsume(_channel, _config, _prefetchSize, _prefetchCount);
        }

        public async Task<bool> CreateNewChannelAndStartConsume(bool rethrowError = false)
        {
            try
            {
                if (!(_channel == null || _channel.IsClosed))
                {
                    _logger.LogWarning("CreateNewChannelAndStartConsume: channel is not closed and can't open new");
                    return false;
                }

                if (_asyncEventingBasicConsumer is null)
                {
                    _logger.LogError("CreateNewChannelAndStartConsume: asyncEventingBasicConsumer არის null");
                    return false;
                }
                else
                {
                    _channelSemaphore.Wait();
                    try
                    {
                        if (!(_channel == null || _channel.IsClosed))
                        {
                            _logger.LogWarning("CreateNewChannelAndStartConsume: channel is not closed and can't open new");
                            return false;
                        }

                        var connection = await _connectionProvider.GetOrCreateAsync();
                        _channel = connection.CreateModel().ConfigureConsumerChannel<T>(_config);
                    }
                    finally
                    {
                        _channelSemaphore.Release();
                    }

                    return StartConsume(rethrowError);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CreateNewChannelAndStartConsume error");
                if (rethrowError)
                {
                    throw;
                }
                else
                {
                    return false;
                }
            }

        }

        public bool StartConsume(bool rethrowError = false)
        {
            try
            {
                if (GetConsumerTags().Any())
                {
                    _logger.LogWarning("StartConsume: ConsumerTags is empty");
                    return false;
                }

                if (_asyncEventingBasicConsumer is null)
                {
                    _logger.LogError("StartConsume: asyncEventingBasicConsumer is null");
                    return false;
                }

                lock (_lockConsumer)
                {
                    if (GetConsumerTags().Any())
                    {
                        _logger.LogWarning("StartConsume: ConsumerTags is empty");
                        return false;
                    }

                    if (_asyncEventingBasicConsumer is null)
                    {
                        _logger.LogError("StartConsume: asyncEventingBasicConsumer is null");
                        return false;
                    }

                    _asyncEventingBasicConsumer.StartConsume(_channel, _config, _prefetchSize, _prefetchCount);
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StartConsume error");
                if (rethrowError)
                {
                    throw;
                }
                else
                {
                    return false;
                }
            }
        }

        public IEnumerable<string> GetConsumerTags()
        {
            if (_asyncEventingBasicConsumer is null) return Enumerable.Empty<string>();

            return _asyncEventingBasicConsumer.ConsumerTags;
        }


        public void BasicCancel()
        {
            var tags = GetConsumerTags();
            if (tags.Any())
                foreach (var tag in tags)
                {
                    _channel.BasicCancel(tag);
                }
        }

        public void CloseChannel()
        {
            _channel.Close();
        }


        private async Task OnException(
            MessageContext msgContext,
            BasicDeliverEventArgs basicDeliverEventArgs,
            T message,
            Exception ex, Func<T, IServiceProvider, MessageContext, Exception, Task> onException)
        {
            try
            {
                if (onException != null)
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        await onException(message, scope.ServiceProvider, msgContext, ex);
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, "OnException action errored");
            }

            RetryOrReject(basicDeliverEventArgs);
        }


        private void RetryOrReject(BasicDeliverEventArgs ea)
        {
            if (IsMaxRetryReached(ea.BasicProperties, out int retryCount) || !_config.IsRetryEnabled())
            {
                if (_config.DisableDeadLettering)
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                else
                {
                    _channel.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                }
            }
            else
            {
                var ttlMs = _config.Retry.PerMessageTtlOnRetryMs[retryCount];

                ea.BasicProperties.Expiration = ttlMs;
                ea.BasicProperties.WithRetryCount(++retryCount);

                _channel.BasicPublish(_config.Queue.Name.AsRetryTopicExchange(),
                                      ttlMs,
                                      mandatory: true,
                                      ea.BasicProperties,
                                      ea.Body);

                if (_config.IsPublisherConfirmsEnabled())
                {
                    _channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(_config.PublisherConfirms.WaitConfirmsMilliseconds));
                }

                _channel.BasicAck(ea.DeliveryTag, false);
            }
        }


        private async Task EnsureInitialized()
        {
            if (_isInitialized) return;

            _channelSemaphore.Wait();
            try
            {
                if (_isInitialized) return;

                var connection = await _connectionProvider.GetOrCreateAsync().ConfigureAwait(false);
                _channel = connection.CreateModel().ConfigureConsumerChannel<T>(_config);
                _asyncEventingBasicConsumer = new AsyncEventingBasicConsumer(_channel);
                _isInitialized = true;
            }
            finally
            {
                _channelSemaphore.Release();
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (_asyncEventingBasicConsumer != null)
            {
                _asyncEventingBasicConsumer.Registered -= Consumer_Registered;
                _asyncEventingBasicConsumer.Unregistered -= Consumer_Unregistered;
                _asyncEventingBasicConsumer.ConsumerCancelled -= Consumer_Cancelled;
                _asyncEventingBasicConsumer.Shutdown -= Consumer_Shutdown;
            }
            _channelSemaphore?.Dispose();
            base.Dispose(disposing);
        }
    }
}