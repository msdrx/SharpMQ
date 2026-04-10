using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpMQ.Serializer.Abstractions;

namespace SharpMQ.Abstractions
{
    public interface IConsumer<T> : IDisposable where T : class
    {
        Task SubscribeAsync(Func<T, IServiceProvider, MessageContext, Task> onDequeue,
                            Func<T, IServiceProvider, MessageContext, Exception, Task> onException,
                            RabbitSerializerOptions serializerOptions = null,
                            CancellationToken cancellationToken = default);


        Task<bool> CreateNewChannelAndStartConsume(bool rethrowError = false, CancellationToken cancellationToken = default);
        bool StartConsume(bool rethrowError = false);
        IEnumerable<string> GetConsumerTags();
        void BasicCancel();
        void CloseChannel();
    }
}