using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.OpenAI;

public static class OpenAiDecisionMapper
{
    public const int MaxQuestionTextLength = 1_048_576;
    public const int MaxChoices = 255;
    private const int _minChoices = 2;
    private const int _maxSafetyIdentifierLength = 128;

    public static LlmTestServerRequestValidationResult Validate(JsonElement body)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty("model", out var model)
            || !ValidString(model) || string.IsNullOrWhiteSpace(model.GetString())
            || !body.TryGetProperty("input", out var input) || !OpenAiDecisionInputValidation.IsValid(input)
            || !body.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Array)
        { return new("model, supported input and questions array are required."); }
        if (body.TryGetProperty("safety_identifier", out var safety) && safety.ValueKind != JsonValueKind.Null
            && !ValidString(safety, _maxSafetyIdentifierLength)) { return new("Invalid safety_identifier."); }
        var entries = questions.EnumerateArray().ToArray();
        return entries.All(ValidQuestion)
            ? new() : new("Every decision question requires type, instructions and matching choices or levels.");
    }

    private static bool ValidQuestion(JsonElement question)
    {
        if (question.ValueKind != JsonValueKind.Object || !question.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String || !ValidOptionalString(question, "name")
            || !question.TryGetProperty("instructions", out var instructions) || !ValidString(instructions)) { return false; }
        if (type.GetString() == OpenAiDecisionTypes.Predicate) { return true; }
        var choice = type.GetString() == OpenAiDecisionTypes.Choice;
        var key = choice ? "choices" : "levels";
        var id = choice ? "value" : "label";
        if (type.GetString() is not (OpenAiDecisionTypes.Choice or OpenAiDecisionTypes.Score)
            || !question.TryGetProperty(key, out var options) || options.ValueKind != JsonValueKind.Array) { return false; }
        var entries = options.EnumerateArray().ToArray();
        if (choice && (entries.Length < _minChoices || entries.Length > MaxChoices)) { return false; }
        return entries.All(entry => entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(id, out var value)
            && (choice ? value.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.String : ValidString(value))
            && ValidOptionalString(entry, "description"))
            && (!choice || entries.Select(entry => ChoiceId(entry.GetProperty(id))).Distinct(StringComparer.Ordinal).Count() == entries.Length);
    }

    private static bool ValidOptionalString(JsonElement body, string property)
    {
        return !body.TryGetProperty(property, out var value) || ValidString(value);
    }

    private static bool ValidString(JsonElement value, int maxLength = MaxQuestionTextLength)
    {
        return value.ValueKind == JsonValueKind.String && value.GetString()!.Length <= maxLength;
    }

    public static LlmTestServerDecisionRequest ToRequest(OpenAiDecisionRequest request)
    {
        return new()
        {
            Provider = LlmTestServerDecisionProvider.OpenAI,
            ModelId = request.Model,
            Input = request.Input.ValueKind == JsonValueKind.String ? request.Input.GetString()! : JsonSerializer.Serialize(request.Input),
            Questions = request.Questions.Select((question, index) => new LlmTestServerDecisionQuestion
            {
                Name = OpenAiDecisionFixtureIds.ForQuestionIndex(index),
                Kind = question.Type switch { OpenAiDecisionTypes.Predicate => LlmTestServerDecisionKind.Predicate, OpenAiDecisionTypes.Choice => LlmTestServerDecisionKind.Choice, _ => LlmTestServerDecisionKind.Score },
                Options = question.Type == OpenAiDecisionTypes.Score ? question.Levels.Select(option => option.Label).ToList() : question.Choices.Select(option => ChoiceId(option.Value)).ToList(),
            }).ToList(),
        };
    }

    public static OpenAiDecisionResponse ToResponse(OpenAiDecisionRequest request, LlmTestServerDecisionResult result)
    {
        return new()
        {
            Model = result.ModelId,
            Answers = request.Questions.Select((question, index) => ToAnswer(question, result.Answers[OpenAiDecisionFixtureIds.ForQuestionIndex(index)])).ToList(),
            Usage = new()
            {
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                TotalTokens = result.Usage.TotalTokens,
                InputTokensDetails = new() { CachedTokens = result.Usage.CachedInputTokens, CacheWriteTokens = result.Usage.CacheCreationInputTokens },
                OutputTokensDetails = new() { ReasoningTokens = result.Usage.ReasoningTokens }
            },
        };
    }

    private static OpenAiDecisionAnswer ToAnswer(OpenAiDecisionQuestion question, LlmTestServerDecisionAnswer answer)
    {
        return new()
        {
            Name = question.Name,
            Type = answer.Refused ? "refusal" : question.Type,
            Probability = answer.Refused || question.Type != OpenAiDecisionTypes.Predicate ? null : answer.Probability,
            Choice = answer.Refused || question.Type != OpenAiDecisionTypes.Choice || answer.Choice is null ? null : NativeChoice(question, answer.Choice),
            Score = answer.Refused || question.Type != OpenAiDecisionTypes.Score ? null : answer.Score,
            Confidence = answer.Refused || question.Type == OpenAiDecisionTypes.Predicate ? null : answer.Confidence,
            Probabilities = answer.Refused || question.Type == OpenAiDecisionTypes.Predicate ? null : answer.Probabilities.Select(entry => new OpenAiDecisionProbability
            {
                Value = question.Type == OpenAiDecisionTypes.Score ? int.Parse(entry.Key, CultureInfo.InvariantCulture) : NativeChoice(question, entry.Key),
                Label = question.Type == OpenAiDecisionTypes.Score ? question.Levels[int.Parse(entry.Key, CultureInfo.InvariantCulture)].Label : null,
                Probability = entry.Value,
            }).ToList(),
        };
    }
    private static string ChoiceId(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.String ? OpenAiDecisionFixtureIds.ForChoiceValue(value.GetString()!) : OpenAiDecisionFixtureIds.ForChoiceValue(value.GetBoolean());
    }

    private static object NativeChoice(OpenAiDecisionQuestion question, string value)
    {
        var native = question.Choices.First(option => ChoiceId(option.Value) == value).Value;
        return native.ValueKind is JsonValueKind.True or JsonValueKind.False ? native.GetBoolean() : native.GetString()!;
    }
}
