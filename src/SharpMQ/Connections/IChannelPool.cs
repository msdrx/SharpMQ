using System;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    internal interface IChannelPool : IDisposable
    {
        IModel GetChannel();
        void AddOrCloseChannel(IModel channel);
    }
}