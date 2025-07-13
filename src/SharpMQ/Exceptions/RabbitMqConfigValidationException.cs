namespace SharpMQ.Exceptions
{
    public class RabbitMqConfigValidationException : RabbitMqException
    {
        public RabbitMqConfigValidationException(string message) : base(message)
        {
        }
    }
}
