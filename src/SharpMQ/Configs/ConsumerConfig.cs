using System;
using System.Linq;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class ConsumerConfig
    {
        public uint ConsumersCount { get; set; }
        public uint? PrefetchSize { get; set; }
        public ushort? PrefechCount { get; set; }
        public bool DisableDeadLettering { get; set; }
        public ExchangeParamsConfig[] Exchanges { get; set; }
        public QueueParamsConfig Queue { get; set; }
        public RetryConfig Retry { get; set; }
        public PublisherConfirmsConfig PublisherConfirms { get; set; }

        internal bool IsPublisherConfirmsEnabled()
        {
            return PublisherConfirms != null;
        }

        internal bool IsRetryEnabled()
        {
            return Retry != null;
        }

        public void Validate()
        {
            if (ConsumersCount <= 0) throw new RabbitMqConfigValidationException("Consumer ConsumersCount is <= 0");

            if (Exchanges != null)
            {
                if (Exchanges.Any(x => x is null)) throw new RabbitMqConfigValidationException("One of Exchanges item is null");
                foreach (var item in Exchanges)
                {
                    item.Validate();
                }
            }

            if (Queue == null) throw new RabbitMqConfigValidationException("Consumer Queue is null");

            Queue.Validate();

            if (IsRetryEnabled())
            {
                Retry.Validate();

                if (Queue.QueueArgs != null
                    && Queue.QueueArgs.Any(x =>
                    string.Equals(x.Key, ConfigConstants.QueueArgKeys.DLExchage, StringComparison.InvariantCultureIgnoreCase)
                    || string.Equals(x.Key, ConfigConstants.QueueArgKeys.DLExchangeRoutingKey, StringComparison.InvariantCultureIgnoreCase))
                    )
                {
                    throw new RabbitMqConfigValidationException("Consumer: if retry enabled queueArgs DeadLetterExchage and DeadLetterExchangeRoutingKey shoud not be provided, retry logic sets this arguments");
                }

            }


            if (!DisableDeadLettering && Queue.QueueArgs != null
                && Queue.QueueArgs.Any(x =>
                    string.Equals(x.Key, ConfigConstants.QueueArgKeys.DLExchage, StringComparison.InvariantCultureIgnoreCase)
                    || string.Equals(x.Key, ConfigConstants.QueueArgKeys.DLExchangeRoutingKey, StringComparison.InvariantCultureIgnoreCase)))
            {
                throw new RabbitMqConfigValidationException("Consumer: if DeadLettering enabled queueArgs DeadLetterExchage and DeadLetterExchangeRoutingKey shoud not be provided, this arguments will be set by default");
            }



            if (PublisherConfirms != null) PublisherConfirms.Validate();

        }
    }
}