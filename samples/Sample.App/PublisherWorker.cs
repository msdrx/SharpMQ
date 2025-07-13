using SharpMQ.Abstractions;

namespace Sample.App;
internal class PublisherWorker : BackgroundService
{
    private readonly IProducer _producer;

    public PublisherWorker(IProducer producer)
    {
        _producer = producer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(3000);
        while (true)
        {
            var m = new TestMessage()
            {
                Amount = Random.Shared.Next(1, 500) * 2m,
                Account = $"{Random.Shared.Next(1, 500)}_account"
            };

            _producer.Publish(m); // publish for ConsumerTestSimple



            _producer.Publish("exchange.topic", "test_routing_key", m); // publish for TopicConsumer

            await Task.Delay(500);
        }
    }
}
