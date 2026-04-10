using FluentAssertions;
using SharpMQ.Configs;
using SharpMQ.Extensions;
using Xunit;

namespace SharpMQ.Unit.Test.Extensions
{
    public class ChannelExtensionsTests
    {
        #region ConfigureConsumerChannel: does not mutate config.Queue.Name

        [Fact]
        public void ConfigureConsumerChannel_Should_NotMutateQueueName_When_UseMessageTypeAsQueueNameIsFalse()
        {
            // Arrange
            const string originalName = "my-custom-queue";
            var config = new ConsumerConfig
            {
                ConsumersCount = 1,
                Queue = new QueueParamsConfig
                {
                    Name = originalName,
                    UseMessageTypeAsQueueName = false,
                },
            };

            // Act
            var resolvedName = ChannelExtensions.ResolveQueueName<ExtensionTestMessage>(config);

            // Assert
            config.Queue.Name.Should().Be(originalName, "ConfigureConsumerChannel must not mutate config.Queue.Name");
            resolvedName.Should().Be(originalName);
        }

        [Fact]
        public void ConfigureConsumerChannel_Should_NotMutateQueueName_When_UseMessageTypeAsQueueNameIsTrue()
        {
            // Arrange
            var config = new ConsumerConfig
            {
                ConsumersCount = 1,
                Queue = new QueueParamsConfig
                {
                    Name = null,
                    UseMessageTypeAsQueueName = true,
                },
            };

            // Act
            var resolvedName = ChannelExtensions.ResolveQueueName<ExtensionTestMessage>(config);

            // Assert
            config.Queue.Name.Should().BeNull("config.Queue.Name should remain null when UseMessageTypeAsQueueName is true");
            resolvedName.Should().Be(typeof(ExtensionTestMessage).FullName);
        }

        #endregion
    }

    /// <summary>
    /// Dummy message type used as a generic type argument in extension tests.
    /// </summary>
    public class ExtensionTestMessage
    {
        public string Content { get; set; }
    }
}
