using System.Text.Json;
using ManagedCode.LlmTestServer.Anthropic;
using ManagedCode.LlmTestServer.Bedrock;
using ManagedCode.LlmTestServer.Cohere;
using ManagedCode.LlmTestServer.Gemini;
using ManagedCode.LlmTestServer.Ollama;
using ManagedCode.LlmTestServer.OpenAI;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

internal static class LlmTestServerRequestPolicy
{
    public static IResult? ValidateApiVersion(HttpContext context)
    {
        var path = context.Request.Path;
        if (path.StartsWithSegments("/azure-openai/openai/deployments"))
        {
            var versions = context.Request.Query["api-version"];
            return versions.Count == 1 && IsSupportedAzureApiVersion(versions[0])
                ? null
                : Results.Json(OpenAiWireMapper.ToError("invalid_api_version",
                    "This route requires one supported Azure OpenAI api-version: 2024-10-21 or 2025-04-01-preview."), statusCode: 400);
        }

        if (path == "/azure-openai/openai/v1/audio/transcriptions"
            || path == "/microsoft-foundry/openai/v1/audio/transcriptions")
        {
            var versions = context.Request.Query["api-version"];
            return versions.Count == 0 || versions.Count == 1 && versions[0] is "v1" or "preview"
                ? null
                : Results.Json(OpenAiWireMapper.ToError("invalid_api_version",
                    "This route supports the default v1 or one explicit v1/preview api-version."), statusCode: 400);
        }

        var expected = path.StartsWithSegments("/azure-openai/openai/v1/video") ? "preview"
            : path == "/microsoft-foundry/chat/completions" || path == "/microsoft-foundry/embeddings"
                || path == "/microsoft-foundry/models/chat/completions" || path == "/microsoft-foundry/models/embeddings" ? "2024-05-01-preview" : null;
        return expected is null || context.Request.Query["api-version"] == expected
            ? null
            : Results.Json(OpenAiWireMapper.ToError("invalid_api_version", $"This route requires api-version={expected}."), statusCode: 400);
    }

    // Current GA and preview deployment contracts documented by Azure. Client defaults
    // remain SDK-owned; unknown date-shaped versions are not valid service versions.
    private static bool IsSupportedAzureApiVersion(string? version)
    {
        return version is "2024-10-21" or "2025-04-01-preview";
    }

    public static bool IsChatRequest<T>()
    {
        return typeof(T) == typeof(OpenAiChatCompletionRequest)
        || typeof(T) == typeof(OpenAiResponseRequest) || typeof(T) == typeof(AnthropicMessagesRequest)
        || typeof(T) == typeof(GeminiGenerateContentRequest) || typeof(T) == typeof(OllamaChatRequest)
        || typeof(T) == typeof(CohereChatRequest) || typeof(T) == typeof(BedrockConverseRequest)
        || typeof(T) == typeof(JsonElement);
    }

    public static bool HasUnsupportedChatFeatures(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in body.EnumerateObject())
        {
            var value = property.Value;
            if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            {
                continue;
            }

            switch (property.Name)
            {
                case "tools":
                case "functions":
                    if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 0)
                    {
                        return true;
                    }
                    break;
                case "tool_choice":
                case "function_call":
                    if (value.ValueKind != JsonValueKind.String || value.GetString() is not ("none" or "auto"))
                    {
                        return true;
                    }
                    break;
                case "toolConfig":
                case "tool_config":
                case "output_config":
                case "outputConfig":
                    return true;
                case "response_format":
                case "format":
                    if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.String || type.GetString() != "text")
                    {
                        return true;
                    }
                    break;
                case "responseMimeType":
                    if (value.ValueKind != JsonValueKind.String || value.GetString() != "text/plain")
                    {
                        return true;
                    }
                    break;
                case "responseSchema":
                case "responseJsonSchema":
                    return true;
                case "text":
                case "generationConfig":
                    if (HasUnsupportedChatFeatures(value))
                    {
                        return true;
                    }
                    break;
            }
        }

        return false;
    }
}
