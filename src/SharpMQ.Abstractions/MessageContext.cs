namespace SharpMQ.Abstractions
{
    public sealed class MessageContext
    {
        public object Sender { get; private set; }
        public bool IsLastTry { get; private set; }
        public MessageBasicDeliverEventArgs BasicDeliverEventArgs { get; private set; }

        public MessageContext(object sender, bool isLastTry, MessageBasicDeliverEventArgs basicDeliverEventArgs)
        {
            Sender = sender;
            IsLastTry = isLastTry;
            BasicDeliverEventArgs = basicDeliverEventArgs;
        }
    }
}
