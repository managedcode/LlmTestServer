using System.Text.Json;
using System.Text.Json.Serialization;

namespace ManagedCode.LlmTestServer.OpenAI;

public static class OpenAiDecisionTypes
{
    public const string Predicate = "predicate";
    public const string Choice = "choice";
    public const string Score = "score";
}
public sealed record OpenAiDecisionRequest
{
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("input")] public JsonElement Input { get; init; }
    [JsonPropertyName("questions")] public List<OpenAiDecisionQuestion> Questions { get; init; } = [];
    [JsonPropertyName("safety_identifier")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? SafetyIdentifier { get; init; }
}
public sealed record OpenAiDecisionQuestion
{
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("name")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonPropertyName("instructions")] public string Instructions { get; init; } = string.Empty;
    [JsonPropertyName("choices")] public List<OpenAiDecisionOption> Choices { get; init; } = [];
    [JsonPropertyName("levels")] public List<OpenAiDecisionLevel> Levels { get; init; } = [];
}
public sealed record OpenAiDecisionOption
{
    [JsonPropertyName("value")] public JsonElement Value { get; init; }
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
}
public sealed record OpenAiDecisionLevel
{
    [JsonPropertyName("label")] public string Label { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
}
public sealed record OpenAiDecisionResponse
{
    [JsonPropertyName("model")] public string Model { get; init; } = string.Empty;
    [JsonPropertyName("answers")] public List<OpenAiDecisionAnswer> Answers { get; init; } = [];
    [JsonPropertyName("usage")] public OpenAiResponseUsage Usage { get; init; } = new();
}
public sealed record OpenAiDecisionAnswer
{
    [JsonPropertyName("type")] public string Type { get; init; } = string.Empty;
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("probability")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Probability { get; init; }
    [JsonPropertyName("choice")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Choice { get; init; }
    [JsonPropertyName("score")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Score { get; init; }
    [JsonPropertyName("confidence")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Confidence { get; init; }
    [JsonPropertyName("probabilities")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public List<OpenAiDecisionProbability>? Probabilities { get; init; }
}
public sealed record OpenAiDecisionProbability
{
    [JsonPropertyName("value")] public JsonElement Value { get; init; } = JsonSerializer.SerializeToElement(string.Empty);
    [JsonPropertyName("label")][JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Label { get; init; }
    [JsonPropertyName("probability")] public double Probability { get; init; }
}
