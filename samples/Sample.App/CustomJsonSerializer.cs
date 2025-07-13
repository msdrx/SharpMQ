using System.Text.Json;
using SharpMQ.Serializer.Abstractions;

namespace Sample.App;
public class CustomJsonSerializer : RabbitSerializer
{
    public override T DeSerialize<T>(string json, RabbitSerializerOptions? options = null)
    {
        return JsonSerializer.Deserialize<T>(json,GetConcreteSerializer(options))!;
    }

    public override string Serialize<T>(T obj, RabbitSerializerOptions? options = null)
    {
        return JsonSerializer.Serialize(obj,GetConcreteSerializer(options));
    }

    private JsonSerializerOptions? GetConcreteSerializer(RabbitSerializerOptions? options)
    {
        JsonSerializerOptions? settings = null;
        if (options != null && options is CustomJsonSerializerOptions obj)
        {
            settings = obj.Settings;
        }
        return settings;
    }
}

public class CustomJsonSerializerOptions : RabbitSerializerOptions
{
    public JsonSerializerOptions Settings { get; set; }
    public CustomJsonSerializerOptions(JsonSerializerOptions settings)
    {
        Settings = settings;
    }
}
