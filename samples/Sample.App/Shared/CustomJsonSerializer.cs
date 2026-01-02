using System.Text.Json;
using SharpMQ.Serializer.Abstractions;

namespace Sample.App.Shared;

public class CustomJsonSerializer : RabbitSerializer
{
    public override T DeSerialize<T>(string json, RabbitSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json, GetConcreteSerializer(options))!;
    }

    public override string Serialize<T>(T obj, RabbitSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(obj, GetConcreteSerializer(options));
    }

    private JsonSerializerOptions? GetConcreteSerializer(RabbitSerializerOptions? options)
    {
        if (options is CustomJsonSerializerOptions customOptions)
        {
            return customOptions.Settings;
        }
        return null;
    }
}
