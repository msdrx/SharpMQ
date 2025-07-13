using System.Collections.Generic;
using System.Linq;
using SharpMQ.Configs;
using SharpMQ.Exceptions;
using RabbitMQ.Client;

namespace SharpMQ.Extensions
{
    internal static class ChannelExtensions
    {
        public static IModel ConfigureConsumerChannel<T>(this IModel channel, ConsumerConfig config)
        {
            if (config.Queue.UseMessageTypeAsQueueName)
            {
                config.Queue.Name = typeof(T).FullName;
            }

            var args = config.Queue.QueueArgs;

            if (!config.DisableDeadLettering)
            {
                var dlargs = new QueueArgConfig[] {
                new QueueArgConfig(){
                    Key =ConfigConstants.QueueArgKeys.DLExchage,
                    Value = config.Queue.Name.AsDLExchange()
                },
                new QueueArgConfig()
                {
                    Key = ConfigConstants.QueueArgKeys.DLExchangeRoutingKey,
                    Value =  config.Queue.Name
                }};
                args = config.Queue.QueueArgs == null ? dlargs : config.Queue.QueueArgs.Concat(dlargs).ToArray();
            }

            channel.AddQueue(config.Queue.Name, args);
            channel.AddExchange(config.Queue.Name.AsDirectExchange(), ConfigConstants.Exchanges.Direct);
            channel.AddBinding(config.Queue.Name, config.Queue.Name.AsDirectExchange(), config.Queue.Name);

            if (!config.DisableDeadLettering)
            {
                channel.AddExchange(config.Queue.Name.AsDLExchange(), ConfigConstants.Exchanges.Direct);

                channel.AddQueue(config.Queue.Name.AsDLQ())
                       .AddBinding(config.Queue.Name.AsDLQ(), config.Queue.Name.AsDLExchange(), config.Queue.Name);
            }

            if (config.IsRetryEnabled())
            {
                channel.ConfigureRetry(config);
            }


            if (config.Exchanges != null)
            {
                foreach (var exchangeItem in config.Exchanges)
                {
                    if (exchangeItem.DeclareExchange)
                    {
                        channel.AddExchange(exchangeItem?.Name, exchangeItem?.Type?.ToLower() ?? ConfigConstants.Exchanges.Direct);
                    }

                    if (string.Equals(exchangeItem.Type, ConfigConstants.Exchanges.Fanout, System.StringComparison.InvariantCultureIgnoreCase))
                    {
                        channel.AddBinding(config.Queue.Name, exchangeItem?.Name, string.Empty);
                    }
                    else
                    {
                        foreach (var routing in exchangeItem.GetRoutingKeys())
                        {
                            channel.AddBinding(config.Queue.Name, exchangeItem?.Name, routing);
                        }
                    }
                }
            }

            return channel;
        }

        public static void StartConsume(this IBasicConsumer consumer, IModel channel, ConsumerConfig config, uint prefetchSize, ushort prefetchCount)
        {
            if (channel == null)
            {
                throw new RabbitMqException("Channel not configured!");
            }

            channel.BasicQos(prefetchSize, prefetchCount, global: false);

            if (config.IsPublisherConfirmsEnabled()) channel.ConfirmSelect();

            channel.BasicConsume(config.Queue.Name, autoAck: false, consumer);
        }

        public static IModel AddExchange(this IModel channel, string exchnage, string exchangeType)
        {
            if (!string.IsNullOrWhiteSpace(exchnage) && !string.IsNullOrWhiteSpace(exchangeType))
            {
                channel.ExchangeDeclare(exchnage, exchangeType, durable: true);
            }

            return channel;
        }

        public static IModel AddQueue(this IModel channel, string queueName, QueueArgConfig[] args = null)
        {
            if (!string.IsNullOrWhiteSpace(queueName))
            {
                channel.QueueDeclare(queueName, durable: true, exclusive: false, autoDelete: false, args?.GetQueueArgs());
            }

            return channel;
        }

        /// <summary>
        /// bind queue to exchange
        /// </summary>
        /// <param name="channel"></param>
        /// <param name="queueName"></param>
        /// <param name="exchange"></param>
        /// <param name="routingKey"></param>
        /// <param name="args">this args is used for header exchange, </param>
        /// <returns></returns>
        public static IModel AddBinding(this IModel channel, string queueName, string exchange, string routingKey, Dictionary<string, object> args = null)
        {
            if (string.IsNullOrWhiteSpace(exchange))
            {
                return channel;
            }

            var rk = string.IsNullOrWhiteSpace(routingKey) ? queueName : routingKey;
            channel.QueueBind(queueName, exchange, rk, args);
            return channel;
        }

        public static IBasicProperties WithPersistens(this IModel channel)
        {
            IBasicProperties basicProperties = channel.CreateBasicProperties();
            basicProperties.Persistent = true;
            return basicProperties;
        }

        private static IModel ConfigureRetry(this IModel channel, ConsumerConfig config)
        {
            var retryQueue = config.Queue.Name.AsRetryQ();
            var retryExchange = config.Queue.Name.AsRetryExchange();

            channel.AddQueue(retryQueue, new QueueArgConfig[]
                   {
                        new QueueArgConfig()
                        {
                            Key = ConfigConstants.QueueArgKeys.MessageTTL,
                            Value = config.Retry.PerQueueTtlMs
                        },
                        new QueueArgConfig()
                        {
                            Key = ConfigConstants.QueueArgKeys.DLExchage,
                            Value = config.Queue.Name.AsDirectExchange()
                        },
                        new QueueArgConfig()
                        {
                            Key = ConfigConstants.QueueArgKeys.DLExchangeRoutingKey,
                            Value = config.Queue.Name
                        }
                    });

            channel.AddExchange(retryExchange, ConfigConstants.Exchanges.Direct);

            channel.AddBinding(retryQueue, retryExchange, retryQueue);

            return channel;
        }
    }
}