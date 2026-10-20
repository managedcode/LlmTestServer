using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed record LlmTestServerChatResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Gpt41Mini;

    public string? ScenarioId { get; init; }

    public string? ResponseId { get; init; }

    public List<LlmTestServerToolCall> ToolCalls { get; init; } = [];

    public string Content { get; init; } = string.Empty;

    public List<string> StreamChunks { get; init; } = [];

    public LlmTestServerTokenUsage Usage { get; init; } = new();

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static LlmTestServerChatResult Success(
        string modelId,
        string scenarioId,
        string content,
        IEnumerable<string> streamChunks,
        LlmTestServerTokenUsage? usage = null
    )
    {
        return new()
        {
            IsSuccess = true,
            ModelId = modelId,
            ScenarioId = scenarioId,
            Content = content,
            StreamChunks = [.. streamChunks],
            Usage = usage ?? new LlmTestServerTokenUsage(),
        };
    }

    public static LlmTestServerChatResult Failure(
        string modelId,
        int statusCode,
        string errorCode,
        string errorMessage,
        string? scenarioId = null,
        LlmTestServerTokenUsage? usage = null
    )
    {
        return new()
        {
            IsSuccess = false,
            ModelId = modelId,
            ScenarioId = scenarioId,
            StatusCode = statusCode,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Usage = usage ?? new LlmTestServerTokenUsage(),
        };
    }
}

public sealed record LlmTestServerEmbeddingResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.TextEmbedding3Small;

    public List<List<float>> Vectors { get; init; } = [];

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record LlmTestServerImageResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.GptImage1;

    public string DataUri { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record LlmTestServerAudioResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Gpt4OMiniTts;

    public byte[] Bytes { get; init; } = [];

    public string MediaType { get; init; } = "audio/mpeg";

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record LlmTestServerTranscriptionResult
{
    public LlmTestServerTranscriptionUsage? Usage { get; init; }

    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Gpt4OMiniTts;

    public string Text { get; init; } = string.Empty;

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}

public sealed record LlmTestServerVideoResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; } = 200;

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Sora2;

    public string Prompt { get; init; } = string.Empty;

    public string VideoId { get; init; } = "video_llm_test_server";

    public string GenerationId { get; init; } = "gen_llm_test_server";

    public string? RemixedFromVideoId { get; init; }

    public byte[] Bytes { get; init; } = [];

    public string MediaType { get; init; } = "video/mp4";

    public long CreatedAt { get; init; }

    public string Size { get; init; } = "1280x720";

    public string Seconds { get; init; } = "4";

    public LlmTestServerTokenUsage Usage { get; init; } = new();

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }
}
