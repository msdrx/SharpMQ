using System.Collections.Generic;
using System.Linq;
using SharpMQ.Exceptions;
using RabbitMQ.Client;

namespace SharpMQ.Configs
{
    public class RabbitMqServerConfig
    {
        public string UserName { get; set; }

        public string Password { get; set; }

        public string VirtualHost { get; set; }
        public string[] Hosts { get; set; }

        public string ClientProvidedName { get; set; }

        public int? ReconnectCount { get; set; }
        public int? ReconnectIntervalInSeconds { get; set; }

        public IEnumerable<AmqpTcpEndpoint> MqHosts()
        {
            foreach (var host in Hosts)
            {
                yield return new AmqpTcpEndpoint(host, 5672);
            }
        }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(UserName)) throw new RabbitMqConfigValidationException("RabbitMq Server UserName is null or empty");
            if (string.IsNullOrWhiteSpace(Password)) throw new RabbitMqConfigValidationException("RabbitMq Server Password is null or empty");
            if (string.IsNullOrWhiteSpace(VirtualHost)) throw new RabbitMqConfigValidationException("RabbitMq Server VirtualHost is null or empty");
            if (string.IsNullOrWhiteSpace(ClientProvidedName)) throw new RabbitMqConfigValidationException("ClientProvidedName is null or empty");

            if (ReconnectCount.HasValue && ReconnectCount <= 0) throw new RabbitMqConfigValidationException("RabbitMq Server ReconnectCount is <= 0");

            if (ReconnectIntervalInSeconds.HasValue && ReconnectIntervalInSeconds <= 0) throw new RabbitMqConfigValidationException("RabbitMq Server ReconnectIntervalInSeconds is <= 0");

            if (Hosts == null || !Hosts.Any() || Hosts.Any(x => string.IsNullOrWhiteSpace(x)))
            {
                throw new RabbitMqConfigValidationException("RabbitMq Server Hosts is null or empty");
            }
        }
    }
}
