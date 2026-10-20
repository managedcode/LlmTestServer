using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.ProviderValidation;

// Linked into provider assemblies: JSON primitives only, no provider-specific field names.
internal static class LlmTestServerJsonValidation
{
    public static LlmTestServerRequestValidationResult Check(JsonElement body, Action<JsonElement> validate)
    {
        try
        {
            Object(body);
            validate(body);
            return new();
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return new("Malformed or unsupported provider request fields.");
        }
    }

    public static IEnumerable<JsonElement> OptionalArray(JsonElement body, string name, bool allowNull = false)
    {
        if (!body.TryGetProperty(name, out var value) || allowNull && value.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        Array(value);
        return value.EnumerateArray();
    }

    public static void OptionalSchema(JsonElement body, string name)
    {
        if (body.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
        {
            Schema(value);
        }
    }

    public static void Schema(JsonElement value)
    {
        if (value.ValueKind is not (JsonValueKind.Object or JsonValueKind.True or JsonValueKind.False))
        {
            throw new JsonException("A schema must be an object or boolean.");
        }
    }

    public static void String(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String) { throw new JsonException("A string is required."); }
    }
    public static void NonEmpty(JsonElement value)
    {
        String(value);
        if (string.IsNullOrWhiteSpace(value.GetString())) { throw new JsonException("A non-empty string is required."); }
    }
    public static void Name(JsonElement value) { Object(value); NonEmpty(value.GetProperty("name")); }
    public static void Object(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) { throw new JsonException("An object is required."); }
    }
    public static void Array(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array) { throw new JsonException("An array is required."); }
    }
    public static void Choice(JsonElement value, params string[] choices)
    {
        String(value);
        if (!choices.Contains(value.GetString(), StringComparer.Ordinal)) { throw new JsonException("Unsupported option value."); }
    }
}
