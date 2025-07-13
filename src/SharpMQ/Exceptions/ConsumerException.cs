namespace SharpMQ.Exceptions
{

    public class ConsumerException : RabbitMqException
    {
        public ConsumerException(string message) : base(message)
        {
        }
    }
}
