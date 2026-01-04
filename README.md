# SharpMQ

.NET RabbitMQ library

## Features

- **Connection & Channel Management**: Efficient resource management with configurable pool sizes
- **Retry Logic**: Automatic retry with configurable TTL tiers using RabbitMQ's dead letter exchange
- **Error Handling**: Guaranteed message delivery with optional publisher confirmations
- **Health Checks**: Built-in health check support for monitoring
- **Serializer Agnostic**: Bring your own serializer (JSON, Protobuf, etc.)

## You should use SharpMQ if
-  You want production-ready patterns out of the box
- You need retry/dead letter queue handling
- You want to focus on business logic, not RabbitMQ plumbing
- You need channel pooling for high-throughput scenarios

## Installation

Install the packages via NuGet:

```bash
dotnet add package SharpMQ
dotnet add package SharpMQ.Extensions
```

For custom implementations, you may also need:

```bash
dotnet add package SharpMQ.Abstractions
dotnet add package SharpMQ.Serializer.Abstractions
```

## Quick Start

### 1. Configure Services

```csharp
using SharpMQ;
using SharpMQ.Configs;
using SharpMQ.Extensions;

var builder = Host.CreateApplicationBuilder(args);

var serverConfig = new RabbitMqServerConfig
{
    HostName = "localhost",
    Port = 5672,
    UserName = "guest",
    Password = "guest",
    VirtualHost = "/"
};

var producerConfig = new ProducerConfig
{
    ChannelPool = new ChannelPoolConfig
    {
        MinPoolSize = 5,
        MaxPoolSize = 20
    }
};

builder.Services
    .AddProducer("my-producer", producerConfig, serverConfig, new MySerializer())
    .OpenProducersConnectionsOnHostStartup();

var host = builder.Build();
host.Run();
```

### 2. Publish Messages

```csharp
public class MyService
{
    private readonly IProducer _producer;

    public MyService(IProducer producer)
    {
        _producer = producer;
    }

    public async Task SendMessageAsync()
    {
        var message = new MyMessage { Text = "Hello RabbitMQ!" };

        // Publish to default queue (based on message type)
        await _producer.PublishAsync(message);

        // Or publish to specific exchange/routing key
        await _producer.PublishAsync("my-exchange", "routing.key", message);
    }

    public async Task SendBatchAsync(IEnumerable<MyMessage> messages)
    {
        // High-performance batch publishing
        await _producer.PublishAsync(messages, batchSize: 100);
    }
}
```

### 3. Consume Messages

```csharp
var serverConfig = new RabbitMqServerConfig { /* ... */ };
var consumerConfig = new ConsumerConfig
{
    Queue = new QueueParamsConfig { Name = "my-queue" },
    ConsumersCount = 3,
    PrefetchCount = 10,
    Retry = new RetryConfig
    {
        // Retry with 5s, 30s, 2min TTLs
        PerMessageTtlOnRetryMs = new[] { "5000", "30000", "120000" }
    }
};

var consumers = ConsumerFactory.CreateConsumers<MyMessage>(
    serverConfig,
    consumerConfig,
    serviceProvider,
    new MySerializer(),
    consumerClientProvidedName: "my-consumer");

consumers.SubscribeAsync(
    onDequeue: async (message, sp, context) =>
    {
        // Process message
        Console.WriteLine($"Received: {message}");
    },
    onException: async (message, sp, context, ex) =>
    {
        // Handle errors
        Console.WriteLine($"Error processing: {ex.Message}");
    });
```

## Configuration

### Server Configuration

```csharp
var serverConfig = new RabbitMqServerConfig
{
    HostName = "localhost",
    Port = 5672,
    UserName = "guest",
    Password = "guest",
    VirtualHost = "/",
    ClientProvidedName = "my-app",
    ReconnectCount = 5,
    ReconnectIntervalInSeconds = 3
};
```

### Consumer Configuration with Retry

```csharp
var consumerConfig = new ConsumerConfig
{
    Queue = new QueueParamsConfig
    {
        Name = "orders-queue",
        Durable = true,
        AutoDelete = false
    },
    Exchange = new ExchangeParamsConfig
    {
        Name = "orders-exchange",
        Type = "topic"
    },
    RoutingKeys = new[] { "orders.*" },
    ConsumersCount = 5,
    PrefetchCount = 20,
    Retry = new RetryConfig
    {
        // 3 retry attempts: 5s, 30s, 2min
        PerMessageTtlOnRetryMs = new[] { "5000", "30000", "120000" }
    },
    PublisherConfirms = new PublisherConfirmsConfig
    {
        Enabled = true,
        WaitConfirmsMilliseconds = 5000
    }
};
```

### Channel Pool Configuration

```csharp
var producerConfig = new ProducerConfig
{
    ChannelPool = new ChannelPoolConfig
    {
        MinPoolSize = 10,    // Initial pool size
        MaxPoolSize = 50,    // Maximum pool size
        WaitTimeoutMs = 5000 // Timeout when pool exhausted
    }
};
```

## Retry Mechanism

SharpMQ implements a sophisticated retry mechanism using RabbitMQ's TTL and dead letter exchange:

1. Failed messages are published to a retry exchange with a TTL
2. After TTL expires, messages are routed back to the original queue
3. Retry count is tracked in message headers
4. Multiple TTL tiers supported for exponential backoff
5. After max retries, messages go to dead letter queue (if enabled)

## Custom Serializer

Implement the `RabbitSerializer` abstract class:

```csharp
public class MyJsonSerializer : RabbitSerializer
{
    public override T Deserialize<T>(ReadOnlySpan<byte> data, RabbitSerializerOptions options)
    {
        return JsonSerializer.Deserialize<T>(data);
    }

    public override byte[] Serialize<T>(T obj, RabbitSerializerOptions options)
    {
        return JsonSerializer.SerializeToUtf8Bytes(obj);
    }
}
```

## Examples

See the `samples/Sample.App` directory for complete working examples:

- **BasicQueueExample**: Simple publish/consume pattern
- **TopicExchangeExample**: Topic exchange with routing patterns
- **RetryExampleWorker**: Retry mechanism with multiple TTL tiers

## License

MIT