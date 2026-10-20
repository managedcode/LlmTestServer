using System.Buffers.Text;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.OpenAI;

public static class OpenAiDecisionInputValidation
{
    public const int MaxImages = 128;
    public const int MaxTextLength = 10_485_760;
    public const int MaxImageUrlLength = 1_073_741_824;
    private const string _base64Marker = ";base64,";

    public static bool IsValid(JsonElement input)
    {
        if (input.ValueKind == JsonValueKind.String) { return true; }
        if (input.ValueKind != JsonValueKind.Array) { return false; }
        var imageCount = 0;
        return input.EnumerateArray().All(message => ValidMessage(message, ref imageCount)) && imageCount <= MaxImages;
    }

    private static bool ValidMessage(JsonElement message, ref int imageCount)
    {
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("role", out var role)
            || role.ValueKind != JsonValueKind.String || role.GetString() != "user"
            || (message.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || type.GetString() != "message"))
            || !message.TryGetProperty("content", out var content)) { return false; }
        if (content.ValueKind == JsonValueKind.String) { return true; }
        if (content.ValueKind != JsonValueKind.Array) { return false; }
        foreach (var part in content.EnumerateArray())
        {
            if (!ValidPart(part, ref imageCount)) { return false; }
        }
        return true;
    }

    private static bool ValidPart(JsonElement part, ref int imageCount)
    {
        if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) { return false; }
        if (type.GetString() == "input_text") { return part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String && text.GetString()!.Length <= MaxTextLength; }
        if (type.GetString() != "input_image" || !part.TryGetProperty("image_url", out var imageUrl)
            || imageUrl.ValueKind != JsonValueKind.String || !ValidDataUrl(imageUrl.GetString()!)) { return false; }
        if (part.TryGetProperty("detail", out var detail) && detail.ValueKind != JsonValueKind.Null
            && (detail.ValueKind != JsonValueKind.String || detail.GetString() is not ("low" or "high" or "auto" or "original"))) { return false; }
        imageCount++;
        return true;
    }

    private static bool ValidDataUrl(string value)
    {
        var marker = value.IndexOf(_base64Marker, StringComparison.Ordinal);
        return value.Length <= MaxImageUrlLength && value.StartsWith("data:", StringComparison.Ordinal) && marker >= "data:".Length
            && Base64.IsValid(value.AsSpan(marker + _base64Marker.Length));
    }
}
