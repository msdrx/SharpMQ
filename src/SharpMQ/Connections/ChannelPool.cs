using System;
using System.Collections.Concurrent;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    internal class ChannelPool : IChannelPool
    {
        private readonly IConnectionManager _connectionManager;
        private readonly int _minPoolSize;
        private readonly int _maxPoolSize;
        private readonly int _waitTimeoutMs;

        private static readonly object _poolLocker = new object();
        private readonly BlockingCollection<IModel> _channelPool;


        public ChannelPool(IConnectionManager connectionManager,
            int minPoolSize,
            int maxPoolSize,
            int waitTimeoutMs
            )
        {
            _connectionManager = connectionManager;

            _minPoolSize = minPoolSize;
            _maxPoolSize = maxPoolSize;
            _waitTimeoutMs = waitTimeoutMs;

            _channelPool = new BlockingCollection<IModel>();
            CreatePool(_connectionManager.GetOrCreateConnection());
        }



        public IModel GetChannel()
        {
            try
            {
                if (_channelPool.TryTake(out var channel, _waitTimeoutMs))
                {
                    if (channel?.IsOpen ?? false) return channel;

                    return _connectionManager.GetOrCreateConnection().CreateModel();
                }
                else
                {
                    return _connectionManager.GetOrCreateConnection().CreateModel();
                }
            }
            catch
            {
                return _connectionManager.GetOrCreateConnection().CreateModel();
            }
        }

        public void AddOrCloseChannel(IModel channel)
        {
            if (channel == null) return;

            if (_channelPool.Count <= _maxPoolSize && channel.IsOpen)
            {
                _channelPool.Add(channel);
            }
            else
            {
                channel.Close();
                channel.Dispose();
            }
        }

        private void CreatePool(IConnection connection)
        {
            lock (_poolLocker)
            {
                for (int i = 0; i < _minPoolSize; i++)
                {
                    _channelPool.Add(connection.CreateModel());
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            try
            {
                _connectionManager?.Dispose();
            }
            catch (ObjectDisposedException)
            {
                //if already disposed its ok
            }
        }
    }
}
