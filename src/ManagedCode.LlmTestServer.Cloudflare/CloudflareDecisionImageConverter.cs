using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Cloudflare;

public sealed class CloudflareDecisionImageConverter : JsonConverter<CloudflareDecisionImage>
{
    public override CloudflareDecisionImage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => new() { DataUrl = reader.GetString()! },
            JsonTokenType.StartObject => new() { Embedded = JsonSerializer.Deserialize<CloudflareBase64Image>(ref reader, options)! },
            _ => throw new JsonException("Clef images must be data URLs or embedded base64 image objects."),
        };
    }

    public override void Write(Utf8JsonWriter writer, CloudflareDecisionImage value, JsonSerializerOptions options)
    {
        if (value.DataUrl is { } dataUrl && value.Embedded is null) { writer.WriteStringValue(dataUrl); }
        else if (value.Embedded is { } embedded && value.DataUrl is null) { JsonSerializer.Serialize(writer, embedded, options); }
        else { throw new JsonException("A Clef image must contain exactly one native representation."); }
    }
}
