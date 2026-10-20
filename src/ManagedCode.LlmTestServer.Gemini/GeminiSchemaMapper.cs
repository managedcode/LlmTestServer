using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ManagedCode.LlmTestServer.Gemini;

/// <summary>Adapts Gemini's OpenAPI Schema fields to the runtime JSON Schema evaluator.</summary>
public static class GeminiSchemaMapper
{
    public static string? ToJsonSchema(JsonElement? schema)
    {
        return schema is null ? null : Normalize(JsonNode.Parse(schema.Value.GetRawText())).ToJsonString();
    }

    private static JsonObject Normalize(JsonNode? node)
    {
        if (node is not JsonObject schema)
        {
            throw new JsonException("Gemini Schema must be an object.");
        }

        if (schema["type"] is JsonValue type)
        {
            schema["type"] = type.GetValue<string>().ToLowerInvariant();
        }

        foreach (var name in new[] { "minItems", "maxItems", "minProperties", "maxProperties", "minLength", "maxLength" })
        {
            if (schema[name] is JsonValue limit && limit.TryGetValue<string>(out var text))
            {
                if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
                {
                    throw new JsonException($"{name} must be a nonnegative int64.");
                }

                schema[name] = value;
            }
        }

        // Traverse schema positions only: example/default/enum contain user data.
        if (schema["properties"] is JsonObject properties)
        {
            foreach (var property in properties.ToArray())
            {
                properties.Remove(property.Key);
                properties[property.Key] = Normalize(property.Value);
            }
        }

        if (schema["items"] is { } items)
        {
            schema.Remove("items");
            schema["items"] = Normalize(items);
        }

        if (schema["anyOf"] is JsonArray alternatives)
        {
            for (var index = 0; index < alternatives.Count; index++)
            {
                var alternative = alternatives[index];
                alternatives[index] = null;
                alternatives[index] = Normalize(alternative);
            }
        }

        var nullable = schema["nullable"]?.GetValue<bool>() == true;
        schema.Remove("nullable");
        return nullable
            ? new JsonObject { ["anyOf"] = new JsonArray(schema, new JsonObject { ["type"] = "null" }) }
            : schema;
    }
}
