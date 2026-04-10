using System;
using System.Collections.Generic;
using System.Threading;
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
        public static IConsumerGroup<T> CreateConsumers<T>(RabbitMqServerConfig serverConfig,
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

            IConnectionProvider connectionProvider = null;
            if (singleConnectionPerConsumerGroup)
            {
                connectionProvider = ConnectionProvider.Create(serverConfig,
                                                               serviceProvider.GetRequiredService<ILogger<ConnectionProvider>>(),
                                                               true,
                                                               consumerClientProvidedName);
            }

            var consumerLogger = serviceProvider.GetRequiredService<ILogger<Consumer<T>>>();
            var consumers = new List<IConsumer<T>>();
            for (int i = 0; i < consumerConfig.ConsumersCount; i++)
            {
                if (!singleConnectionPerConsumerGroup)
                {
                    connectionProvider = ConnectionProvider.Create(serverConfig,
                                                                   serviceProvider.GetRequiredService<ILogger<ConnectionProvider>>(),
                                                                   true,
                                                                   $"{consumerClientProvidedName}:{i}");
                }

                // When each consumer has its own connection, it owns (and disposes) it.
                // When sharing, the ConsumerGroup owns the connection.
                var ownsConnection = !singleConnectionPerConsumerGroup;
                consumers.Add(new Consumer<T>(connectionProvider, consumerConfig, serviceProvider, consumerLogger, serializer, defaultSerializerOptions, ownsConnection));
            }

            return new ConsumerGroup<T>(consumers, singleConnectionPerConsumerGroup ? connectionProvider : null);

        }

        public static async Task SubscribeAsync<T>(this IEnumerable<IConsumer<T>> consumers,
                                                    Func<T, IServiceProvider, MessageContext, Task> onDequeue,
                                                    Func<T, IServiceProvider, MessageContext, Exception, Task> onException = null,
                                                    RabbitSerializerOptions serializerOptions = null,
                                                    CancellationToken cancellationToken = default) where T : class
        {
            var tasks = new List<Task>();
            foreach (IConsumer<T> consumer in consumers)
            {
                tasks.Add(consumer.SubscribeAsync(onDequeue, onException, serializerOptions, cancellationToken));
            }
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }
}