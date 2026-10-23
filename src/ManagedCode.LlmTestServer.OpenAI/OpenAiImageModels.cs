using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiImageGenerationRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.GptImage1;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("partial_images")]
    public int? PartialImages { get; init; }

    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }

    [JsonPropertyName("quality")]
    public string? Quality { get; init; }

    [JsonPropertyName("background")]
    public string? Background { get; init; }

    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; init; }
}

public sealed record OpenAiImageGenerationResponse
{
    [JsonPropertyName("created")]
    public long Created { get; init; }

    [JsonPropertyName("data")]
    public List<OpenAiImageData> Data { get; init; } = [];

    [JsonPropertyName("background")]
    public string? Background { get; init; }

    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; init; }

    [JsonPropertyName("quality")]
    public string? Quality { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }
}

public sealed record OpenAiImageData
{
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("b64_json")]
    public string? Base64Json { get; init; }

    [JsonPropertyName("revised_prompt")]
    public string? RevisedPrompt { get; init; }
}

public sealed record OpenAiImageReference
{
    [JsonPropertyName("file_id")]
    public string? FileId { get; init; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; init; }
}

public sealed record OpenAiImageEditRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.GptImage1;

    [JsonPropertyName("prompt")]
    public string Prompt { get; init; } = string.Empty;

    [JsonPropertyName("images")]
    public IReadOnlyList<OpenAiImageReference> Images { get; init; } = [];

    [JsonPropertyName("image")]
    public JsonElement Image { get; init; }

    [JsonPropertyName("mask")]
    public OpenAiImageReference? Mask { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("partial_images")]
    public int? PartialImages { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }

    [JsonPropertyName("quality")]
    public string? Quality { get; init; }

    [JsonPropertyName("background")]
    public string? Background { get; init; }

    [JsonPropertyName("output_format")]
    public string? OutputFormat { get; init; }
}

public sealed record OpenAiImageVariationRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = "dall-e-2";

    [JsonPropertyName("n")]
    public int? Count { get; init; }

    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; init; }

    [JsonPropertyName("size")]
    public string? Size { get; init; }
}

public sealed record OpenAiImageStreamingEvent
{
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("b64_json")] public string Base64Json { get; init; } = string.Empty;
    [JsonPropertyName("created_at")] public long CreatedAt { get; init; }
    [JsonPropertyName("size")] public string Size { get; init; } = "1024x1024";
    [JsonPropertyName("quality")] public string Quality { get; init; } = "auto";
    [JsonPropertyName("background")] public string Background { get; init; } = "auto";
    [JsonPropertyName("output_format")] public string OutputFormat { get; init; } = "png";
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiImageStreamingUsage? Usage { get; init; }
    [JsonPropertyName("partial_image_index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PartialImageIndex { get; init; }
}

public sealed record OpenAiImageStreamingUsage
{
    [JsonPropertyName("total_tokens")] public int TotalTokens { get; init; }
    [JsonPropertyName("input_tokens")] public int InputTokens { get; init; }
    [JsonPropertyName("output_tokens")] public int OutputTokens { get; init; }
    [JsonPropertyName("input_tokens_details")] public OpenAiImageStreamingInputTokensDetails InputTokensDetails { get; init; } = new();
}

public sealed record OpenAiImageStreamingInputTokensDetails
{
    [JsonPropertyName("text_tokens")] public int TextTokens { get; init; }
    [JsonPropertyName("image_tokens")] public int ImageTokens { get; init; }
}
