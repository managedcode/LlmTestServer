using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Scenarios;

public enum LlmTestServerStreamDeliveryMode
{
    EarlyCleanEnd = 1,
    DelayThenContinue = 2,
    AbortConnection = 3,
}

public enum LlmTestServerStreamDeliverySurface
{
    OpenAiChatCompletions = 1,
    OpenAiResponses = 2,
    AnthropicMessages = 3,
    GeminiGenerateContent = 4,
}

/// <summary>Bounded delivery behavior applied after an actual text content frame is flushed.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerStreamDelivery
{
    public LlmTestServerStreamDeliveryMode Mode { get; init; }
    public int AfterContentFrames { get; init; } = 1;
    public int DelayMilliseconds { get; init; }

    internal static LlmTestServerStreamDelivery? Snapshot(LlmTestServerScenarioResponse response)
    {
        if (response.StreamDelivery is not { } plan) { return null; }
        if (!Enum.IsDefined(plan.Mode) || plan.AfterContentFrames is < 1 or > 128
            || plan.AfterContentFrames > (response.StreamChunks.Count > 0
                ? response.StreamChunks.Count(chunk => !string.IsNullOrEmpty(chunk)) : response.Content.Length > 0 ? 1 : 0)
            || (plan.Mode == LlmTestServerStreamDeliveryMode.DelayThenContinue
                ? plan.DelayMilliseconds is < 1 or > 60000 : plan.DelayMilliseconds != 0))
        { throw new ArgumentException("Invalid bounded stream delivery plan."); }
        if (response.Error is not null || response.ToolCalls.Count > 0 || response.OutputItems.Count > 0
            || response.StreamChunks.Count > 0 && string.Concat(response.StreamChunks) != response.Content)
        { throw new ArgumentException("Stream delivery requires a text-only response with matching content chunks."); }
        return plan with { };
    }
}
