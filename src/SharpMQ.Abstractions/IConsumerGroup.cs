using System;
using System.Collections.Generic;

namespace SharpMQ.Abstractions
{
    /// <summary>
    /// A disposable collection of consumers. Disposing the group disposes all
    /// consumers and any shared resources (e.g. a shared connection).
    /// </summary>
    public interface IConsumerGroup<T> : IReadOnlyCollection<IConsumer<T>>, IDisposable where T : class
    {
    }
}
