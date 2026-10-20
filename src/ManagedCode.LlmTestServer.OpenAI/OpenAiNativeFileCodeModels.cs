using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiFileSearchRankingOptions
{
    [JsonPropertyName("ranker")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Ranker { get; init; }
    [JsonPropertyName("score_threshold")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? ScoreThreshold { get; init; }
}

public sealed record OpenAiResponseFileSearchCall : OpenAiResponseOutputItem
{
    [JsonIgnore] public override string Type => "file_search_call";
    [JsonPropertyName("queries")] public List<string> Queries { get; init; } = [];
    [JsonPropertyName("results")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<OpenAiFileSearchResult>? Results { get; init; }
}

public sealed record OpenAiFileSearchResult
{
    [JsonPropertyName("file_id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? FileId { get; init; }
    [JsonPropertyName("filename")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Filename { get; init; }
    [JsonPropertyName("text")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; init; }
    [JsonPropertyName("score")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Score { get; init; }
    [JsonPropertyName("attributes")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public Dictionary<string, JsonElement>? Attributes { get; init; }
}

public sealed record OpenAiResponseCodeInterpreterCall : OpenAiResponseOutputItem
{
    [JsonIgnore] public override string Type => "code_interpreter_call";
    [JsonPropertyName("container_id")] public string ContainerId { get; init; } = string.Empty;
    [JsonPropertyName("code")] public string? Code { get; init; }
    [JsonPropertyName("outputs")] public List<OpenAiCodeInterpreterOutput>? Outputs { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(OpenAiCodeInterpreterLogsOutput), "logs")]
[JsonDerivedType(typeof(OpenAiCodeInterpreterImageOutput), "image")]
public abstract record OpenAiCodeInterpreterOutput;

public sealed record OpenAiCodeInterpreterLogsOutput : OpenAiCodeInterpreterOutput
{
    [JsonPropertyName("logs")] public string Logs { get; init; } = string.Empty;
}

public sealed record OpenAiCodeInterpreterImageOutput : OpenAiCodeInterpreterOutput
{
    [JsonPropertyName("url")] public string Url { get; init; } = string.Empty;
}

public sealed record OpenAiFileCitation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "file_citation";
    [JsonPropertyName("file_id")] public string FileId { get; init; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; init; } = string.Empty;
    [JsonPropertyName("index")] public int Index { get; init; }
}

public sealed record OpenAiContainerFileCitation
{
    [JsonPropertyName("type")] public string Type { get; init; } = "container_file_citation";
    [JsonPropertyName("container_id")] public string ContainerId { get; init; } = string.Empty;
    [JsonPropertyName("file_id")] public string FileId { get; init; } = string.Empty;
    [JsonPropertyName("filename")] public string Filename { get; init; } = string.Empty;
    [JsonPropertyName("start_index")] public int StartIndex { get; init; }
    [JsonPropertyName("end_index")] public int EndIndex { get; init; }
}
