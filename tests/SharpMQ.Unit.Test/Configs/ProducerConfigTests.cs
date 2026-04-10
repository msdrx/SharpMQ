using FluentAssertions;
using SharpMQ.Configs;
using SharpMQ.Exceptions;
using Xunit;

namespace SharpMQ.Unit.Test.Configs
{
    public class ProducerConfigTests
    {
        #region Validate: per-field error messages

        [Fact]
        public void Validate_Should_ThrowWithSpecificMessage_When_ChannelPoolIsNull()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = null
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*ChannelPool*required*");
        }

        [Fact]
        public void Validate_Should_ThrowWithSpecificMessage_When_MinPoolSizeIsZero()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 0,
                    MaxPoolSize = 5,
                    WaitTimeoutMs = 5000
                }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*MinPoolSize*greater than 0*");
        }

        [Fact]
        public void Validate_Should_ThrowWithSpecificMessage_When_MaxPoolSizeIsZero()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 1,
                    MaxPoolSize = 0,
                    WaitTimeoutMs = 5000
                }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*MaxPoolSize*greater than 0*");
        }

        [Fact]
        public void Validate_Should_ThrowWithSpecificMessage_When_MinPoolSizeGreaterOrEqualToMaxPoolSize()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 5,
                    MaxPoolSize = 5,
                    WaitTimeoutMs = 5000
                }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*MinPoolSize*less than MaxPoolSize*");
        }

        [Fact]
        public void Validate_Should_ThrowWithSpecificMessage_When_WaitTimeoutMsIsZero()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 1,
                    MaxPoolSize = 5,
                    WaitTimeoutMs = 0
                }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*WaitTimeoutMs*greater than 0*");
        }

        [Fact]
        public void Validate_Should_Pass_When_AllFieldsAreValid()
        {
            // Arrange
            var config = new ProducerConfig
            {
                ChannelPool = new ChannelPoolConfig
                {
                    MinPoolSize = 1,
                    MaxPoolSize = 5,
                    WaitTimeoutMs = 5000
                }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Validate_Should_HaveDistinctMessages_ForEachField()
        {
            // This test verifies that each validation rule produces a distinct error message
            // rather than a single generic "config is invalid" message.

            // ChannelPool null
            var ex1 = Assert.Throws<RabbitMqConfigValidationException>(() =>
                new ProducerConfig { ChannelPool = null }.Validate());

            // MinPoolSize invalid
            var ex2 = Assert.Throws<RabbitMqConfigValidationException>(() =>
                new ProducerConfig { ChannelPool = new ChannelPoolConfig { MinPoolSize = 0, MaxPoolSize = 5, WaitTimeoutMs = 5000 } }.Validate());

            // MaxPoolSize invalid
            var ex3 = Assert.Throws<RabbitMqConfigValidationException>(() =>
                new ProducerConfig { ChannelPool = new ChannelPoolConfig { MinPoolSize = 1, MaxPoolSize = 0, WaitTimeoutMs = 5000 } }.Validate());

            // WaitTimeoutMs invalid
            var ex4 = Assert.Throws<RabbitMqConfigValidationException>(() =>
                new ProducerConfig { ChannelPool = new ChannelPoolConfig { MinPoolSize = 1, MaxPoolSize = 5, WaitTimeoutMs = 0 } }.Validate());

            // Assert — all messages should be distinct
            var messages = new[] { ex1.Message, ex2.Message, ex3.Message, ex4.Message };
            messages.Should().OnlyHaveUniqueItems("each field should produce its own specific error message");
        }

        #endregion
    }
}
