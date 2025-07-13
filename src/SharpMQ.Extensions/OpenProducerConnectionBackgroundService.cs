using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpMQ.Abstractions;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Hosting;

namespace SharpMQ.Extensions
{
    public class OpenProducerConnectionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OpenProducerConnectionBackgroundService> _logger;

        public OpenProducerConnectionBackgroundService(IServiceProvider serviceProvider, ILogger<OpenProducerConnectionBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("start opening Producers connections");

                _serviceProvider.GetRequiredService<IProducerFactory>();

                _logger.LogInformation("end opening Producers connections");

                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Producers connections failed to open");
                return Task.FromException(e);
            }
        }
    }
}