using Sample.App;
using SharpMQ;
using SharpMQ.Configs;
using SharpMQ.Extensions;

var builder = Host.CreateApplicationBuilder(args);

var configuration = builder.Services.BuildServiceProvider().GetRequiredService<IConfiguration>();
var serverConfig = configuration.GetRequiredSection("ServerTest").Get<RabbitMqServerConfig>();
var producerConfig = configuration.GetRequiredSection("ProducerTest").Get<ProducerConfig>();

//builder.Services.AddProducers("Test_Producer",
//                      new CustomJsonSerializer(),
//                      new CustomJsonSerializerOptions(JsonConstants.PublisherDefault),
//                      openProducerConnectionsOnSturtup: true,
//                      new ProducerAndServerConfig("Test_Producer", producerConfig, serverConfig));

//builder.Services.AddProducer("Test_Producer", producerConfig, serverConfig, new NewtonsoftJsonSerializer());

//builder.Services.AddRawConnection(serverConfig);

builder.Services.AddProducer("topic-producer", producerConfig, serverConfig, new CustomJsonSerializer()).OpenProducersConnectionsOnHostStartup();


builder.Services.AddHostedService<ConsumerWorker>();
builder.Services.AddHostedService<PublisherWorker>();

var host = builder.Build();
host.Run();
