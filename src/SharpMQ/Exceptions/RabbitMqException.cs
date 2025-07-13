using System;

namespace SharpMQ.Exceptions
{
    public class RabbitMqException : Exception
    {
        public RabbitMqException(string message) : base(message)
        {
        }
    }
}
