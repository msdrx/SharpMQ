namespace SharpMQ.Configs
{
    public class ProducerAndServerConfig
    {
        public string ProducerKey { get; private set; }
        public ProducerConfig ProducerConfig { get; private set; }
        public RabbitMqServerConfig ServerConfig { get; private set; }

        public ProducerAndServerConfig(string producerKey, ProducerConfig producerConfig, RabbitMqServerConfig serverConfig)
        {
            ProducerKey = producerKey;
            ProducerConfig = producerConfig;
            ServerConfig = serverConfig;
        }
    }
}
