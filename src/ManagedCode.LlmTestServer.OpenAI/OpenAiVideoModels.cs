using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiVideoCreateRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.Sora2;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("seconds")]
    public string? Seconds { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }
}

public sealed record OpenAiVideoResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "video";

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "queued";

    [JsonPropertyName("progress")]
    public int Progress { get; init; }

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public long? CompletedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; init; }

    [JsonPropertyName("error")]
    public OpenAiVideoError? Error { get; init; }

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("remixed_from_video_id")]
    public string? RemixedFromVideoId { get; init; }

    [JsonPropertyName("seconds")]
    public string Seconds { get; init; } = "4";

    [JsonPropertyName("size")]
    public string Size { get; init; } = "1280x720";

    [JsonPropertyName("quality")]
    public string Quality { get; init; } = "standard";
}

public sealed record OpenAiVideoError
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed record OpenAiVideoListResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";

    [JsonPropertyName("data")]
    public IReadOnlyList<OpenAiVideoResponse> Data { get; init; } = [];

    [JsonPropertyName("first_id")]
    public string? FirstId { get; init; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }
}

public sealed record OpenAiVideoDeleteResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("deleted")]
    public bool Deleted { get; init; } = true;

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "video.deleted";
}

public sealed record OpenAiVideoReference
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

public sealed record OpenAiVideoEditRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("video")]
    public OpenAiVideoReference? Video { get; init; }
}

public sealed record OpenAiVideoExtensionRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("seconds")]
    public string? Seconds { get; init; }

    [JsonPropertyName("video")]
    public OpenAiVideoReference? Video { get; init; }
}

public sealed record OpenAiVideoRemixRequest
{
    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;
}

public sealed record OpenAiVideoCharacterResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "char_llm_tck";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

public sealed record AzureVideoGenerationJobRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.Sora2;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("width")]
    public int Width { get; init; } = 1280;

    [JsonPropertyName("height")]
    public int Height { get; init; } = 720;

    [JsonPropertyName("n_seconds")]
    public int NSeconds { get; init; } = 4;

    [JsonPropertyName("n_variants")]
    public int NVariants { get; init; } = 1;
}

public sealed record AzureVideoGenerationJobResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "video.generation.job";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = "succeeded";

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("finished_at")]
    public long? FinishedAt { get; init; }

    [JsonPropertyName("expires_at")]
    public long? ExpiresAt { get; init; }

    [JsonPropertyName("generations")]
    public IReadOnlyList<AzureVideoGenerationResponse> Generations { get; init; } = [];

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("n_variants")]
    public int NVariants { get; init; } = 1;

    [JsonPropertyName("n_seconds")]
    public int NSeconds { get; init; } = 4;

    [JsonPropertyName("height")]
    public int Height { get; init; } = 720;

    [JsonPropertyName("width")]
    public int Width { get; init; } = 1280;

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; init; }
}

public sealed record AzureVideoGenerationResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "video.generation";

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("job_id")]
    public string JobId { get; init; } = string.Empty;

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; } = 1280;

    [JsonPropertyName("height")]
    public int Height { get; init; } = 720;

    [JsonPropertyName("n_seconds")]
    public int NSeconds { get; init; } = 4;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;
}

public sealed record AzureVideoGenerationJobListResponse
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";

    [JsonPropertyName("data")]
    public IReadOnlyList<AzureVideoGenerationJobResponse> Data { get; init; } = [];

    [JsonPropertyName("first_id")]
    public string? FirstId { get; init; }

    [JsonPropertyName("last_id")]
    public string? LastId { get; init; }

    [JsonPropertyName("has_more")]
    public bool HasMore { get; init; }
}
