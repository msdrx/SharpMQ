using System;
using System.Linq;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class RetryConfig
    {
        [Obsolete("PerQueueTtlMs is obsolete and will be removed in a future version. " +
                  "Use PerMessageTtlOnRetryMs to define tier-specific TTLs.")]
        public long PerQueueTtlMs { get; set; }

        public string[] PerMessageTtlOnRetryMs { get; set; }

        public void Validate()
        {
            if (!PerMessageTtlOnRetryMs?.Any() ?? true)
                throw new RabbitMqConfigValidationException(
                    "RetryConfig PerMessageTtlOnRetryMs cannot be null or empty");

            for (int i = 0; i < PerMessageTtlOnRetryMs.Length; i++)
            {
                if (!long.TryParse(PerMessageTtlOnRetryMs[i], out long ttlMs))
                {
                    throw new RabbitMqConfigValidationException(
                        $"RetryConfig PerMessageTtlOnRetryMs[{i}] is not a valid number: '{PerMessageTtlOnRetryMs[i]}'");
                }

                if (ttlMs < 500)
                {
                    throw new RabbitMqConfigValidationException(
                        $"RetryConfig PerMessageTtlOnRetryMs[{i}] is {ttlMs}ms, must be >= 500ms");
                }
            }
        }
    }
}
