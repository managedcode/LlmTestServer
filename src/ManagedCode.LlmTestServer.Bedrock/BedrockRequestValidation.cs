using System.Text.Json;
using ManagedCode.LlmTestServer.Providers;
using static ManagedCode.LlmTestServer.ProviderValidation.LlmTestServerJsonValidation;

namespace ManagedCode.LlmTestServer.Bedrock;

public static class BedrockRequestValidation
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
        if (body.TryGetProperty("toolConfig", out var config) && config.ValueKind != JsonValueKind.Null)
        {
            Array(config.GetProperty("tools"));
            foreach (var tool in config.GetProperty("tools").EnumerateArray())
            {
                var spec = tool.GetProperty("toolSpec"); Name(spec); Schema(spec.GetProperty("inputSchema").GetProperty("json"));
            }
        }
        if (body.TryGetProperty("outputConfig", out config) && config.ValueKind != JsonValueKind.Null)
        {
            var format = config.GetProperty("textFormat"); Choice(format.GetProperty("type"), "json_schema");
            NonEmpty(format.GetProperty("structure").GetProperty("jsonSchema").GetProperty("schema"));
        }
        foreach (var message in OptionalArray(body, "messages"))
        {
            foreach (var part in OptionalArray(message, "content"))
            {
                if (part.TryGetProperty("toolUse", out var call) && call.ValueKind != JsonValueKind.Null)
                { Name(call); NonEmpty(call.GetProperty("toolUseId")); Object(call.GetProperty("input")); }
                if (part.TryGetProperty("toolResult", out var result) && result.ValueKind != JsonValueKind.Null)
                {
                    NonEmpty(result.GetProperty("toolUseId")); Array(result.GetProperty("content"));
                    foreach (var item in result.GetProperty("content").EnumerateArray()) { Object(item); }
                }
            }
        }
    }
}
