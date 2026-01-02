using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    /// <summary>
    /// Manages RabbitMQ connection lifecycle with async support, health monitoring, and graceful shutdown
    /// </summary>
    public interface IConnectionProvider : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets whether consumers are dispatched asynchronously
        /// </summary>
        bool IsDispatchConsumersAsyncEnabled { get; }

        /// <summary>
        /// Gets or creates a connection to RabbitMQ with cancellation support
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>An active RabbitMQ connection</returns>
        Task<IConnection> GetOrCreateAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current health status of the connection
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Connection health information</returns>
        Task<ConnectionHealth> GetHealthAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Event raised when a connection lifecycle event occurs
        /// </summary>
        event EventHandler<ConnectionEvent> ConnectionEventOccurred;
    }
}