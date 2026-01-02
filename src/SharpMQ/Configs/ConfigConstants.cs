using System.Collections.Generic;

namespace SharpMQ.Configs
{
    public static class ConfigConstants
    {
        public static class Default
        {
            public const int MAX_RECONNECT_COUNT = 3;
            public const int RECONNECT_INTERVAL_SECONDS = 10;
            public const int NETWORK_RECOVERY_INTERVAL_SECONDS = 5;

            public const uint PREFETCH_SIZE = 0u;
            public const ushort PREFETCH_COUNT = 1;
        }

        public static class QueueArgKeys
        {
            public static readonly IReadOnlyList<string> Keys = new List<string>()
            {
                AutoExpire,
                MaxPriority,
                MessageTTL,
                DLExchage,
                DLExchangeRoutingKey,
                SingleActiveConsumer
            };

            public const string AutoExpire = "x-expires";

            public const string MaxPriority = "x-max-priority";

            public const string MessageTTL = "x-message-ttl";

            public const string DLExchage = "x-dead-letter-exchange";

            public const string DLExchangeRoutingKey = "x-dead-letter-routing-key";

            public const string SingleActiveConsumer = "x-single-active-consumer";
        }

        public static class BasicPropertyHeaders
        {
            public const string XRetries = "x-retries";
        }

        public static class Exchanges
        {
            public const string Direct = "direct";
            public const string Fanout = "fanout";
            public const string Topic = "topic";
            public const string Headers = "headers";

            public static readonly IReadOnlyCollection<string> All = new List<string>() { Direct, Fanout, Topic, Headers };
        }
    }
}
