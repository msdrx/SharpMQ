using FluentAssertions;
using SharpMQ.Configs;
using SharpMQ.Exceptions;
using Xunit;

namespace SharpMQ.Unit.Test.Configs
{
    public class RetryConfigTests
    {
        #region Validate: long[] PerMessageTtlOnRetryMs

        [Fact]
        public void Validate_Should_Pass_When_AllTtlValues_AreValidLongs()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = new long[] { 500, 1000, 60000, long.MaxValue }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Validate_Should_Pass_When_SingleTtlValue_IsExactlyMinimum()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = new long[] { 500 }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().NotThrow();
        }

        [Fact]
        public void Validate_Should_Throw_When_PerMessageTtlOnRetryMs_IsNull()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = null
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*PerMessageTtlOnRetryMs*null or empty*");
        }

        [Fact]
        public void Validate_Should_Throw_When_PerMessageTtlOnRetryMs_IsEmpty()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = System.Array.Empty<long>()
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*PerMessageTtlOnRetryMs*null or empty*");
        }

        [Fact]
        public void Validate_Should_Throw_When_AnyTtlValue_IsBelowMinimum()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = new long[] { 1000, 200, 5000 }
            };

            // Act
            var act = () => config.Validate();

            // Assert — should report the index and value of the offending element
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*PerMessageTtlOnRetryMs[1]*200*must be >= 500*");
        }

        [Fact]
        public void Validate_Should_Throw_When_FirstTtlValue_IsZero()
        {
            // Arrange
            var config = new RetryConfig
            {
                PerMessageTtlOnRetryMs = new long[] { 0 }
            };

            // Act
            var act = () => config.Validate();

            // Assert
            act.Should().Throw<RabbitMqConfigValidationException>()
                .WithMessage("*PerMessageTtlOnRetryMs[0]*0*must be >= 500*");
        }

        #endregion
    }
}
