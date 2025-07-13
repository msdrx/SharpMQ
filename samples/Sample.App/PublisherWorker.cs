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


            var messages = Enumerable.Range(0, 50).Select(x => new TestMessage()
            {
                Amount = Random.Shared.Next(1, 500) * 2m,
                Account = $"{x}_{Random.Shared.Next(1, 500)}_account"
            });

            //_producer.Publish("exchange.topic", "test_routing_key", m); // publish for TopicConsumer
            _producer.Publish<TestMessage>("exchange.topic", "test_routing_key", messages); // publish for TopicConsumer

            await Task.Delay(2000);
        }
    }
}
