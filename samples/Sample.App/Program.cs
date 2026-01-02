using Sample.App;
using Sample.App.Shared;
using SharpMQ;
using SharpMQ.Configs;
using SharpMQ.Extensions;

var builder = Host.CreateApplicationBuilder(args);

var configuration = builder.Services.BuildServiceProvider().GetRequiredService<IConfiguration>();
var serverConfig = configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();
var producerConfig = configuration.GetRequiredSection("ProducerTest").Get<ProducerConfig>();

// Register producer
builder.Services
    .AddProducer("topic-producer", producerConfig, serverConfig, new CustomJsonSerializer())
    .OpenProducersConnectionsOnHostStartup();

//builder.Services.AddHostedService<ConsumerWorker>();
//builder.Services.AddHostedService<PublisherWorker>();
builder.Services.AddHostedService<RetryExampleWorker>();

var host = builder.Build();
host.Run();
