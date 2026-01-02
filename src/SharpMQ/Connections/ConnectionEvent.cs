using System;

namespace SharpMQ.Connections
{
    /// <summary>
    /// Represents a connection lifecycle event
    /// </summary>
    public class ConnectionEvent
    {
        /// <summary>
        /// Gets the type of the connection event
        /// </summary>
        public ConnectionEventType EventType { get; }

        /// <summary>
        /// Gets the timestamp when the event occurred
        /// </summary>
        public DateTimeOffset Timestamp { get; }

        /// <summary>
        /// Gets the message associated with the event
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Gets the exception associated with the event, if any
        /// </summary>
        public Exception Exception { get; }

        public ConnectionEvent(ConnectionEventType eventType, string message, Exception exception = null)
        {
            EventType = eventType;
            Message = message;
            Exception = exception;
            Timestamp = DateTimeOffset.UtcNow;
        }

        public override string ToString()
        {
            var exceptionInfo = Exception != null ? $" - Exception: {Exception.Message}" : string.Empty;
            return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {EventType}: {Message}{exceptionInfo}";
        }
    }

    /// <summary>
    /// Represents the type of connection lifecycle event
    /// </summary>
    public enum ConnectionEventType
    {
        /// <summary>
        /// Connection is being established
        /// </summary>
        Connecting,

        /// <summary>
        /// Connection has been successfully established
        /// </summary>
        Connected,

        /// <summary>
        /// Connection is being recovered after a failure
        /// </summary>
        Recovering,

        /// <summary>
        /// Connection has been successfully recovered
        /// </summary>
        Recovered,

        /// <summary>
        /// Connection has been shutdown
        /// </summary>
        Shutdown,

        /// <summary>
        /// Connection has been blocked by the broker
        /// </summary>
        Blocked,

        /// <summary>
        /// Connection has been unblocked by the broker
        /// </summary>
        Unblocked,

        /// <summary>
        /// An error occurred with the connection
        /// </summary>
        Error,

        /// <summary>
        /// A callback exception occurred
        /// </summary>
        CallbackException,

        /// <summary>
        /// Connection is being disposed
        /// </summary>
        Disposing,

        /// <summary>
        /// Connection has been disposed
        /// </summary>
        Disposed
    }
}
