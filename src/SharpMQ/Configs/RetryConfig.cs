using System;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class RetryConfig
    {
        [Obsolete("PerQueueTtlMs is obsolete and will be removed in a future version. " +
                  "Use PerMessageTtlOnRetryMs to define tier-specific TTLs.")]
        public long PerQueueTtlMs { get; set; }

        public long[] PerMessageTtlOnRetryMs { get; set; }

        public void Validate()
        {
            if (PerMessageTtlOnRetryMs is null || PerMessageTtlOnRetryMs?.Length == 0)
                throw new RabbitMqConfigValidationException(
                    "RetryConfig PerMessageTtlOnRetryMs cannot be null or empty");

            for (int i = 0; i < PerMessageTtlOnRetryMs.Length; i++)
            {
                if (PerMessageTtlOnRetryMs[i] < 500)
                {
                    throw new RabbitMqConfigValidationException(
                        $"RetryConfig PerMessageTtlOnRetryMs[{i}] is {PerMessageTtlOnRetryMs[i]}ms, must be >= 500ms");
                }
            }
        }
    }
}
