using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SharpMQ.Serializer.Abstractions;

namespace SharpMQ.Abstractions
{
    public interface IProducer : IDisposable
    {
        /// <summary>
        /// publish message to exchange
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="exchange">exchange name</param>
        /// <param name="routingKey">routing key</param>
        /// <param name="message">message</param>
        /// <param name="priority">message priority</param>
        /// <param name="expirationMs">message time to live milliseconds  must be > 100</param>
        /// <param name="serializerOptions">json serializer options</param>
        /// <param name="cancellationToken">cancellation token</param>
        Task PublishAsync<T>(string exchange,
                        string routingKey,
                        T message,
                        int? priority = null,
                        long expirationMs = 0,
                        RabbitSerializerOptions serializerOptions = null,
                        CancellationToken cancellationToken = default);

        /// <summary>
        /// publish messages to exchange
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="exchange">exchange name</param>
        /// <param name="routingKey">routing key</param>
        /// <param name="messages">messages</param>
        /// <param name="priority">messages priority</param>
        /// <param name="expirationMs">messages time to live milliseconds  must be > 100</param>
        /// <param name="serializerOptions">json serializer options</param>
        /// <param name="batchSize">batch size. default is 20</param>
        /// <param name="cancellationToken">cancellation token</param>
        Task PublishAsync<T>(string exchange,
                        string routingKey,
                        IEnumerable<T> messages,
                        int? priority = null,
                        long expirationMs = 0,
                        RabbitSerializerOptions serializerOptions = null,
                        int batchSize = 20,
                        CancellationToken cancellationToken = default);

        /// <summary>
        /// publish message to queue named <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="message">message</param>
        /// <param name="priority">message priority</param>
        /// <param name="expirationMs">message time to live milliseconds, must be > 100</param>
        /// <param name="serializerOptions">json serializer options</param>
        /// <param name="cancellationToken">cancellation token</param>
        Task PublishAsync<T>(T message,
                        int? priority = null,
                        long expirationMs = 0,
                        RabbitSerializerOptions serializerOptions = null,
                        CancellationToken cancellationToken = default);

        /// <summary>
        /// publish messages to queue named <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="messages">messages</param>
        /// <param name="priority">messages priority</param>
        /// <param name="expirationMs">messages time to live milliseconds, must be > 100</param>
        /// <param name="serializerOptions">json serializer options</param>
        /// <param name="batchSize">batch size. default is 20</param>
        /// <param name="cancellationToken">cancellation token</param>
        Task PublishAsync<T>(IEnumerable<T> messages,
                        int? priority = null,
                        long expirationMs = 0,
                        RabbitSerializerOptions serializerOptions = null,
                        int batchSize = 20,
                        CancellationToken cancellationToken = default);
    }
}
