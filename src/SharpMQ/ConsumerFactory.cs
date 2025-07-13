using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Consumers;
using SharpMQ.Exceptions;
using SharpMQ.Serializer.Abstractions;

namespace SharpMQ
{
    public static class ConsumerFactory
    {
        public static IReadOnlyCollection<IConsumer<T>> CreateConsumers<T>(RabbitMqServerConfig serverConfig,
                                                                           ConsumerConfig consumerConfig,
                                                                           IServiceProvider serviceProvider,
                                                                           RabbitSerializer serializer,
                                                                           RabbitSerializerOptions defaultSerializerOptions = null,
                                                                           bool singleConnectionPerConsumerGroup = true,
                                                                           string consumerClientProvidedName = null) where T : class
        {

            if (serverConfig == null || consumerConfig == null)
            {
                throw new RabbitMqConfigValidationException("ConsumerFactory: config is null");
            }
            if (serviceProvider == null) throw new RabbitMqConfigValidationException("RabbitMQ serviceProvider is null");

            serverConfig.Validate();
            consumerConfig.Validate();

            var connectionManagerLogger = serviceProvider.GetRequiredService<ILogger<ConnectionManager>>();
            if (connectionManagerLogger == null) throw new RabbitMqConfigValidationException("RabbitMQ can't get logger from service provider");

            ConnectionManager cm = null;
            if (singleConnectionPerConsumerGroup)
            {
                cm = new ConnectionManager(serverConfig, connectionManagerLogger, true, consumerClientProvidedName);
            }

            var consumerLogger = serviceProvider.GetRequiredService<ILogger<Consumer<T>>>();
            var consumers = new List<IConsumer<T>>();
            for (int i = 0; i < consumerConfig.ConsumersCount; i++)
            {
                if (!singleConnectionPerConsumerGroup)
                {
                    cm = new ConnectionManager(serverConfig, connectionManagerLogger, true, $"{consumerClientProvidedName}:{i}");
                }

                consumers.Add(new Consumer<T>(cm, consumerConfig, serviceProvider, consumerLogger, serializer, defaultSerializerOptions));
            }

            return consumers.AsReadOnly();

        }

        public static void SubscribeAsync<T>(this IEnumerable<IConsumer<T>> consumers,
                                             Func<T, IServiceProvider, MessageContext, Task> onDequeue,
                                             Func<T, IServiceProvider, MessageContext, Exception, Task> onException = null,
                                             RabbitSerializerOptions serializerOptions = null) where T : class
        {
            foreach (IConsumer<T> consumer in consumers)
            {
                consumer.SubscribeAsync(onDequeue, onException, serializerOptions);
            }
        }
    }
}