using System.Text;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.SystemOne;

/// <summary>Validation for the claimed TypeSafe JSON API surface; no inference or token estimation.</summary>
public static class TypeSafeRequestValidation
{
    public const string ImagesHeader = "TypeSafe-Danger-Zone-Images";
    public const string LongContextHeader = "TypeSafe-Danger-Zone-Long-Context";
    public const string Enabled = "true";
    public const int MaxRequestBytes = 32 * 1024 * 1024;
    public const string PreviewModel = "jev-preview";
    public const string Jev114Model = "jev-1.14.0";
    private const int _maxReferenceBytes = 1024;
    private const int _maxReferencesBytes = 65536;

    public static TypeSafeValidationResult Validate(JsonElement body, bool imagesEnabled, bool multipart = false)
    {
        if (!multipart && Encoding.UTF8.GetByteCount(body.GetRawText()) > MaxRequestBytes)
        { return new("TypeSafe request body exceeds 32 MiB.", 413); }
        var shape = SystemOneDecisionMapper.Validate(body, Decisions.LlmTestServerDecisionProvider.TypeSafe);
        if (!shape.IsValid) { return new(shape.Error!, 422); }
        var questions = body.GetProperty("questions");
        var box = questions.EnumerateObject().Any(entry => entry.Value.GetProperty("type").GetString() == SystemOneDecisionTypes.Box);
        var hasAssets = body.TryGetProperty("assets", out var assets);
        if (hasAssets && assets.ValueKind != JsonValueKind.Object) { return new("assets must be an object."); }
        var entries = hasAssets ? assets.EnumerateObject().ToArray() : [];
        if ((entries.Length > 0 || box) && !imagesEnabled) { return new("Images and box questions require the images header."); }
        if (box && entries.Length == 0) { return new("Box questions require an image."); }
        if (box && body.GetProperty("model").GetString() is not (PreviewModel or Jev114Model))
        { return new("Box questions require jev-preview or jev-1.14.0 in this compatibility profile."); }
        if (entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        { return new("Image references must be unique."); }
        var referenceBytes = entries.Sum(entry => (long)Encoding.UTF8.GetByteCount(entry.Name));
        if (referenceBytes > _maxReferencesBytes) { return new("Image references exceed the total byte limit.", 413); }
        long totalBytes = 0;
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name)) { return new("Image references cannot be empty."); }
            if (Encoding.UTF8.GetByteCount(entry.Name) > _maxReferenceBytes) { return new("Image reference exceeds 1024 bytes.", 413); }
            if (!ContainsReference(body.GetProperty("state"), entry.Name) && !questions.EnumerateObject().Any(question =>
                question.Value.TryGetProperty("instructions", out var instructions) && ContainsReference(instructions, entry.Name)
                || question.Value.TryGetProperty("criteria", out var criteria) && ContainsReference(criteria, entry.Name)))
            { return new("Every image asset must be referenced by a content value."); }
            var result = TypeSafeImageValidation.Validate(entry.Value, out var bytes);
            if (result.Error is not null) { return result; }
            totalBytes += bytes;
            if (totalBytes > MaxRequestBytes) { return new("Decoded images exceed 32 MiB.", 413); }
        }
        return new();
    }

    private static bool ContainsReference(JsonElement value, string reference)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()!.Contains(reference, StringComparison.Ordinal),
            JsonValueKind.Array => value.EnumerateArray().Any(item => ContainsReference(item, reference)),
            JsonValueKind.Object => value.EnumerateObject().Any(item => ContainsReference(item.Value, reference)),
            _ => false,
        };
    }
}

public sealed record TypeSafeValidationResult(string? Error = null, int StatusCode = 400);
