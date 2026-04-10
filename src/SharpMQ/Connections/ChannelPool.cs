using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    internal class ChannelPool : IChannelPool
    {
        private readonly IConnectionProvider _connectionProvider;
        private readonly int _minPoolSize;
        private readonly int _maxPoolSize;
        private readonly int _waitTimeoutMs;
        private readonly bool _enablePublisherConfirms;

        private readonly SemaphoreSlim _poolLocker = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _poolSizeGate;
        private readonly Channel<IModel> _channelPool;
        private bool _isInitialized;
        private bool _disposed;


        public ChannelPool(IConnectionProvider connectionProvider,
            int minPoolSize,
            int maxPoolSize,
            int waitTimeoutMs,
            bool enablePublisherConfirms = false
            )
        {
            _connectionProvider = connectionProvider;

            _minPoolSize = minPoolSize;
            _maxPoolSize = maxPoolSize;
            _waitTimeoutMs = waitTimeoutMs;
            _enablePublisherConfirms = enablePublisherConfirms;

            // Semaphore acts as a concurrency gate: at most _maxPoolSize channels can exist at any time.
            // Each permit represents the right to have one channel outstanding (not in the pool).
            _poolSizeGate = new SemaphoreSlim(_maxPoolSize, _maxPoolSize);

            // Create unbounded channel for async operations
            _channelPool = Channel.CreateUnbounded<IModel>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
            _isInitialized = false;
            _disposed = false;
        }



        public async Task<IModel> GetChannelAsync(CancellationToken cancellationToken = default)
        {
            // Lazy initialization on first use
            await EnsurePoolInitialized(cancellationToken).ConfigureAwait(false);

            // Acquire a permit from the pool size gate. This guarantees we never exceed _maxPoolSize
            // outstanding channels, even under heavy concurrent access.
            if (!await _poolSizeGate.WaitAsync(_waitTimeoutMs, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"Channel pool exhausted. Maximum pool size ({_maxPoolSize}) reached.");
            }

            try
            {
                // Try to get an existing healthy channel from the pool
                while (_channelPool.Reader.TryRead(out var pooledChannel))
                {
                    if (IsChannelHealthy(pooledChannel))
                    {
                        return pooledChannel;
                    }

                    // Channel is unhealthy, dispose it (the permit we hold covers this slot)
                    DisposeChannel(pooledChannel);
                }

                // No healthy channel available in pool — create a new one
                var connection = await _connectionProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                var newChannel = connection.CreateModel();
                if (_enablePublisherConfirms) newChannel.ConfirmSelect();
                return newChannel;
            }
            catch
            {
                // On any failure after acquiring the permit, release it so others can proceed
                _poolSizeGate.Release();
                throw;
            }
        }

        public async ValueTask AddOrCloseChannelAsync(IModel channel, CancellationToken cancellationToken = default)
        {
            try
            {
                if (channel == null) return;

                try
                {
                    // Validate before returning to pool
                    if (IsChannelHealthy(channel))
                    {
                        if (!await _channelPool.Writer.WaitToWriteAsync(cancellationToken).ConfigureAwait(false) ||
                            !_channelPool.Writer.TryWrite(channel))
                        {
                            // Channel writer is completed or closed, dispose the RabbitMQ channel
                            DisposeChannel(channel);
                        }
                    }
                    else
                    {
                        // Channel is unhealthy, dispose it
                        DisposeChannel(channel);
                    }
                }
                catch
                {
                    DisposeChannel(channel);
                }
            }
            finally
            {
                // Always release the permit — the channel is no longer checked out,
                // whether it went back to the pool or was disposed.
                _poolSizeGate.Release();
            }
        }

        private async Task EnsurePoolInitialized(CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;

            await _poolLocker.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {

                if (_isInitialized) return;

                var connection = await _connectionProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < _minPoolSize; i++)
                {
                    var channel = connection.CreateModel();
                    if (_enablePublisherConfirms) channel.ConfirmSelect();
                    if (!_channelPool.Writer.TryWrite(channel))
                    {
                        // Failed to write, dispose the channel
                        DisposeChannel(channel);
                    }
                    // No permit acquired — channels in the pool are not "checked out"
                }
                _isInitialized = true;
            }
            finally
            {
                _poolLocker.Release();
            }
        }

        private bool IsChannelHealthy(IModel channel)
        {
            if (channel == null) return false;

            try
            {
                return channel.IsOpen && !channel.IsClosed;
            }
            catch
            {
                return false;
            }
        }

        private void DisposeChannel(IModel channel)
        {
            if (channel == null) return;

            try
            {
                if (channel.IsOpen)
                {
                    channel.Close();
                }
                channel.Dispose();
            }
            catch (Exception)
            {
                // Ignore errors during disposal
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    _poolLocker?.Dispose();

                    // Complete the channel to prevent new writes
                    _channelPool?.Writer?.Complete();

                    // Drain and dispose all channels in the pool
                    while (_channelPool?.Reader.TryRead(out var channel) ?? false)
                    {
                        DisposeChannel(channel);
                    }

                    _poolSizeGate?.Dispose();
                }
                catch (Exception)
                {
                    // Ignore errors during disposal
                }

                _disposed = true;
            }

            // DO NOT dispose ConnectionProvider - we don't own it
        }
    }
}
