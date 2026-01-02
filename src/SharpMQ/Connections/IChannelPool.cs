using System;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    internal interface IChannelPool : IDisposable
    {
        Task<IModel> GetChannelAsync(CancellationToken cancellationToken = default);
        ValueTask AddOrCloseChannelAsync(IModel channel);
    }
}