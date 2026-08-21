using System.Text.Json;
using System.Text.Json.Serialization;

namespace TimecodeBridge.Models;

public class ProjectData
{
    public List<Cue> Cues { get; set; } = [];
    public List<OscHost> Hosts { get; set; } = [];
    public RelaySettings RelaySettings { get; set; } = new();
    public TimecodeOffset Offset { get; set; }
    public TimecodeSourceSettings SourceSettings { get; set; } = new();
    public OscTriggerPanelSettings OscTriggerPanel { get; set; } = new();
    public CueSyncSettings CueSync { get; set; } = new();

    public static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.Converters.Add(new OscArgumentJsonConverter());
        return options;
    }
}

public class OscArgumentJsonConverter : JsonConverter<OscArgument>
{
    public override OscArgument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        var typeStr = root.GetProperty("type").GetString();
        return typeStr switch
        {
            "int32" => new OscInt32Argument(root.GetProperty("value").GetInt32()),
            "float32" => new OscFloat32Argument(root.GetProperty("value").GetSingle()),
            "string" => new OscStringArgument(root.GetProperty("value").GetString()!),
            _ => throw new JsonException($"Unknown OscArgument type: {typeStr}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, OscArgument value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        switch (value)
        {
            case OscInt32Argument intArg:
                writer.WriteString("type", "int32");
                writer.WriteNumber("value", intArg.Value);
                break;
            case OscFloat32Argument floatArg:
                writer.WriteString("type", "float32");
                writer.WriteNumber("value", floatArg.Value);
                break;
            case OscStringArgument strArg:
                writer.WriteString("type", "string");
                writer.WriteString("value", strArg.Value);
                break;
        }
        writer.WriteEndObject();
    }
}
