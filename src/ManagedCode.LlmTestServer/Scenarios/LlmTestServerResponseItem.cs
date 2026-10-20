using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Scenarios;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerResponseMessageItem), "message")]
[JsonDerivedType(typeof(LlmTestServerResponseFunctionCallItem), "function_call")]
[JsonDerivedType(typeof(LlmTestServerResponseWebSearchItem), "web_search")]
[JsonDerivedType(typeof(LlmTestServerResponseFileSearchItem), "file_search")]
[JsonDerivedType(typeof(LlmTestServerResponseCodeInterpreterItem), "code_interpreter")]
[JsonDerivedType(typeof(LlmTestServerResponseServerToolUseItem), "server_tool_use")]
[JsonDerivedType(typeof(LlmTestServerResponseServerToolResultItem), "server_tool_result")]
public abstract record LlmTestServerResponseItem;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseMessageItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public List<string> StreamChunks { get; init; } = [];
    public List<LlmTestServerResponseCitation> Citations { get; init; } = [];
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

/// <summary>Typed provider citations; indexed citation variants use their explicit bounded fixture ranges.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerUrlCitation), "url")]
[JsonDerivedType(typeof(LlmTestServerFileCitation), "file")]
[JsonDerivedType(typeof(LlmTestServerContainerFileCitation), "container_file")]
[JsonDerivedType(typeof(LlmTestServerAnthropicWebSearchCitation), "anthropic_web_search")]
public abstract record LlmTestServerResponseCitation;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerUrlCitation : LlmTestServerResponseCitation
{
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
    public string Url { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
}

public enum LlmTestServerNativeToolContract { OpenAiResponsesWebSearch, OpenAiResponsesFileSearch, OpenAiResponsesCodeInterpreter, AnthropicMessagesWebSearch20250305 }
public enum LlmTestServerNativeToolSurface { OpenAiResponses, AnthropicMessages }
public enum LlmTestServerNativeToolInclude { WebSearchSources, FileSearchResults, CodeInterpreterOutputs }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerNativeToolDeclaration
{
    public LlmTestServerNativeToolContract Contract { get; init; }
    public LlmTestServerWebSearchOptions? WebSearchOptions { get; init; }
    public LlmTestServerAnthropicWebSearchOptions? AnthropicWebSearchOptions { get; init; }
    public LlmTestServerFileSearchOptions? FileSearchOptions { get; init; }
    public LlmTestServerCodeInterpreterOptions? CodeInterpreterOptions { get; init; }
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


[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerFileCitation : LlmTestServerResponseCitation
{
    public string FileId { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public int Index { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerContainerFileCitation : LlmTestServerResponseCitation
{
    public string ContainerId { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public string Filename { get; init; } = string.Empty;
    public int StartIndex { get; init; }
    public int EndIndex { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseFileSearchItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerNativeToolContract Contract { get; init; } = LlmTestServerNativeToolContract.OpenAiResponsesFileSearch;
    public List<string> VectorStoreIds { get; init; } = [];
    public List<string> Queries { get; init; } = [];
    public List<LlmTestServerFileSearchResult> Results { get; init; } = [];
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerFileSearchResult
{
    public string? FileId { get; init; }
    public string? Filename { get; init; }
    public string? Text { get; init; }
    public double? Score { get; init; }
    public Dictionary<string, JsonElement>? Attributes { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerResponseCodeInterpreterItem : LlmTestServerResponseItem
{
    public string Id { get; init; } = string.Empty;
    public LlmTestServerNativeToolContract Contract { get; init; } = LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter;
    public string ContainerId { get; init; } = string.Empty;
    public List<string> InputFileIds { get; init; } = [];
    public List<string> GeneratedFileIds { get; init; } = [];
    public string? Code { get; init; }
    public List<string> CodeChunks { get; init; } = [];
    public List<LlmTestServerCodeInterpreterOutput>? Outputs { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(LlmTestServerCodeInterpreterLogsOutput), "logs")]
[JsonDerivedType(typeof(LlmTestServerCodeInterpreterImageOutput), "image")]
public abstract record LlmTestServerCodeInterpreterOutput;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerCodeInterpreterLogsOutput : LlmTestServerCodeInterpreterOutput
{
    public string Logs { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerCodeInterpreterImageOutput : LlmTestServerCodeInterpreterOutput
{
    public string Url { get; init; } = string.Empty;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerFileSearchOptions
{
    public List<string> VectorStoreIds { get; init; } = [];
    public int? MaxNumResults { get; init; }
    public string? Ranker { get; init; }
    public double? ScoreThreshold { get; init; }
    public JsonElement? Filters { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LlmTestServerCodeInterpreterOptions
{
    public string? ContainerId { get; init; }
    public bool AutomaticContainer { get; init; }
    public List<string> FileIds { get; init; } = [];
}
