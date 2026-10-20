using System.Text.Json;
using Json.Schema;

namespace ManagedCode.LlmTestServer.Scenarios;

public static class LlmTestServerJsonSchema
{
    public static void ValidateJson(string json)
    {
        using var document = JsonDocument.Parse(json);
    }

    public static bool Matches(string json, string schemaJson)
    {
        using var instance = JsonDocument.Parse(json);
        using var schema = JsonDocument.Parse(schemaJson);
        RejectExternalReferences(schema.RootElement);
        var compiled = JsonSchema.Build(schema.RootElement, new BuildOptions
        {
            Dialect = Dialect.Draft202012,
            SchemaRegistry = new(),
        });
        return compiled.Evaluate(instance.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.Flag }).IsValid;
    }

    private static void RejectExternalReferences(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                RejectExternalReferences(item);
            }
        }
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is "$ref" or "$dynamicRef" &&
                (property.Value.ValueKind != JsonValueKind.String || !property.Value.GetString()!.StartsWith('#')))
            {
                throw new JsonException("Fixture schemas must be self-contained; external references are not supported.");
            }

            RejectExternalReferences(property.Value);
        }
    }
}
