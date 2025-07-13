namespace SharpMQ.Abstractions
{
    /// <summary>
    /// Basic properties and in <see cref="MessageBasicDeliverEventArgs.Instance"/> property BasicDeliverEventArgs instance is stored
    /// </summary>
    public sealed class MessageBasicDeliverEventArgs
    {
        ///<summary>The consumer tag of the consumer that the message
        ///was delivered to.</summary>
        public string ConsumerTag { get; set; }

        ///<summary>The delivery tag for this delivery. See
        ///IModel.BasicAck.</summary>
        public ulong DeliveryTag { get; set; }

        ///<summary>The exchange the message was originally published
        ///to.</summary>
        public string Exchange { get; set; }

        ///<summary>The AMQP "redelivered" flag.</summary>
        public bool Redelivered { get; set; }

        ///<summary>The routing key used when the message was
        ///originally published.</summary>
        public string RoutingKey { get; set; }

        /// <summary>
        /// RabbitMQ BasicDeliverEventArgs
        /// </summary>
        public object Instance { get; set; }
    }
}