using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Scenarios;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerResponseMessageItem), "message")]
[JsonDerivedType(typeof(LlmTestServerResponseFunctionCallItem), "function_call")]
[JsonDerivedType(typeof(LlmTestServerResponseWebSearchItem), "web_search")]
public abstract record LlmTestServerResponseItem;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseMessageItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public List<string> StreamChunks { get; init; } = [];
    public List<LlmTestServerUrlCitation> Citations { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseFunctionCallItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerToolCall Call { get; init; } = new();
}

/// <summary>A completed, explicitly scripted search. It never performs network access.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseWebSearchItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerNativeToolContract Contract { get; init; } = LlmTestServerNativeToolContract.OpenAiResponsesWebSearch;
    public List<string> Queries { get; init; } = [];
    public List<LlmTestServerWebSearchSource> Sources { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchSource
{
    public string Url { get; init; } = string.Empty;
}

/// <summary>Explicit provider citation indexes; this initial fixture contract accepts ASCII text only.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerUrlCitation
{
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
}

public enum LlmTestServerNativeToolContract { OpenAiResponsesWebSearch }
public enum LlmTestServerNativeToolInclude { WebSearchSources }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerNativeToolDeclaration
{
    public LlmTestServerNativeToolContract Contract { get; init; }
    public LlmTestServerWebSearchOptions? WebSearchOptions { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchOptions
{
    public string? SearchContextSize { get; init; }
    public LlmTestServerWebSearchLocation? UserLocation { get; init; }
    public List<string> AllowedDomains { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchLocation
{
    public string Type { get; init; } = "approximate";
    public string? Country { get; init; }
    public string? Region { get; init; }
    public string? City { get; init; }
    public string? Timezone { get; init; }
}
