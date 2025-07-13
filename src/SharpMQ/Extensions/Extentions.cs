using System;
using System.Collections.Generic;
using System.Text;
using RabbitMQ.Client.Events;
using SharpMQ.Abstractions;
using SharpMQ.Serializer.Abstractions;
using SharpMQ.Exceptions;
using SharpMQ.Configs;

namespace SharpMQ.Extensions
{
    internal static class Extentions
    {
        public static Dictionary<string, object> GetQueueArgs(this QueueArgConfig[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            var result = new Dictionary<string, object>();
            foreach (var arg in args)
            {
                if (string.Equals(arg.Key, ConfigConstants.QueueArgKeys.MaxPriority, StringComparison.InvariantCultureIgnoreCase))
                {
                    result.Add(arg.Key.ToLower(), Convert.ToInt32(arg.Value));
                }
                else if (string.Equals(arg.Key, ConfigConstants.QueueArgKeys.SingleActiveConsumer, StringComparison.InvariantCultureIgnoreCase))
                {
                    result.Add(arg.Key.ToLower(), Convert.ToBoolean(arg.Value));
                }
                else
                {
                    result.Add(arg.Key.ToLower(), arg.Value);
                }
            }

            return result;
        }

        public static byte[] ToByteArray<T>(this T obj, RabbitSerializer serializer, RabbitSerializerOptions serializerOptions = null)
        {
            return Encoding.UTF8.GetBytes(serializer.Serialize<T>(obj, serializerOptions));
        }

        public static ReadOnlyMemory<byte> ToReadOnlyMemory<T>(this T obj, RabbitSerializer serializer, RabbitSerializerOptions serializerOptions = null)
        {
            return Encoding.UTF8.GetBytes(serializer.Serialize<T>(obj, serializerOptions));
        }

        public static T ToObject<T>(this ReadOnlySpan<byte> bytes, RabbitSerializer serializer, RabbitSerializerOptions serializerOptions = null)
        {
            var msgStr = Encoding.UTF8.GetString(bytes.ToArray());
            var msg = serializer.DeSerialize<T>(msgStr, serializerOptions);
            if (msg == null) throw new ConsumerException($"after deserialize message is null msgStr={msgStr}");

            return msg;
        }

        public static string AsDLQ(this string queueName)
        {
            return queueName + ".DLQ";
        }

        public static string AsDLExchange(this string queueName)
        {
            return queueName + ".direct.DL";
        }

        public static string AsDirectExchange(this string queueName)
        {
            return queueName + ".direct";
        }
        public static string AsRetryQ(this string queueName)
        {
            return queueName + ".RetryQ";
        }

        public static string AsRetryExchange(this string queueName)
        {
            return queueName + ".direct.Retry";
        }

        internal static MessageBasicDeliverEventArgs ToMessageBasicDeliverEventArgs(this BasicDeliverEventArgs args)
        {
            return new MessageBasicDeliverEventArgs()
            {
                Instance = args,
                ConsumerTag = args.ConsumerTag,
                DeliveryTag = args.DeliveryTag,
                Exchange = args.Exchange,
                Redelivered = args.Redelivered,
                RoutingKey = args.RoutingKey
            };
        }
    }
}