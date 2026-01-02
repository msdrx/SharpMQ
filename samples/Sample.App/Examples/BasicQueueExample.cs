using SharpMQ;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using Sample.App.Shared;

namespace Sample.App.Examples;

/// <summary>
/// Demonstrates basic queue publish/consume pattern
/// </summary>
public class BasicQueueExample : BackgroundService
{
    private readonly IProducer _producer;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BasicQueueExample> _logger;
    private IReadOnlyCollection<IConsumer<TestMessage>>? _consumers;

    public BasicQueueExample(
        IProducer producer,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<BasicQueueExample> logger)
    {
        _producer = producer;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Setup consumer
        var serverConfig = _configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();
        var consumerConfig = _configuration.GetRequiredSection("ConsumerTestSimple").Get<ConsumerConfig>();

        _consumers = ConsumerFactory.CreateConsumers<TestMessage>(
            serverConfig,
            consumerConfig,
            _serviceProvider,
            new CustomJsonSerializer(),
            consumerClientProvidedName: "BasicQueueConsumer");

        _consumers.SubscribeAsync(
            async (message, sp, msgContext) =>
            {
                await Task.Delay(100);
                _logger.LogInformation("[BasicQueue] Received: {Message}", message);
            },
            async (message, sp, msgContext, ex) =>
            {
                await Task.Delay(100);
                _logger.LogError("[BasicQueue] Error: {Message}", message);
            },
            serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault));

        // Publish messages
        await Task.Delay(3000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var message = new TestMessage
            {
                Amount = Random.Shared.Next(1, 500) * 2m,
                Account = $"{Random.Shared.Next(1, 500)}_account"
            };

            await _producer.PublishAsync(message);
            _logger.LogInformation("[BasicQueue] Published: {Message}", message);

            await Task.Delay(2000, stoppingToken);
        }
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
