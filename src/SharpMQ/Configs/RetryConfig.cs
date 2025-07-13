using System.Linq;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class RetryConfig
    {
        public long PerQueueTtlMs { get; set; }
        public string[] PerMessageTtlOnRetryMs { get; set; }

        public void Validate()
        {
            if (PerQueueTtlMs < 500) throw new RabbitMqConfigValidationException("RetryConfig PerQueueTtlMs is <= 500");

            if (!PerMessageTtlOnRetryMs?.Any() ?? true) throw new RabbitMqConfigValidationException("RetryConfig PerMessageTtlOnRetryMs cannot be empty");

            if (PerMessageTtlOnRetryMs.Any(x => long.Parse(x) > PerQueueTtlMs)) throw new RabbitMqConfigValidationException("RetryConfig: PerQueueTtlMs value must be greater than PerMessageTtlOnRetryMs values");
        }
    }
}
