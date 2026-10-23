using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiAudioSpeechRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.Gpt4OMiniTts;

    [JsonPropertyName("input")]
    public string Input { get; init; } = string.Empty;

    [JsonPropertyName("voice")]
    public string Voice { get; init; } = string.Empty;

    [JsonPropertyName("response_format")]
    public string? ResponseFormat { get; init; }
}

public sealed record OpenAiAudioTranscriptionResponse
{
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiAudioTranscriptionUsage? Usage { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;
}

public sealed record GroqAudioTranscriptionResponse
{
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("x_groq")]
    public GroqRequestMetadata XGroq { get; init; } = new();
}

public sealed record GroqRequestMetadata
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;
}

public sealed record OpenAiVerboseAudioTranscriptionResponse
{
    [JsonPropertyName("usage")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiAudioTranscriptionUsage? Usage { get; init; }

    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; init; } = "en";

    [JsonPropertyName("duration")]
    public double Duration { get; init; }

    [JsonPropertyName("segments")]
    public IReadOnlyList<OpenAiAudioTranscriptionSegment> Segments { get; init; } = [];
}

public sealed record OpenAiAudioTranscriptionUsage
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "duration";

    [JsonPropertyName("seconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Seconds { get; init; }

    [JsonPropertyName("input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? OutputTokens { get; init; }

    [JsonPropertyName("total_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalTokens { get; init; }

    [JsonPropertyName("input_token_details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiAudioTranscriptionInputTokenDetails? InputTokenDetails { get; init; }
}

public sealed record OpenAiAudioTranscriptionInputTokenDetails
{
    [JsonPropertyName("audio_tokens")]
    public long AudioTokens { get; init; }

    [JsonPropertyName("text_tokens")]
    public long TextTokens { get; init; }
}

public sealed record OpenAiAudioTranscriptionSegment
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("seek")] public int Seek { get; init; }
    [JsonPropertyName("start")] public double Start { get; init; }
    [JsonPropertyName("end")] public double End { get; init; }
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
    [JsonPropertyName("tokens")] public IReadOnlyList<int> Tokens { get; init; } = [];
    [JsonPropertyName("temperature")] public double Temperature { get; init; }
    [JsonPropertyName("avg_logprob")] public double AverageLogProbability { get; init; }
    [JsonPropertyName("compression_ratio")] public double CompressionRatio { get; init; }
    [JsonPropertyName("no_speech_prob")] public double NoSpeechProbability { get; init; }
}

public sealed record OpenAiTranscriptTextDeltaEvent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "transcript.text.delta";
    [JsonPropertyName("delta")] public string Delta { get; init; } = string.Empty;
}

public sealed record OpenAiTranscriptTextDoneEvent
{
    [JsonPropertyName("type")] public string Type { get; init; } = "transcript.text.done";
    [JsonPropertyName("text")] public string Text { get; init; } = string.Empty;
    [JsonPropertyName("usage")] public OpenAiAudioTranscriptionUsage? Usage { get; init; }
}
