using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotOnlyFiendsStudio.Studio;

public static class JsonOptions
{
    private static JsonSerializerOptions? _instance;

    public static JsonSerializerOptions Default => _instance ??= Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
            }
        };
        return options;
    }
}
