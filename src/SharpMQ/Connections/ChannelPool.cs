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

        private readonly SemaphoreSlim _poolLocker = new SemaphoreSlim(1, 1);
        private readonly Channel<IModel> _channelPool;
        private int _currentPoolSize;
        private bool _isInitialized;
        private bool _disposed;


        public ChannelPool(IConnectionProvider connectionProvider,
            int minPoolSize,
            int maxPoolSize,
            int waitTimeoutMs
            )
        {
            _connectionProvider = connectionProvider;

            _minPoolSize = minPoolSize;
            _maxPoolSize = maxPoolSize;
            _waitTimeoutMs = waitTimeoutMs;

            // Create unbounded channel for async operations
            _channelPool = Channel.CreateUnbounded<IModel>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = false
            });
            _currentPoolSize = 0;
            _isInitialized = false;
            _disposed = false;
        }



        public async Task<IModel> GetChannelAsync(CancellationToken cancellationToken = default)
        {
            // Lazy initialization on first use
            await EnsurePoolInitialized(cancellationToken);

            try
            {
                // Try to get channel from pool with timeout
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(_waitTimeoutMs);

                    if (await _channelPool.Reader.WaitToReadAsync(cts.Token).ConfigureAwait(false))
                    {
                        if (_channelPool.Reader.TryRead(out var channel))
                        {
                            // Validate channel before returning
                            if (IsChannelHealthy(channel))
                            {
                                Interlocked.Decrement(ref _currentPoolSize);
                                return channel;
                            }

                            // Channel is unhealthy, dispose it and create new one
                            DisposeChannel(channel);
                            Interlocked.Decrement(ref _currentPoolSize);
                        }
                    }
                }

                // Pool exhausted or timeout - check if we can create new channel
                int currentSize = Interlocked.Increment(ref _currentPoolSize);
                if (currentSize <= _maxPoolSize)
                {
                    var connection = await _connectionProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                    return connection.CreateModel();
                }
                else
                {
                    // Exceeded max pool size, decrement and throw
                    Interlocked.Decrement(ref _currentPoolSize);
                    throw new InvalidOperationException($"Channel pool exhausted. Maximum pool size ({_maxPoolSize}) reached.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // On any error, try to create a new channel if under limit
                int currentSize = Interlocked.Increment(ref _currentPoolSize);
                if (currentSize <= _maxPoolSize)
                {
                    var connection = await _connectionProvider.GetOrCreateAsync(cancellationToken).ConfigureAwait(false);
                    return connection.CreateModel();
                }
                else
                {
                    Interlocked.Decrement(ref _currentPoolSize);
                    throw;
                }
            }
        }

        public async ValueTask AddOrCloseChannelAsync(IModel channel)
        {
            if (channel == null)
            {
                Interlocked.Decrement(ref _currentPoolSize);
                return;
            }

            // Validate before returning to pool
            if (IsChannelHealthy(channel) && _currentPoolSize <= _maxPoolSize)
            {
                if (!await _channelPool.Writer.WaitToWriteAsync().ConfigureAwait(false) ||
                    !_channelPool.Writer.TryWrite(channel))
                {
                    // Channel is disposed or closed, dispose the RabbitMQ channel
                    DisposeChannel(channel);
                    Interlocked.Decrement(ref _currentPoolSize);
                }
            }
            else
            {
                // Channel is unhealthy or pool is full, dispose it
                DisposeChannel(channel);
                Interlocked.Decrement(ref _currentPoolSize);
            }
        }

        private async Task EnsurePoolInitialized(CancellationToken cancellationToken = default)
        {
            if (_isInitialized) return;

            await _poolLocker.WaitAsync(cancellationToken);

            try
            {

                if (_isInitialized) return;

                var connection = await _connectionProvider.GetOrCreateAsync();
                for (int i = 0; i < _minPoolSize; i++)
                {
                    var channel = connection.CreateModel();
                    if (_channelPool.Writer.TryWrite(channel))
                    {
                        Interlocked.Increment(ref _currentPoolSize);
                    }
                    else
                    {
                        // Failed to write, dispose the channel
                        DisposeChannel(channel);
                    }
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
