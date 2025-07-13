using System;
using System.Collections.Concurrent;
using SharpMQ.Abstractions;
using SharpMQ.Exceptions;

namespace SharpMQ.Producers
{
    internal class ProducerFactory : IProducerFactory
    {
        private readonly ConcurrentDictionary<string, IProducer> _producers = new ConcurrentDictionary<string, IProducer>();

        public IProducer Get(string key)
        {
            bool found = _producers.TryGetValue(key, out IProducer producer);
            if (!found) throw new ProducerException($"producer not found by key={key}");
            if (producer == null) throw new ProducerException($"producer is null for key={key}");

            return producer;
        }

        public void Add(string key, IProducer producer)
        {
            bool added = _producers.TryAdd(key, producer);
            if (!added) throw new ProducerException($"can't add producer by key={key} or it already exists");
        }


        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_producers != null)
            {
                foreach (var producer in _producers)
                {
                    try
                    {
                        producer.Value?.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        //if already disposed its ok
                    }
                }
            }
        }
    }
}