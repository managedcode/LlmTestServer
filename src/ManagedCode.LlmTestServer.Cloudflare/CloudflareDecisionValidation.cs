using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Cloudflare;

public static class CloudflareDecisionValidation
{
    public const int MaxQuestions = 64;
    public const int MaxQuestionIdLength = 100;

    public static LlmTestServerRequestValidationResult Validate(JsonElement body)
    {
        var native = ValidateNative(body);
        return new(native.Error);
    }

    public static CloudflareDecisionValidationResult ValidateNative(JsonElement body)
    {
        var shape = SystemOneDecisionMapper.Validate(body, LlmTestServerDecisionProvider.Cloudflare);
        if (!shape.IsValid) { return new(shape.Error); }
        var model = body.GetProperty("model").GetString()!.Trim();
        if (model is not ("clef" or "clef-flash")) { return new("Clef requires its short native model selector."); }
        if (body.GetProperty("state").ValueKind is not (JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array))
        { return new("Clef state must be text, a JSON object or a JSON array."); }
        var questions = body.GetProperty("questions").EnumerateObject().ToArray();
        if (!questions.All(question => HasRequiredInstructions(question.Value) && HasEnoughCriteria(question.Value)))
        { return new("Clef questions require instructions; choice and score criteria require at least two entries."); }
        if (questions.Length > MaxQuestions || questions.Any(question => question.Name.Length is 0 or > MaxQuestionIdLength
            || question.Name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '.' or '-'))))
        { return new("Clef supports at most 64 questions with ASCII letters, digits, underscore, dot and hyphen IDs of at most 100 characters."); }
        if (questions.Any(question => question.Value.GetProperty("type").GetString() == SystemOneDecisionTypes.Predicate
            && question.Value.TryGetProperty("criteria", out var criteria) && criteria.ValueKind != JsonValueKind.Object))
        { return new("Clef predicate criteria must be an object when supplied."); }
        if (questions.Any(question => question.Value.GetProperty("type").GetString() == SystemOneDecisionTypes.Choice
            && question.Value.GetProperty("criteria").EnumerateObject().Any(candidate => candidate.Name.Length == 0)))
        { return new("Clef choice option IDs must be nonempty strings."); }
        return CloudflareDecisionImageValidation.Validate(body);
    }

    private static bool HasRequiredInstructions(JsonElement question)
    {
        return question.TryGetProperty("instructions", out var instructions) && (instructions.ValueKind is JsonValueKind.Object or JsonValueKind.Array
            || (instructions.ValueKind == JsonValueKind.String && instructions.GetString()!.Length > 0));
    }

    private static bool HasEnoughCriteria(JsonElement question)
    {
        return question.GetProperty("type").GetString() switch
        {
            SystemOneDecisionTypes.Choice => question.GetProperty("criteria").EnumerateObject().Count() >= 2,
            SystemOneDecisionTypes.Score => question.GetProperty("criteria").GetArrayLength() >= 2,
            _ => true,
        };
    }
}
