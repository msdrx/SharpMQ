using System;
using System.Collections.Generic;
using System.Linq;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class ExchangeParamsConfig
    {
        public string Name { get; set; }

        public string Type { get; set; }

        public bool DeclareExchange { get; set; }

        public string[] RoutingKeys { get; set; }

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new RabbitMqConfigValidationException("ExchangeParams Name is null or empty");

            if (!ConfigConstants.Exchanges.All.Contains(Type?.ToLower())) throw new RabbitMqConfigValidationException("ExchangeParams Type is not valid");

            if (Type != ConfigConstants.Exchanges.Fanout &&
                (RoutingKeys is null || RoutingKeys.Any(x => string.IsNullOrWhiteSpace(x)))) throw new RabbitMqConfigValidationException("Exchange RoutingKeys is null or has empty item");
        }

        internal IEnumerable<string> GetRoutingKeys()
        {
            if (RoutingKeys != null)
            {
                foreach (var key in RoutingKeys.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    yield return key;
                }
            }
        }
    }
}