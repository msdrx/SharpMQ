namespace SharpMQ.Exceptions
{
    public class ProducerException : RabbitMqException
    {
        public ProducerException(string message) : base(message)
        {
        }
    }
}
