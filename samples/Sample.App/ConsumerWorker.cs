using SharpMQ;
using SharpMQ.Abstractions;
using SharpMQ.Configs;

namespace Sample.App;
public class ConsumerWorker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private IReadOnlyCollection<IConsumer<TestMessage>>? _consumersSimple;
    private IReadOnlyCollection<IConsumer<TestMessage>>? _consumersTopic;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;

    public ConsumerWorker(ILogger<Worker> logger, IConfiguration configuration, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var serverConfig = _configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();
        var consumerConfigSimple = _configuration.GetRequiredSection("ConsumerTestSimple").Get<ConsumerConfig>();
        var consumerConfigTopic = _configuration.GetRequiredSection("TopicConsumer").Get<ConsumerConfig>();


        _consumersSimple = ConsumerFactory.CreateConsumers<TestMessage>(serverConfig,
                                                            consumerConfigSimple,
                                                            _serviceProvider,
                                                            new CustomJsonSerializer(),
                                                            consumerClientProvidedName: "ConsumerTestSimple");

        _consumersTopic = ConsumerFactory.CreateConsumers<TestMessage>(serverConfig,
                                                    consumerConfigTopic,
                                                    _serviceProvider,
                                                    new CustomJsonSerializer(),
                                                    consumerClientProvidedName: "TopicConsumer");

        _consumersSimple.SubscribeAsync(async (message, sp, msgContext) =>
        {
            //on receive
            await Task.Delay(100); // simulate message processing
            _logger.LogInformation("ConsumerTestSimple: recieved: {isLastTry} {msg}", msgContext.IsLastTry, message);
        }, async (message, sp, msgContext, ex) =>
        {
            //on error
            await Task.Delay(100); // simulate error processing
            _logger.LogError("ConsumerTestSimple: errored: {isLastTry} {msg}", msgContext.IsLastTry, message);
        }, serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault));

        _consumersTopic.SubscribeAsync(async (message, sp, msgContext) =>
        {
            //on receive
            await Task.Delay(100); // simulate message processing
            _logger.LogInformation("TopicConsumer: recieved: {isLastTry} {msg}", msgContext.IsLastTry, message);
        }, async (message, sp, msgContext, ex) =>
        {
            //on error
            await Task.Delay(100); // simulate error processing
            _logger.LogError("TopicConsumer: errored: {isLastTry} {msg}", msgContext.IsLastTry, message);
        }, serializerOptions: new CustomJsonSerializerOptions(JsonConstants.ConsumerDefault));

        return Task.CompletedTask;
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        if (_consumersSimple != null)
        {
            foreach (var item in _consumersSimple)
            {
                item?.Dispose();
            }
        }
        if (_consumersTopic != null)
        {
            foreach (var item in _consumersTopic)
            {
                item?.Dispose();
            }
        }
        return base.StopAsync(cancellationToken);
    }
}
