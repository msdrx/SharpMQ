using System;
using RabbitMQ.Client;

namespace SharpMQ.Connections
{
    internal interface IConnectionManager : IDisposable
    {
        bool IsDispatchConsumersAsyncEnabled { get; }
        IConnection GetOrCreateConnection();
    }
}