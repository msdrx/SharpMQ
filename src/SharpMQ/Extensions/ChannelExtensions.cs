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
            var queueName = ResolveQueueName<T>(config);

            var args = config.Queue.QueueArgs;

            if (!config.DisableDeadLettering)
            {
                var dlargs = new QueueArgConfig[] {
                new QueueArgConfig(){
                    Key =ConfigConstants.QueueArgKeys.DLExchage,
                    Value = queueName.AsDLExchange()
                },
                new QueueArgConfig()
                {
                    Key = ConfigConstants.QueueArgKeys.DLExchangeRoutingKey,
                    Value =  queueName
                }};
                args = config.Queue.QueueArgs == null ? dlargs : config.Queue.QueueArgs.Concat(dlargs).ToArray();
            }

            channel.AddQueue(queueName, args);
            channel.AddExchange(queueName.AsDirectExchange(), ConfigConstants.Exchanges.Direct);
            channel.AddBinding(queueName, queueName.AsDirectExchange(), queueName);

            if (!config.DisableDeadLettering)
            {
                channel.AddExchange(queueName.AsDLExchange(), ConfigConstants.Exchanges.Direct);

                channel.AddQueue(queueName.AsDLQ())
                       .AddBinding(queueName.AsDLQ(), queueName.AsDLExchange(), queueName);
            }

            if (config.IsRetryEnabled())
            {
                channel.ConfigureRetry(config, queueName);
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
                        channel.AddBinding(queueName, exchangeItem?.Name, string.Empty);
                    }
                    else
                    {
                        foreach (var routing in exchangeItem.GetRoutingKeys())
                        {
                            channel.AddBinding(queueName, exchangeItem?.Name, routing);
                        }
                    }
                }
            }

            return channel;
        }

        /// <summary>
        /// Resolves the effective queue name for a consumer without mutating the config.
        /// </summary>
        internal static string ResolveQueueName<T>(ConsumerConfig config)
        {
            return config.Queue.UseMessageTypeAsQueueName
                ? typeof(T).FullName
                : config.Queue.Name;
        }

        public static void StartConsume<T>(this IBasicConsumer consumer, IModel channel, ConsumerConfig config, uint prefetchSize, ushort prefetchCount) where T : class
        {
            if (channel == null)
            {
                throw new RabbitMqException("Channel not configured!");
            }

            var queueName = ResolveQueueName<T>(config);

            channel.BasicQos(prefetchSize, prefetchCount, global: false);

            if (config.IsPublisherConfirmsEnabled()) channel.ConfirmSelect();

            channel.BasicConsume(queueName, autoAck: false, consumer);
        }

        public static IModel AddExchange(this IModel channel, string exchange, string exchangeType)
        {
            if (!string.IsNullOrWhiteSpace(exchange) && !string.IsNullOrWhiteSpace(exchangeType))
            {
                channel.ExchangeDeclare(exchange, exchangeType, durable: true);
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

        public static IBasicProperties WithPersistence(this IModel channel)
        {
            IBasicProperties basicProperties = channel.CreateBasicProperties();
            basicProperties.Persistent = true;
            return basicProperties;
        }

        private static IModel ConfigureRetry(this IModel channel, ConsumerConfig config, string queueName)
        {
            var retryTopicExchange = queueName.AsRetryTopicExchange();
            channel.AddExchange(retryTopicExchange, ConfigConstants.Exchanges.Topic);

            foreach (var ttlMs in config.Retry.PerMessageTtlOnRetryMs)
            {
                var retryQueue = queueName.AsRetryQ(ttlMs);

                channel.AddQueue(retryQueue, new QueueArgConfig[]
                {
                    new QueueArgConfig()
                    {
                        Key = ConfigConstants.QueueArgKeys.MessageTTL,
                        Value = ttlMs
                    },
                    new QueueArgConfig()
                    {
                        Key = ConfigConstants.QueueArgKeys.DLExchage,
                        Value = queueName.AsDirectExchange()
                    },
                    new QueueArgConfig()
                    {
                        Key = ConfigConstants.QueueArgKeys.DLExchangeRoutingKey,
                        Value = queueName
                    }
                });

                channel.AddBinding(retryQueue, retryTopicExchange, ttlMs.ToString());
            }

            return channel;
        }
    }
}