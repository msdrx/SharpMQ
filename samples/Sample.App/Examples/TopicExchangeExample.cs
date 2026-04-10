using SharpMQ;
using SharpMQ.Abstractions;
using SharpMQ.Configs;
using Sample.App.Shared;

namespace Sample.App.Examples;

/// <summary>
/// Demonstrates topic exchange routing with multiple routing keys and consumers
/// Publisher sends messages with routing keys: orders.usa.*, orders.europe.*, logs.error, logs.info
/// Consumer 1 subscribes to: orders.usa.* (US orders)
/// Consumer 2 subscribes to: orders.europe.* (Europe orders)
/// Consumer 3 subscribes to: logs.# (All logs)
/// </summary>
public class TopicExchangeExample : BackgroundService
{
    private readonly IProducer _producer;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TopicExchangeExample> _logger;
    private IConsumerGroup<TestMessage>? _usaOrderConsumers;
    private IConsumerGroup<TestMessage>? _europeOrderConsumers;
    private IConsumerGroup<TestMessage>? _logConsumers;

    public TopicExchangeExample(
        IProducer producer,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<TopicExchangeExample> logger)
    {
        _producer = producer;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverConfig = _configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();

        // Consumer 1: USA orders (orders.usa.*)
        var usaOrderConfig = _configuration.GetRequiredSection("TopicConsumerUsaOrders").Get<ConsumerConfig>();
        _usaOrderConsumers = ConsumerFactory.CreateConsumers<TestMessage>(
            serverConfig,
            usaOrderConfig,
            _serviceProvider,
            new CustomJsonSerializer(),
            consumerClientProvidedName: "UsaOrderConsumer");

        await _usaOrderConsumers.SubscribeAsync(
            async (message, sp, msgContext) =>
            {
                await Task.Delay(100);
                _logger.LogInformation("[USA ORDERS] RoutingKey={RoutingKey}, Message={Message}",
                    msgContext.BasicDeliverEventArgs.RoutingKey, message);
            },
            async (message, sp, msgContext, ex) =>
            {
                await Task.Delay(100);
                _logger.LogError("[USA ORDERS ERROR] {Message}", message);
            },
            serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault),
            cancellationToken: stoppingToken);

        // Consumer 2: Europe orders (orders.europe.*)
        var europeOrderConfig = _configuration.GetRequiredSection("TopicConsumerEuropeOrders").Get<ConsumerConfig>();
        _europeOrderConsumers = ConsumerFactory.CreateConsumers<TestMessage>(
            serverConfig,
            europeOrderConfig,
            _serviceProvider,
            new CustomJsonSerializer(),
            consumerClientProvidedName: "EuropeOrderConsumer");

        await _europeOrderConsumers.SubscribeAsync(
            async (message, sp, msgContext) =>
            {
                await Task.Delay(100);
                _logger.LogInformation("[EUROPE ORDERS] RoutingKey={RoutingKey}, Message={Message}",
                    msgContext.BasicDeliverEventArgs.RoutingKey, message);
            },
            async (message, sp, msgContext, ex) =>
            {
                await Task.Delay(100);
                _logger.LogError("[EUROPE ORDERS ERROR] {Message}", message);
            },
            serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault),
            cancellationToken: stoppingToken);

        // Consumer 3: All logs (logs.#)
        var logConfig = _configuration.GetRequiredSection("TopicConsumerLogs").Get<ConsumerConfig>();
        _logConsumers = ConsumerFactory.CreateConsumers<TestMessage>(
            serverConfig,
            logConfig,
            _serviceProvider,
            new CustomJsonSerializer(),
            consumerClientProvidedName: "LogConsumer");

        await _logConsumers.SubscribeAsync(
            async (message, sp, msgContext) =>
            {
                await Task.Delay(100);
                _logger.LogInformation("[LOGS] RoutingKey={RoutingKey}, Message={Message}",
                    msgContext.BasicDeliverEventArgs.RoutingKey, message);
            },
            async (message, sp, msgContext, ex) =>
            {
                await Task.Delay(100);
                _logger.LogError("[LOGS ERROR] {Message}", message);
            },
            serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault),
            cancellationToken: stoppingToken);

        // Wait for consumers to be ready
        await Task.Delay(3000, stoppingToken);

        // Publish messages with different routing keys
        var routingKeys = new[]
        {
            "orders.usa.california",
            "orders.usa.newyork",
            "orders.europe.germany",
            "orders.europe.france",
            "logs.error",
            "logs.info"
        };

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var routingKey in routingKeys)
            {
                var message = new TestMessage
                {
                    Amount = Random.Shared.Next(1, 1000) * 2m,
                    Account = $"{routingKey.Replace(".", "_")}_account_{Random.Shared.Next(1, 100)}"
                };

                await _producer.PublishAsync("exchange.topic", routingKey, message);
                _logger.LogInformation("[PUBLISHER] RoutingKey={RoutingKey}, Message={Message}",
                    routingKey, message);

                await Task.Delay(500, stoppingToken);
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _usaOrderConsumers?.Dispose();
        _europeOrderConsumers?.Dispose();
        _logConsumers?.Dispose();

        return base.StopAsync(cancellationToken);
    }
}
