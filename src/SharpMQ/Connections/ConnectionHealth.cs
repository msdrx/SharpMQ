using System;

namespace SharpMQ.Connections
{
    /// <summary>
    /// Represents the health status of a RabbitMQ connection
    /// </summary>
    public class ConnectionHealth
    {
        /// <summary>
        /// Gets the current health status
        /// </summary>
        public ConnectionHealthStatus Status { get; }

        /// <summary>
        /// Gets whether the connection is currently open
        /// </summary>
        public bool IsConnected { get; }

        /// <summary>
        /// Gets the connection uptime
        /// </summary>
        public TimeSpan? Uptime { get; }

        /// <summary>
        /// Gets the timestamp of the last successful health check
        /// </summary>
        public DateTimeOffset LastCheckTime { get; }

        /// <summary>
        /// Gets additional details about the health status
        /// </summary>
        public string Details { get; }

        public ConnectionHealth(
            ConnectionHealthStatus status,
            bool isConnected,
            TimeSpan? uptime = null,
            string details = null)
        {
            Status = status;
            IsConnected = isConnected;
            Uptime = uptime;
            LastCheckTime = DateTimeOffset.UtcNow;
            Details = details;
        }

        /// <summary>
        /// Creates a healthy connection health instance
        /// </summary>
        public static ConnectionHealth Healthy(TimeSpan uptime) =>
            new ConnectionHealth(ConnectionHealthStatus.Healthy, true, uptime, "Connection is healthy");

        /// <summary>
        /// Creates an unhealthy connection health instance
        /// </summary>
        public static ConnectionHealth Unhealthy(string details) =>
            new ConnectionHealth(ConnectionHealthStatus.Unhealthy, false, null, details);

        /// <summary>
        /// Creates a degraded connection health instance
        /// </summary>
        public static ConnectionHealth Degraded(string details) =>
            new ConnectionHealth(ConnectionHealthStatus.Degraded, true, null, details);

        /// <summary>
        /// Creates a disconnected connection health instance
        /// </summary>
        public static ConnectionHealth Disconnected(string details = "Connection is not established") =>
            new ConnectionHealth(ConnectionHealthStatus.Disconnected, false, null, details);
    }

    /// <summary>
    /// Represents the health status of a connection
    /// </summary>
    public enum ConnectionHealthStatus
    {
        /// <summary>
        /// Connection is healthy and operating normally
        /// </summary>
        Healthy,

        /// <summary>
        /// Connection is degraded but still operational
        /// </summary>
        Degraded,

        /// <summary>
        /// Connection is unhealthy and may not be operational
        /// </summary>
        Unhealthy,

        /// <summary>
        /// Connection is not established
        /// </summary>
        Disconnected
    }
}
