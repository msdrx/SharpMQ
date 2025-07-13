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

        public static IServiceCollection OpenRawConnectionOnHostStartup(this IServiceCollection services)
        {
            services.AddHostedService<OpenRawConnectionBackgroundService>();
            return services;
        }
    }
}
