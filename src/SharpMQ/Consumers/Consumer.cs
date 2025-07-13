using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly AsyncEventingBasicConsumer _asyncEventingBasicConsumer;
        private readonly object _lockChannel = new object();
        private readonly object _lockConsumer = new object();

        public Consumer(IConnectionManager connectionManager,
                        ConsumerConfig config,
                        IServiceProvider serviceProvider,
                        ILogger logger,
                        RabbitSerializer serializer,
                        RabbitSerializerOptions defaultSerializerOptions = null)
                        : base(connectionManager, config, serviceProvider, logger, serializer, defaultSerializerOptions)
        {
            _channel = _connectionManager.GetOrCreateConnection()
                             .CreateModel()
                             .ConfigureConsumerChannel<T>(_config);

            _asyncEventingBasicConsumer = new AsyncEventingBasicConsumer(_channel);
        }

        public void SubscribeAsync(
            Func<T, IServiceProvider, MessageContext, Task> onDequeue,
            Func<T, IServiceProvider, MessageContext, Exception, Task> onException,
            RabbitSerializerOptions serializerOptions = null)
        {
            if (!_connectionManager.IsDispatchConsumersAsyncEnabled)
            {
                throw new ConsumerException("DispatchConsumersAsync is disabled when consumer built");
            }

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

        public bool CreateNewChannelAndStartConsume(bool rethrowError = false)
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
                    lock (_lockChannel)
                    {
                        if (!(_channel == null || _channel.IsClosed))
                        {
                            _logger.LogWarning("CreateNewChannelAndStartConsume: channel is not closed and can't open new");
                            return false;
                        }

                        _channel = _connectionManager.GetOrCreateConnection()
                                                     .CreateModel()
                                                     .ConfigureConsumerChannel<T>(_config);
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
                ea.BasicProperties.Expiration = _config.Retry.PerMessageTtlOnRetryMs != null ? _config.Retry.PerMessageTtlOnRetryMs[retryCount] : _config.Retry.PerQueueTtlMs.ToString();
                ea.BasicProperties.WithRetryCount(++retryCount);
                _channel.BasicPublish(_config.Queue.Name.AsRetryExchange(), _config.Queue.Name.AsRetryQ(), mandatory: true, ea.BasicProperties, ea.Body);
                if (_config.IsPublisherConfirmsEnabled())
                {
                    _channel.WaitForConfirmsOrDie(TimeSpan.FromMilliseconds(_config.PublisherConfirms.WaitConfirmsMilliseconds));
                }
                _channel.BasicAck(ea.DeliveryTag, false);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if(_asyncEventingBasicConsumer != null)
            {
                _asyncEventingBasicConsumer.Registered -= Consumer_Registered;
                _asyncEventingBasicConsumer.Unregistered -= Consumer_Unregistered;
                _asyncEventingBasicConsumer.ConsumerCancelled -= Consumer_Cancelled;
                _asyncEventingBasicConsumer.Shutdown -= Consumer_Shutdown;
            }
            base.Dispose(disposing);
        }
    }
}