using System;
using System.Collections;
using System.Collections.Generic;
using SharpMQ.Abstractions;
using SharpMQ.Connections;

namespace SharpMQ.Consumers
{
    /// <summary>
    /// A disposable wrapper around a group of consumers that share a single connection.
    /// Disposing the group disposes all consumers and then the shared connection.
    /// </summary>
    internal sealed class ConsumerGroup<T> : IConsumerGroup<T> where T : class
    {
        private readonly IReadOnlyList<IConsumer<T>> _consumers;
        private readonly IConnectionProvider _sharedConnection;
        private bool _disposed;

        public ConsumerGroup(IReadOnlyList<IConsumer<T>> consumers, IConnectionProvider sharedConnection = null)
        {
            _consumers = consumers ?? throw new ArgumentNullException(nameof(consumers));
            _sharedConnection = sharedConnection;
        }

        public int Count => _consumers.Count;

        public IEnumerator<IConsumer<T>> GetEnumerator() => _consumers.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Dispose all consumers first (closes channels)
            foreach (var consumer in _consumers)
            {
                try
                {
                    consumer?.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed
                }
            }

            // Then dispose the shared connection
            if (_sharedConnection != null)
            {
                try
                {
                    _sharedConnection.Dispose();
                }
                catch (ObjectDisposedException)
                {
                    // already disposed
                }
            }
        }
    }
}
