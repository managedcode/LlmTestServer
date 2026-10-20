using ManagedCode.LlmTestServer.OpenAI;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Client;

internal static class LlmTestServerUsageMapper
{
    public static UsageDetails Map(OpenAiUsage usage)
    {
        return new()
        {
            InputTokenCount = usage.PromptTokens,
            OutputTokenCount = usage.CompletionTokens,
            TotalTokenCount = usage.TotalTokens,
            CachedInputTokenCount = usage.PromptTokensDetails?.CachedTokens ?? usage.PromptCacheHitTokens,
            ReasoningTokenCount = usage.CompletionTokensDetails?.ReasoningTokens,
            AdditionalCounts = usage.PromptTokensDetails?.CacheWriteTokens is { } writes
                ? new() { ["InputTokenDetails.CacheWrite"] = writes }
                : null,
        };
    }
}
