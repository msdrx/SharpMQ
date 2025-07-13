using System;

namespace SharpMQ.Abstractions
{
    public interface IProducerFactory : IDisposable
    {
        IProducer Get(string key);
        void Add(string key, IProducer producer);
    }
}