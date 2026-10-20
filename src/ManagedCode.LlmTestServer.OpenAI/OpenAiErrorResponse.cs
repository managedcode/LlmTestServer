using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiErrorResponse
{
    [JsonPropertyName("error")]
    public OpenAiError Error { get; init; } = new();
}

public sealed record OpenAiError
{
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = "invalid_request_error";

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}
