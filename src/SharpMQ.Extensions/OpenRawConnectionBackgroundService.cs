using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System.Threading;
using SharpMQ.Abstractions;

namespace SharpMQ.Extensions
{
    public class OpenRawConnectionBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<OpenRawConnectionBackgroundService> _logger;

        public OpenRawConnectionBackgroundService(IServiceProvider serviceProvider, ILogger<OpenRawConnectionBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("start opening raw connection");

                _serviceProvider.GetRequiredService<IRawConnectionAccessor>();

                _logger.LogInformation("end opening raw connection");

                return Task.CompletedTask;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "raw connection failed to open");
                return Task.FromException(e);
            }
        }
    }
}
