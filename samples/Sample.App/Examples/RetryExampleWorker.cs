using Sample.App.Shared;
using SharpMQ;
using SharpMQ.Abstractions;
using SharpMQ.Configs;

namespace Sample.App;

/// <summary>
/// Simple worker demonstrating retry mechanism with variable TTL times.
/// This worker intentionally fails messages to showcase the retry flow.
/// </summary>
public class RetryExampleWorker : BackgroundService
{
    private readonly ILogger<RetryExampleWorker> _logger;
    private IReadOnlyCollection<IConsumer<TestMessage>>? _consumers;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public RetryExampleWorker(ILogger<RetryExampleWorker> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverConfig = _configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();
        var consumerConfig = _configuration.GetRequiredSection("RetryExampleConsumer").Get<ConsumerConfig>();

        _consumers = ConsumerFactory.CreateConsumers<TestMessage>(
            serverConfig,
            consumerConfig,
            _serviceProvider,
            new CustomJsonSerializer(),
            consumerClientProvidedName: "RetryExampleConsumer");

        _consumers.SubscribeAsync(
            async (message, sp, msgContext) =>
            {
                // Simulate message processing
                await Task.Delay(100);

                _logger.LogInformation(
                    "RetryExample: Processing message. IsLastTry={IsLastTry}, Message={Message}",
                    msgContext.IsLastTry,
                    message);

                // Intentionally fail to demonstrate retry mechanism
                // In real scenarios, this would be actual business logic that might fail
                throw new Exception("Simulated processing failure to demonstrate retry");
            },
            async (message, sp, msgContext, ex) =>
            {
                // Handle error - this is called when message processing fails
                await Task.Delay(100);

                _logger.LogWarning(
                    "RetryExample: Message failed. IsLastTry={IsLastTry}, Message={Message}, Error={Error}",
                    msgContext.IsLastTry,
                    message,
                    ex.Message);

                // After this callback, the message will be:
                // - Retried with increasing delays (5s -> 15s -> 1m) if retries remain
                // - Sent to DLQ if max retries reached
            },
            serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault));

        _logger.LogInformation("RetryExampleWorker started. Waiting for messages to demonstrate retry with variable TTL...");
        _logger.LogInformation("Retry configuration: 5s -> 15s -> 1m");
        _logger.LogInformation("Queue names created: retry.example.RetryQ.5s, retry.example.RetryQ.15s, retry.example.RetryQ.1m");

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumers != null)
        {
            foreach (var consumer in _consumers)
            {
                consumer?.Dispose();
            }
        }
        return base.StopAsync(cancellationToken);
    }
}
