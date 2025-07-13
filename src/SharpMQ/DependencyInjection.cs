using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using SharpMQ.Connections;
using SharpMQ.Exceptions;
using SharpMQ.Producers;
using SharpMQ.Serializer.Abstractions;

namespace SharpMQ
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers Singleton <see cref="IProducer"/> and <see cref="IProducerFactory"/> services
        /// <br></br>
        /// to get producer by key use <see cref="IProducerFactory.Get(string)"/> method
        /// <br></br>
        /// <see cref="IProducer"/> is main producer instance
        /// </summary>
        /// <param name="services"></param>
        /// <param name="mainProducerKey">main producer key</param>
        /// <param name="serializer"></param>
        /// <param name="defaultSerializerOptions"></param>
        /// <param name="producerAndServerConfigs">producer and server configs</param>
        /// <returns></returns>
        /// <exception cref="RabbitMqConfigValidationException"></exception>
        public static IServiceCollection AddProducers(this IServiceCollection services,
            string mainProducerKey,
            RabbitSerializer serializer,
            RabbitSerializerOptions defaultSerializerOptions,
            params ProducerAndServerConfig[] producerAndServerConfigs)
        {
            if (producerAndServerConfigs == null || producerAndServerConfigs.Length == 0) throw new RabbitMqConfigValidationException("ProducerAndServerConfig is empty");
            if (producerAndServerConfigs.Select(x => x.ProducerKey).Distinct().Count() != producerAndServerConfigs.Length) throw new RabbitMqConfigValidationException("producerKey must be unique");
            if (!producerAndServerConfigs.Any(x => x.ProducerKey == mainProducerKey)) throw new RabbitMqConfigValidationException($"{mainProducerKey} not found in producerAndServerConfigs list");

            services.AddSingleton<IProducerFactory, ProducerFactory>(provider =>
            {
                var factory = new ProducerFactory();
                foreach (var item in producerAndServerConfigs)
                {
                    AddProducer(item.ProducerKey, factory, provider, item.ServerConfig, item.ProducerConfig, serializer, defaultSerializerOptions);
                }
                return factory;
            });

            services.AddSingleton<IProducer>(provider =>
            {
                var factory = provider.GetRequiredService<IProducerFactory>();
                return factory.Get(producerAndServerConfigs.First(x => x.ProducerKey == mainProducerKey).ProducerKey);
            });

            return services;
        }

        /// <summary>
        /// Registers Singleton <see cref="IRawConnectionAccessor"/> service
        /// </summary>
        /// <param name="services"></param>
        /// <param name="serverConfig"></param>
        /// <param name="clientProvidedNameSuffix"></param>
        /// <returns></returns>
        /// <exception cref="RabbitMqConfigValidationException"></exception>
        public static IServiceCollection AddRawConnection(this IServiceCollection services,
                                                          RabbitMqServerConfig serverConfig,
                                                          string clientProvidedNameSuffix = "raw-connection")
        {
            if (serverConfig == null)
            {
                throw new RabbitMqConfigValidationException("serverConfig is null");
            }
            serverConfig.Validate();

            services.AddSingleton<IRawConnectionAccessor, RawConnectionAccessor>(sp =>
            {
                var cm = new ConnectionManager(serverConfig, sp.GetRequiredService<ILogger<RawConnectionAccessor>>(), true, clientProvidedNameSuffix: clientProvidedNameSuffix);
                return new RawConnectionAccessor(cm);
            });

            return services;
        }

        /// <summary>
        /// Registers Singleton <see cref="IProducer"/>-ს and <see cref="IProducerFactory"/> services (only one producer)
        /// </summary>
        /// <param name="services"></param>
        /// <param name="producerKey"></param>
        /// <param name="producerConfig"></param>
        /// <param name="serverConfig"></param>
        /// <param name="serializer"></param>
        /// <param name="defaultSerializerOptions"></param>
        /// <returns></returns>
        public static IServiceCollection AddProducer(this IServiceCollection services,
                                                      string producerKey,
                                                      ProducerConfig producerConfig,
                                                      RabbitMqServerConfig serverConfig,
                                                      RabbitSerializer serializer,
                                                      RabbitSerializerOptions defaultSerializerOptions = null)
        {
            AddProducers(services,
                         producerKey,
                         serializer,
                         defaultSerializerOptions,
                         new ProducerAndServerConfig(producerKey, producerConfig, serverConfig));

            return services;
        }



        private static void AddProducer(
            string producerKey,
            IProducerFactory factory,
            IServiceProvider serviceProvider,
            RabbitMqServerConfig serverConfig,
            ProducerConfig producerConfig,
            RabbitSerializer serializer,
            RabbitSerializerOptions defaultSerializerOptions = null)
        {
            if (string.IsNullOrWhiteSpace(producerKey)) throw new RabbitMqConfigValidationException("producerKey is required. must be unique.");

            if (serverConfig == null)
            {
                throw new RabbitMqConfigValidationException("serverConfig is null");
            }
            serverConfig.Validate();

            if (producerConfig == null)
            {
                throw new RabbitMqConfigValidationException("producerConfig is null");
            }
            producerConfig.Validate();

            var cm = new ConnectionManager(serverConfig, serviceProvider.GetRequiredService<ILogger<ConnectionManager>>(), true, clientProvidedNameSuffix: producerKey);
            var cpm = new ChannelPool(cm, producerConfig.ChannelPool.MinPoolSize, producerConfig.ChannelPool.MaxPoolSize, producerConfig.ChannelPool.WaitTimeoutMs);
            var p = new Producer(cpm, producerConfig, serviceProvider.GetRequiredService<ILogger<Producer>>(), serializer, defaultSerializerOptions);

            factory.Add(producerKey, p);
        }
    }
}

