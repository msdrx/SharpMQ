using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SharpMQ.Abstractions;
using SharpMQ.Exceptions;

namespace SharpMQ.Producers
{
    internal class ProducerFactory : IProducerFactory
    {
        private readonly ConcurrentDictionary<string, IProducer> _producers = new ConcurrentDictionary<string, IProducer>();
        private readonly List<IDisposable> _ownedDisposables = new List<IDisposable>();
        private bool _disposed;

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

        /// <summary>
        /// Tracks an <see cref="IDisposable"/> resource that this factory owns and will dispose
        /// when the factory itself is disposed.
        /// </summary>
        public void TrackDisposable(IDisposable disposable)
        {
            if (disposable == null) return;
            _ownedDisposables.Add(disposable);
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

                foreach (var disposable in _ownedDisposables)
                {
                    try
                    {
                        disposable?.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        //if already disposed its ok
                    }
                }

                _ownedDisposables.Clear();
                _disposed = true;
            }
        }
    }
}