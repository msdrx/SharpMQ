using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using RabbitMQ.Client;
using SharpMQ.Configs;
using SharpMQ.Extensions;
using Xunit;

namespace SharpMQ.Unit.Test.Extensions
{
    public class BasicPropertiesExtensionsTests
    {
        #region GetRetryCount: missing header, valid header, corrupt header

        [Fact]
        public void GetRetryCount_Should_ReturnMaxRetryCount_When_HeaderIsMissing()
        {
            // Arrange
            var props = new Mock<IBasicProperties>();
            props.SetupGet(p => p.Headers).Returns(new Dictionary<string, object>());
            const int maxRetryCount = 5;

            // Act
            var result = props.Object.GetRetryCount(maxRetryCount);

            // Assert — missing header should return the max retry count (default)
            result.Should().Be(maxRetryCount);
        }

        [Fact]
        public void GetRetryCount_Should_ReturnMaxRetryCount_When_HeadersIsNull()
        {
            // Arrange
            var props = new Mock<IBasicProperties>();
            props.SetupGet(p => p.Headers).Returns((IDictionary<string, object>)null);
            const int maxRetryCount = 3;

            // Act
            var result = props.Object.GetRetryCount(maxRetryCount);

            // Assert
            result.Should().Be(maxRetryCount);
        }

        [Fact]
        public void GetRetryCount_Should_ReturnCorrectCount_When_HeaderIsValid()
        {
            // Arrange
            var headers = new Dictionary<string, object>
            {
                { ConfigConstants.BasicPropertyHeaders.XRetries, 2 }
            };
            var props = new Mock<IBasicProperties>();
            props.SetupGet(p => p.Headers).Returns(headers);
            const int maxRetryCount = 5;

            // Act
            var result = props.Object.GetRetryCount(maxRetryCount);

            // Assert
            result.Should().Be(2);
        }

        [Fact]
        public void GetRetryCount_Should_Throw_When_HeaderIsCorruptNonInteger()
        {
            // Arrange — a corrupt header value that cannot be cast to int
            var headers = new Dictionary<string, object>
            {
                { ConfigConstants.BasicPropertyHeaders.XRetries, "not-a-number" }
            };
            var props = new Mock<IBasicProperties>();
            props.SetupGet(p => p.Headers).Returns(headers);
            const int maxRetryCount = 5;

            // Act — GetRetryCount now only catches KeyNotFoundException,
            // so an InvalidCastException should propagate.
            var act = () => props.Object.GetRetryCount(maxRetryCount);

            // Assert
            act.Should().Throw<InvalidCastException>();
        }

        #endregion
    }
}
