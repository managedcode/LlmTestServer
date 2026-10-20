using System.Globalization;

namespace ManagedCode.LlmTck.Decisions;

internal static class LlmTckDecisionFixtureValidation
{
    private const double _probabilityRoundingError = 0.00005;
    private const double _floatingPointTolerance = 0.000001;

    public static bool IsConformant(LlmTckDecisionProvider provider, LlmTckDecisionQuestion question, LlmTckDecisionAnswer answer)
    {
        if (provider == LlmTckDecisionProvider.Unspecified || answer.Kind == LlmTckDecisionKind.Predicate) { return true; }
        if (answer.Kind == LlmTckDecisionKind.Box)
        { return provider == LlmTckDecisionProvider.TypeSafe && answer.Box is { IsValid: true } && answer.Probabilities.Count == 0; }
        var domain = question.Kind == LlmTckDecisionKind.Score
            ? Enumerable.Range(0, question.Options.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()
            : question.Options.ToArray();
        if (!answer.Confidence.HasValue || answer.Probabilities.Count != domain.Length
            || !domain.All(answer.Probabilities.ContainsKey)) { return false; }
        var tolerance = provider is LlmTckDecisionProvider.Kev or LlmTckDecisionProvider.Cloudflare
            ? _probabilityRoundingError * domain.Length + _floatingPointTolerance : _floatingPointTolerance;
        if (Math.Abs(answer.Probabilities.Values.Sum() - 1) > tolerance) { return false; }
        if (provider != LlmTckDecisionProvider.OpenAI && answer.Kind == LlmTckDecisionKind.Choice && answer.Probabilities[answer.Choice!] + _floatingPointTolerance < answer.Probabilities.Values.Max()) { return false; }
        return provider != LlmTckDecisionProvider.Cloudflare
            || Math.Abs(answer.Confidence.Value - answer.Probabilities.Values.Max()) <= _probabilityRoundingError + _floatingPointTolerance;
    }
}
