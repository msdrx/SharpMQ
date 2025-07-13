using SharpMQ.Exceptions;

namespace SharpMQ.Configs
{
    public class QueueParamsConfig
    {
        public string Name { get; set; }
        public bool UseMessageTypeAsQueueName { get; set; }

        public QueueArgConfig[] QueueArgs { get; set; }

        //add args for binding (BindingArgs). for header exchange this args configured in binding, are for routing.

        public void Validate()
        {
            if (UseMessageTypeAsQueueName && !string.IsNullOrWhiteSpace(Name)) throw new RabbitMqConfigValidationException("Queue name should not be provided when UseMessageTypeAsQueueName is true");
            if (!UseMessageTypeAsQueueName && string.IsNullOrWhiteSpace(Name)) throw new RabbitMqConfigValidationException("Queue name is required when UseMessageTypeAsQueueName is false");

            if (QueueArgs != null)
            {
                foreach (var item in QueueArgs)
                {
                    item.Validate();
                }
            }
        }
    }
}