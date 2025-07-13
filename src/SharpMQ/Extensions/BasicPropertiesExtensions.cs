using System;
using System.Collections.Generic;
using SharpMQ.Configs;
using RabbitMQ.Client;

namespace SharpMQ.Extensions
{
    internal static class BasicPropertiesExtensions
    {
        public static int GetRetryCount(this IBasicProperties properties, int max_retry_count)
        {
            try
            {
                return (int?)properties.Headers?[ConfigConstants.BasicPropertyHeaders.XRetries] ?? max_retry_count;
            }
            catch
            {
                return max_retry_count;
            }
        }

        public static void WithRetryCount(this IBasicProperties properties, int retryCount)
        {
            if (properties?.Headers == null)
            {
                properties.Headers = new Dictionary<string, object>();
            }
            properties.Headers[ConfigConstants.BasicPropertyHeaders.XRetries] = retryCount;
        }

        public static IBasicProperties WithPriority(this IBasicProperties properties, int? priority = null)
        {
            if (!priority.HasValue)
            {
                return properties;
            }
            properties.Priority = Convert.ToByte(priority.Value);
            return properties;
        }
    }

}