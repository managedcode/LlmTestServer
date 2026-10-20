using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Providers;

namespace ManagedCode.LlmTestServer.SystemOne;

public static class SystemOneDecisionMapper
{
    private const int _nativeProbabilityPrecision = 4;
    public const int MaxChoices = 255;
    public const int JevMaxScoreLevels = 10;
    public const int KevMaxScoreLevels = 255;
    public const string KevDefaultModel = "kev-latest";

    public static LlmTestServerRequestValidationResult Validate(JsonElement body)
    {
        return Validate(body, MaxChoices, KevMaxScoreLevels);
    }

    public static LlmTestServerRequestValidationResult Validate(JsonElement body, int maxChoices, int maxScoreLevels)
    {
        return ValidateCore(body, maxChoices, maxScoreLevels, false, false);
    }

    public static LlmTestServerRequestValidationResult Validate(JsonElement body, LlmTestServerDecisionProvider provider)
    {
        return ValidateCore(body, MaxChoices, provider is LlmTestServerDecisionProvider.TypeSafe or LlmTestServerDecisionProvider.Cloudflare ? JevMaxScoreLevels : KevMaxScoreLevels,
                provider == LlmTestServerDecisionProvider.Kev, provider == LlmTestServerDecisionProvider.Cloudflare, provider == LlmTestServerDecisionProvider.TypeSafe);
    }

    private static LlmTestServerRequestValidationResult ValidateCore(JsonElement body, int maxChoices, int maxScoreLevels, bool kev, bool allowScalarPredicateCriteria, bool typeSafe = false)
    {
        if (body.ValueKind != JsonValueKind.Object || (!body.TryGetProperty("model", out var model) ? !kev : model.ValueKind != JsonValueKind.String)
            || !body.TryGetProperty("state", out var state) || !IsContent(state, kev) || (!kev && state.ValueKind == JsonValueKind.Null)
            || !body.TryGetProperty("questions", out var questions) || questions.ValueKind != JsonValueKind.Object)
        { return new("model, supported state and object questions are required."); }
        var entries = questions.EnumerateObject().ToArray();
        if (entries.Length == 0 || entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != entries.Length)
        { return new("Questions must have unique names and at least one entry."); }
        return entries.All(entry => ValidQuestion(entry.Value, maxChoices, maxScoreLevels, kev, allowScalarPredicateCriteria, typeSafe))
            ? new() : new("Every question requires a supported type and matching native criteria.");
    }

    private static bool ValidQuestion(JsonElement question, int maxChoices, int maxScoreLevels, bool kev, bool allowScalarPredicateCriteria, bool typeSafe)
    {
        if (question.ValueKind != JsonValueKind.Object || !question.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String) { return false; }
        if (question.TryGetProperty("instructions", out var instructions) && !IsContent(instructions, kev)) { return false; }
        var hasCriteria = question.TryGetProperty("criteria", out var criteria);
        if (typeSafe && type.GetString() == SystemOneDecisionTypes.Box)
        { return !hasCriteria; }
        if (type.GetString() == SystemOneDecisionTypes.Predicate)
        {
            return !hasCriteria || criteria.ValueKind == JsonValueKind.Null || (criteria.ValueKind == JsonValueKind.Object
                && criteria.EnumerateObject().Where(entry => entry.Name is "true" or "false").All(entry => IsContent(entry.Value, kev || allowScalarPredicateCriteria)));
        }
        if (!hasCriteria) { return false; }
        if (type.GetString() == SystemOneDecisionTypes.Score)
        { return criteria.ValueKind == JsonValueKind.Array && criteria.GetArrayLength() >= 1 && criteria.GetArrayLength() <= maxScoreLevels && criteria.EnumerateArray().All(value => IsContent(value, kev) && (kev || value.ValueKind != JsonValueKind.Null)); }
        if (type.GetString() != SystemOneDecisionTypes.Choice || criteria.ValueKind != JsonValueKind.Object) { return false; }
        var entries = criteria.EnumerateObject().ToArray();
        return entries.Length >= 1 && entries.Length <= maxChoices && entries.All(entry => IsContent(entry.Value, kev))
            && entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() == entries.Length;
    }

    private static bool IsContent(JsonElement value, bool kev)
    {
        return value.ValueKind is JsonValueKind.String or JsonValueKind.Object or JsonValueKind.Array or JsonValueKind.Null
                || (kev && value.ValueKind is JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False);
    }

