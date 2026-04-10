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
            if (ChannelPool == null)
                throw new RabbitMqConfigValidationException("Producer ChannelPool is required");

            if (ChannelPool.MinPoolSize <= 0)
                throw new RabbitMqConfigValidationException("Producer ChannelPool MinPoolSize must be greater than 0");

            if (ChannelPool.MaxPoolSize <= 0)
                throw new RabbitMqConfigValidationException("Producer ChannelPool MaxPoolSize must be greater than 0");

            if (ChannelPool.MinPoolSize >= ChannelPool.MaxPoolSize)
                throw new RabbitMqConfigValidationException("Producer ChannelPool MinPoolSize must be less than MaxPoolSize");

            if (ChannelPool.WaitTimeoutMs <= 0)
                throw new RabbitMqConfigValidationException("Producer ChannelPool WaitTimeoutMs must be greater than 0");

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
