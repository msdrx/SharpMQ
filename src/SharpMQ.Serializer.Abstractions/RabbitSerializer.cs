namespace SharpMQ.Serializer.Abstractions
{
    public abstract class RabbitSerializer
    {
        public abstract string Serialize<T>(T obj, RabbitSerializerOptions options = null);
        public abstract T DeSerialize<T>(string json, RabbitSerializerOptions options = null);
    }
}
