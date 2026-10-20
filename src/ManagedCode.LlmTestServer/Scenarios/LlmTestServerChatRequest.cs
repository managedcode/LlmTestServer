using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerChatRequest
{
    /// <summary>
    /// Optional caller-provided identifier copied to runtime trace events for stable correlation.
    /// </summary>
    public string? RequestId { get; init; }

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Gpt41Mini;

    public List<LlmTestServerMessage> Messages { get; init; } = [];

    /// <summary>Exact provider/transport namespace for optional stored-response continuation.</summary>
    public string? HistoryNamespace { get; init; }
    public string? PreviousResponseId { get; init; }
    public bool StoreResponse { get; init; }

    /// <summary>The qualified native route contract; null on surfaces without native support.</summary>
    public LlmTestServerNativeToolSurface? NativeToolSurface { get; init; }
    public List<LlmTestServerNativeToolDeclaration> NativeTools { get; init; } = [];
    public LlmTestServerNativeToolContract? RequiredNativeTool { get; init; }
    public List<LlmTestServerNativeToolInclude> NativeIncludes { get; init; } = [];

    public bool Stream { get; init; }
    public List<LlmTestServerToolDefinition> Tools { get; init; } = [];
    public LlmTestServerToolChoice ToolChoice { get; init; }
    /// <summary>Whether a fixture may contain more than one tool call in a response.</summary>
    public bool AllowParallelToolCalls { get; init; } = true;
    public string? RequiredToolName { get; init; }
    public bool RequireJson { get; init; }
    public string? ResponseSchemaJson { get; init; }

    /// <summary>Warms the prompt cache without consuming a configured response.</summary>
    public bool PopulateCacheOnly { get; init; }

    public LlmTestServerPromptCachePolicy PromptCachePolicy { get; init; }

    public string? PromptCacheKey { get; init; }
}

public enum LlmTestServerPromptCachePolicy
{
    None,
    OpenAiCompatible,
    Mistral,
    Anthropic,
    Gemini,
    Bedrock,
    DeepSeek,
    OpenRouter,
    Ollama,
}
