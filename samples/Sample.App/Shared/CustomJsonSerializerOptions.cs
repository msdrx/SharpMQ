using System.Text.Json;
using SharpMQ.Serializer.Abstractions;

namespace Sample.App.Shared;

public class CustomJsonSerializerOptions : RabbitSerializerOptions
{
    public JsonSerializerOptions Settings { get; set; }

    public CustomJsonSerializerOptions(JsonSerializerOptions settings)
    {
        Settings = settings;
    }
}
