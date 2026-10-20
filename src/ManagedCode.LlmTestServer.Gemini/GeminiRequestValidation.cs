using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.Gemini;

public static class GeminiRequestValidation
{
    public static LlmTestServerRequestValidationResult Validate(JsonElement body)
    {
        return Check(body, ValidateBody);
    }

    private static void ValidateBody(JsonElement body)
    {
        if (body.TryGetProperty("functions", out _) || body.TryGetProperty("function_call", out _))
        {
            throw new JsonException("Legacy function fields are unsupported.");
        }
        foreach (var tool in OptionalArray(body, "tools"))
        {
            Array(tool.GetProperty("functionDeclarations"));
            foreach (var declaration in tool.GetProperty("functionDeclarations").EnumerateArray())
            {
                Name(declaration); OptionalSchema(declaration, "parametersJsonSchema");
                if (declaration.TryGetProperty("parameters", out var parameters)) { _ = GeminiSchemaMapper.ToJsonSchema(parameters); }
            }
        }
        if (body.TryGetProperty("generationConfig", out var config) && config.ValueKind != JsonValueKind.Null)
        {
            OptionalSchema(config, "responseJsonSchema");
            if (config.TryGetProperty("responseSchema", out var schema)) { _ = GeminiSchemaMapper.ToJsonSchema(schema); }
        }
        if (body.TryGetProperty("toolConfig", out config) && config.ValueKind != JsonValueKind.Null)
        {
            var calling = config.GetProperty("functionCallingConfig");
            if (calling.TryGetProperty("mode", out var mode)) { Choice(mode, "AUTO", "ANY", "NONE", "VALIDATED"); }
            foreach (var name in OptionalArray(calling, "allowedFunctionNames")) { NonEmpty(name); }
        }
        foreach (var message in OptionalArray(body, "contents"))
        {
            foreach (var part in OptionalArray(message, "parts"))
            {
                if (part.TryGetProperty("functionCall", out var call) && call.ValueKind != JsonValueKind.Null) { Name(call); Object(call.GetProperty("args")); }
                if (part.TryGetProperty("functionResponse", out var result) && result.ValueKind != JsonValueKind.Null) { Name(result); Object(result.GetProperty("response")); }
            }
        }
    }
}
