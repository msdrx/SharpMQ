using Microsoft.Extensions.DependencyInjection;

namespace SharpMQ.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection OpenProducersConnectionsOnHostStartup(this IServiceCollection services)
        {
            services.AddHostedService<OpenProducerConnectionBackgroundService>();
            return services;
        }

        /// <summary>
        /// Adds RabbitMQ health check to the health check builder using ConnectionProvider to create a connection provider
        /// </summary>
        /// <param name="builder">The health check builder</param>
        /// <param name="serverConfig">RabbitMQ server configuration for the health check connection</param>
        /// <param name="name">The health check name (defaults to "rabbitmq")</param>
        /// <param name="failureStatus">The health status to report when unhealthy (defaults to Unhealthy)</param>
        /// <param name="tags">Optional tags to associate with the health check</param>
        /// <returns>The health check builder for chaining</returns>
        public static IHealthChecksBuilder AddRabbitMq(
            this IHealthChecksBuilder builder,
            SharpMQ.Configs.RabbitMqServerConfig serverConfig,
            string name = "rabbitmq",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? failureStatus = null,
            params string[] tags)
        {
            builder.Services.AddSingleton<RabbitMqHealthCheck>(sp =>
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SharpMQ.Connections.ConnectionProvider>>();
                var connectionProvider = SharpMQ.Connections.ConnectionProvider.Create(serverConfig, logger, true, "healthcheck");
                return new RabbitMqHealthCheck(connectionProvider);
            });

            return builder.AddCheck<RabbitMqHealthCheck>(name, failureStatus, tags);
        }

        /// <summary>
        /// Adds RabbitMQ health check to the health check builder using an existing IConnectionProvider
        /// </summary>
        /// <param name="builder">The health check builder</param>
        /// <param name="connectionProvider">The connection provider to monitor</param>
        /// <param name="name">The health check name (defaults to "rabbitmq")</param>
        /// <param name="failureStatus">The health status to report when unhealthy (defaults to Unhealthy)</param>
        /// <param name="tags">Optional tags to associate with the health check</param>
        /// <returns>The health check builder for chaining</returns>
        public static IHealthChecksBuilder AddRabbitMq(
            this IHealthChecksBuilder builder,
            SharpMQ.Connections.IConnectionProvider connectionProvider,
            string name = "rabbitmq",
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus? failureStatus = null,
            params string[] tags)
        {
            builder.Services.AddSingleton(new RabbitMqHealthCheck(connectionProvider));
            return builder.AddCheck<RabbitMqHealthCheck>(name, failureStatus, tags);
        }
    }
}
