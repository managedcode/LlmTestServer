using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Scenarios;

[JsonConverter(typeof(JsonStringEnumConverter<LlmTestServerOutputDispositionKind>))]
public enum LlmTestServerOutputDispositionKind
{
    Filtered = 1,
    Refused = 2,
}

public enum LlmTestServerOutputDispositionSurface
{
    OpenAiChatCompletions = 1,
    OpenAiResponses = 2,
    AnthropicMessages = 3,
    GeminiGenerateContent = 4,
    DeepSeekChatCompletions = 5,
    AzureOpenAiDeploymentChatCompletions = 6,
    MicrosoftFoundryChatCompletions = 7,
    MicrosoftFoundryModelsChatCompletions = 8,
    AzureOpenAiV1ChatCompletions = 9,
    AzureOpenAiV1Responses = 10,
    MicrosoftFoundryV1ChatCompletions = 11,
    MicrosoftFoundryV1Responses = 12,
}

/// <summary>A provider policy outcome; fixture Content supplies generated text or refusal text for its native mapping.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerOutputDisposition
{
    public LlmTestServerOutputDispositionKind Kind { get; init; }
    public string? Category { get; init; }
    public string? Explanation { get; init; }

    internal static void Validate(LlmTestServerOutputDisposition disposition)
    {
        if (!Enum.IsDefined(disposition.Kind) || disposition.Category?.Length > 256 || disposition.Explanation?.Length > 4096)
        { throw new ArgumentException("Invalid or oversized output disposition metadata.", nameof(disposition)); }
    }

    /// <summary>Only native input semantics belong in history; outcome diagnostics remain on the result.</summary>
    internal static LlmTestServerOutputDisposition? ForHistory(LlmTestServerOutputDisposition? disposition)
    {
        if (disposition is null) { return null; }
        Validate(disposition);
        return disposition.Kind == LlmTestServerOutputDispositionKind.Refused
            ? new() { Kind = LlmTestServerOutputDispositionKind.Refused } : null;
    }

    internal static LlmTestServerOutputDisposition? Snapshot(LlmTestServerScenarioResponse response)
    {
        if (response.OutputDisposition is not { } disposition) { return null; }
        Validate(disposition);
        if (response.Error is not null || response.StreamDelivery is not null || response.ToolCalls.Count > 0 || response.OutputItems.Count > 0
            || response.StreamChunks.Count > 0 && string.Concat(response.StreamChunks) != response.Content)
        { throw new ArgumentException("Output disposition requires a plain text response with matching chunks and no error, tools, native items or stream delivery plan.", nameof(response)); }
        return disposition with { };
    }
}
