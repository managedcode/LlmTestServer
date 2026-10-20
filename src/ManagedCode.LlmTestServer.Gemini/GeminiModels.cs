using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.Gemini;

public sealed record GeminiGenerateContentRequest
{
    [JsonPropertyName("tools")] public List<GeminiTool> Tools { get; init; } = [];
    [JsonPropertyName("toolConfig")] public GeminiToolConfig? ToolConfig { get; init; }
    [JsonPropertyName("generationConfig")] public GeminiGenerationConfig? GenerationConfig { get; init; }

    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; init; } = [];

    [JsonPropertyName("systemInstruction")]
    public GeminiContent? SystemInstruction { get; init; }
}

public sealed record GeminiEmbedContentRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public GeminiContent Content { get; init; } = new();
}

public sealed record GeminiPredictLongRunningRequest
{
    [JsonPropertyName("instances")]
    public List<JsonElement> Instances { get; init; } = [];

    [JsonPropertyName("parameters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement? Parameters { get; init; }
}

public sealed record GeminiContent
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; init; } = [];
}

public sealed record GeminiPart
{
    [JsonPropertyName("thoughtSignature")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ThoughtSignature { get; init; }

    [JsonPropertyName("functionCall")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GeminiFunctionCall? FunctionCall { get; init; }
    [JsonPropertyName("functionResponse")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public GeminiFunctionResponse? FunctionResponse { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("inlineData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement? InlineData { get; init; }

    [JsonPropertyName("inline_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement? InlineDataSnakeCase { get; init; }

    [JsonPropertyName("fileData")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement? FileData { get; init; }

    [JsonPropertyName("file_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public JsonElement? FileDataSnakeCase { get; init; }
}

public sealed record GeminiGenerateContentResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate> Candidates { get; init; } = [];

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata UsageMetadata { get; init; } = new();

    [JsonPropertyName("modelVersion")]
    public string ModelVersion { get; init; } = string.Empty;

    [JsonPropertyName("responseId")]
    public string ResponseId { get; init; } = string.Empty;
}

public sealed record GeminiCandidate
{
    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiContent? Content { get; init; } = new();

    [JsonPropertyName("finishReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; init; }

    [JsonPropertyName("index")]
    public int Index { get; init; }
}

public sealed record GeminiUsageMetadata
{
    [JsonPropertyName("thoughtsTokenCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ThoughtsTokenCount { get; init; }

    [JsonPropertyName("promptTokenCount")]
    public int PromptTokenCount { get; init; }

    [JsonPropertyName("cachedContentTokenCount")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int CachedContentTokenCount { get; init; }

    [JsonPropertyName("candidatesTokenCount")]
    public int CandidatesTokenCount { get; init; }

    [JsonPropertyName("totalTokenCount")]
    public int TotalTokenCount { get; init; }
}

public sealed record GeminiEmbedContentResponse
{
    [JsonPropertyName("embedding")]
    public GeminiContentEmbedding Embedding { get; init; } = new();

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata UsageMetadata { get; init; } = new();
}

public sealed record GeminiContentEmbedding
{
    [JsonPropertyName("values")]
    public IReadOnlyList<float> Values { get; init; } = [];
}

public sealed record GeminiOperationResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("response")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiVideoOperationResponse? Response { get; init; }
}

public sealed record GeminiVideoOperationResponse
{
    [JsonPropertyName("generateVideoResponse")]
    public GeminiGenerateVideoResponse GenerateVideoResponse { get; init; } = new();
}

public sealed record GeminiGenerateVideoResponse
{
    [JsonPropertyName("generatedSamples")]
    public IReadOnlyList<GeminiGeneratedVideoSample> GeneratedSamples { get; init; } = [];

    [JsonPropertyName("usageMetadata")]
    public GeminiUsageMetadata UsageMetadata { get; init; } = new();
}

public sealed record GeminiGeneratedVideoSample
{
    [JsonPropertyName("video")]
    public GeminiGeneratedVideo Video { get; init; } = new();
}

public sealed record GeminiGeneratedVideo
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = string.Empty;
}

public sealed record GeminiFileResponse
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    public string MimeType { get; init; } = string.Empty;

    [JsonPropertyName("sizeBytes")]
    public string SizeBytes { get; init; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("downloadUri")]
    public string DownloadUri { get; init; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; init; } = "ACTIVE";

    [JsonPropertyName("source")]
    public string Source { get; init; } = "GENERATED";

    [JsonPropertyName("videoMetadata")]
    public GeminiVideoFileMetadata VideoMetadata { get; init; } = new();
}

public sealed record GeminiVideoFileMetadata
{
    [JsonPropertyName("videoDuration")]
    public string VideoDuration { get; init; } = string.Empty;
}

public sealed record GeminiErrorResponse
{
    [JsonPropertyName("error")]
    public GeminiError Error { get; init; } = new();
}

public sealed record GeminiError
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "INVALID_ARGUMENT";
}

public sealed record GeminiTool
{
    [JsonPropertyName("functionDeclarations")] public List<GeminiFunctionDeclaration> FunctionDeclarations { get; init; } = [];
}
public sealed record GeminiFunctionDeclaration
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("parameters")] public JsonElement? Parameters { get; init; }
    [JsonPropertyName("parametersJsonSchema")] public JsonElement? ParametersJsonSchema { get; init; }
}
public sealed record GeminiFunctionCall
{
    [JsonPropertyName("id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("args")] public JsonElement Args { get; init; }
}
public sealed record GeminiFunctionResponse
{
    [JsonPropertyName("id")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("response")] public JsonElement Response { get; init; }
}
public sealed record GeminiToolConfig
{
    [JsonPropertyName("functionCallingConfig")] public GeminiFunctionCallingConfig? FunctionCallingConfig { get; init; }
}
public sealed record GeminiFunctionCallingConfig
{
    [JsonPropertyName("mode")] public string Mode { get; init; } = "AUTO";
    [JsonPropertyName("allowedFunctionNames")] public List<string>? AllowedFunctionNames { get; init; }
}
public sealed record GeminiGenerationConfig
{
    [JsonPropertyName("responseMimeType")] public string? ResponseMimeType { get; init; }
    [JsonPropertyName("responseSchema")] public JsonElement? ResponseSchema { get; init; }
    [JsonPropertyName("responseJsonSchema")] public JsonElement? ResponseJsonSchema { get; init; }
}
