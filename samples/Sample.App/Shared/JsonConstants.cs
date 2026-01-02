using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sample.App.Shared;

public static class JsonConstants
{
    public static readonly JsonSerializerOptions ConsumerDefault = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static readonly JsonSerializerOptions PublisherDefault = new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };
}
