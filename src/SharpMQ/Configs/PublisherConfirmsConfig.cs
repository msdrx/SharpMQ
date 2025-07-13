using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class PublisherConfirmsConfig
    {
        public long WaitConfirmsMilliseconds { get; set; }

        public void Validate()
        {

            if (WaitConfirmsMilliseconds < 10) throw new RabbitMqConfigValidationException("PublisherConfirmsConfig WaitConfirmsMilliseconds is <= 10");
        }
    }
}