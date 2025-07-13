using System;
using System.Linq;
using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class QueueArgConfig
    {
        public string Key { get; set; }

        public object Value { get; set; }

        public QueueArgConfig()
        {
        }

        public QueueArgConfig(string key, object value)
        {
            Key = key;
            Value = value;
        }

        public void Validate()
        {
            if (!ConfigConstants.QueueArgKeys.Keys.Any(x => string.Equals(Key, x, StringComparison.InvariantCultureIgnoreCase))) throw new RabbitMqConfigValidationException("QueueArg Key is not valid");

            if (Value == null) throw new RabbitMqConfigValidationException("QueueArg Value is null");
        }
    }
}