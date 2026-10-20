using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Scenarios;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseServerToolUseItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerNativeToolContract Contract { get; init; } = LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305;
    public LlmTestServerServerToolInput Input { get; init; } = new LlmTestServerWebSearchInput();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerWebSearchInput), "web_search")]
public abstract record LlmTestServerServerToolInput;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchInput : LlmTestServerServerToolInput
{
    public string Query { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseServerToolResultItem : LlmTestServerResponseItem
{
    public string ToolUseId { get; init; } = string.Empty;
    public LlmTestServerNativeToolContract Contract { get; init; } = LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305;
    public LlmTestServerServerToolResult Result { get; init; } = new LlmTestServerWebSearchResults();
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerWebSearchResults), "web_search_results")]
[JsonDerivedType(typeof(LlmTestServerWebSearchErrorResult), "web_search_error")]
public abstract record LlmTestServerServerToolResult;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchResults : LlmTestServerServerToolResult
{
    public List<LlmTestServerAnthropicWebSearchResult> Results { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerAnthropicWebSearchResult
{
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string EncryptedContent { get; init; } = string.Empty;
    public string? PageAge { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerWebSearchErrorResult : LlmTestServerServerToolResult
{
    public string ErrorCode { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerAnthropicWebSearchCitation : LlmTestServerResponseCitation
{
    public string Url { get; init; } = string.Empty;
    public string EncryptedIndex { get; init; } = string.Empty;
    public string CitedText { get; init; } = string.Empty;
    public string? Title { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerAnthropicWebSearchOptions
{
    public int? MaxUses { get; init; }
    public List<string>? AllowedDomains { get; init; }
    public List<string>? BlockedDomains { get; init; }
    public LlmTestServerWebSearchLocation? UserLocation { get; init; }
}
