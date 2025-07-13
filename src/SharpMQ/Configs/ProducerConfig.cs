using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class ProducerConfig
    {
        public PublisherConfirmsConfig PublisherConfirms { get; set; }

        public ChannelPoolConfig ChannelPool { get; set; }

        internal bool IsPublisherConfirmsEnabled()
        {
            return PublisherConfirms != null;
        }

        public void Validate()
        {
            if (ChannelPool == null
                || ChannelPool.MinPoolSize <= 0
                || ChannelPool.MaxPoolSize <= 0
                || ChannelPool.MinPoolSize >= ChannelPool.MaxPoolSize
                || ChannelPool.WaitTimeoutMs <= 0)
                throw new RabbitMqConfigValidationException("Producer ChannelPool config is invalid");

            PublisherConfirms?.Validate();
        }
    }

    public class ChannelPoolConfig
    {
        public int MinPoolSize { get; set; }
        public int MaxPoolSize { get; set; }
        public int WaitTimeoutMs { get; set; }
    }
}