    public static LlmTestServerDecisionRequest ToRequest(SystemOneDecisionRequest request, LlmTestServerDecisionProvider provider = LlmTestServerDecisionProvider.TypeSafe)
    {
        return new()
        {
            Provider = provider,
            ModelId = request.Model ?? KevDefaultModel,
            Input = request.Assets is { Count: > 0 } ? JsonSerializer.Serialize(new { state = request.State, assets = request.Assets }) : JsonSerializer.Serialize(request.State),
            Questions = request.Questions.Select(entry => new LlmTestServerDecisionQuestion
            {
                Name = entry.Key,
                Kind = entry.Value.Type switch
                { SystemOneDecisionTypes.Predicate => LlmTestServerDecisionKind.Predicate, SystemOneDecisionTypes.Choice => LlmTestServerDecisionKind.Choice, SystemOneDecisionTypes.Box => LlmTestServerDecisionKind.Box, _ => LlmTestServerDecisionKind.Score },
                Options = entry.Value.Type is SystemOneDecisionTypes.Predicate or SystemOneDecisionTypes.Box ? [] : entry.Value.Type == SystemOneDecisionTypes.Score
                    ? entry.Value.Criteria.EnumerateArray().Select(value => JsonSerializer.Serialize(value)).ToList()
                    : entry.Value.Criteria.EnumerateObject().Select(value => value.Name).ToList(),
            }).ToList(),
        };
    }

    public static SystemOneDecisionResponse ToResponse(SystemOneDecisionRequest request, LlmTestServerDecisionResult result, bool roundValues = false,
        LlmTestServerDecisionProvider provider = LlmTestServerDecisionProvider.TypeSafe)
    {
        var kev = provider == LlmTestServerDecisionProvider.Kev;
        return new()
        {
            Model = result.ModelId,
            Answers = result.Answers.ToDictionary(entry => entry.Key, entry => ToAnswer(request.Questions[entry.Key], RoundedAnswer(roundValues, entry.Value), kev), StringComparer.Ordinal),
            Usage = new()
            {
                InputTokens = result.Usage.InputTokens,
                OutputTokens = result.Usage.OutputTokens,
                StateTokens = kev ? result.Metadata.StateTokens : null,
                StateTokensUsed = kev ? result.Metadata.StateTokensUsed : null
            },
            LatencyMilliseconds = kev ? Math.Round(result.Metadata.LatencyMilliseconds, 1) : null,
            Truncated = kev ? result.Metadata.Truncated : null,
        };
    }

    private static LlmTestServerDecisionAnswer RoundedAnswer(bool roundValues, LlmTestServerDecisionAnswer answer)
    {
        if (!roundValues) { return answer; }
        return answer with
        {
            Probability = Round(answer.Probability),
            Score = Round(answer.Score),
            Confidence = Round(answer.Confidence),
            Probabilities = answer.Probabilities.ToDictionary(entry => entry.Key, entry => Math.Round(entry.Value, _nativeProbabilityPrecision))
        };
    }

    private static double? Round(double? value)
    {
        return value.HasValue ? Math.Round(value.Value, _nativeProbabilityPrecision) : null;
    }

    private static SystemOneDecisionAnswer ToAnswer(SystemOneDecisionQuestion question, LlmTestServerDecisionAnswer answer, bool kev)
    {
        return new()
        {
            Type = question.Type,
            Probability = answer.Kind == LlmTestServerDecisionKind.Predicate ? answer.Probability : null,
            Choice = answer.Kind == LlmTestServerDecisionKind.Choice ? answer.Choice : null,
            Score = answer.Kind == LlmTestServerDecisionKind.Score ? answer.Score : null,
            Confidence = answer.Kind == LlmTestServerDecisionKind.Predicate ? null : answer.Confidence,
            Probabilities = answer.Kind is LlmTestServerDecisionKind.Predicate or LlmTestServerDecisionKind.Box ? null : new(answer.Probabilities),
            Box = answer.Box is { } box ? new() { Top = box.Top, Bottom = box.Bottom, Left = box.Left, Right = box.Right } : null,
            Legend = answer.Kind == LlmTestServerDecisionKind.Score ? question.Criteria.EnumerateArray()
                .Select((value, index) => (index, value)).ToDictionary(entry => entry.index.ToString(CultureInfo.InvariantCulture),
                    entry => kev ? JsonSerializer.SerializeToElement(KevDecisionContent.Render(entry.value)) : entry.value.Clone()) : null,
        };
    }
}
