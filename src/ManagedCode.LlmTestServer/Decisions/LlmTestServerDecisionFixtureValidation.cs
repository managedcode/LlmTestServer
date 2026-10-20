using System.Globalization;

namespace ManagedCode.LlmTestServer.Decisions;

internal static class LlmTestServerDecisionFixtureValidation
{
    private const double _probabilityRoundingError = 0.00005;
    private const double _floatingPointTolerance = 0.000001;

    public static bool IsConformant(LlmTestServerDecisionProvider provider, LlmTestServerDecisionQuestion question, LlmTestServerDecisionAnswer answer)
    {
        if (provider == LlmTestServerDecisionProvider.Unspecified || answer.Kind == LlmTestServerDecisionKind.Predicate) { return true; }
        if (answer.Kind == LlmTestServerDecisionKind.Box)
        { return provider == LlmTestServerDecisionProvider.TypeSafe && answer.Box is { IsValid: true } && answer.Probabilities.Count == 0; }
        var domain = question.Kind == LlmTestServerDecisionKind.Score
            ? Enumerable.Range(0, question.Options.Count).Select(index => index.ToString(CultureInfo.InvariantCulture)).ToArray()
            : question.Options.ToArray();
        if (!answer.Confidence.HasValue || answer.Probabilities.Count != domain.Length
            || !domain.All(answer.Probabilities.ContainsKey)) { return false; }
        var tolerance = provider is LlmTestServerDecisionProvider.Kev or LlmTestServerDecisionProvider.Cloudflare
            ? _probabilityRoundingError * domain.Length + _floatingPointTolerance : _floatingPointTolerance;
        if (Math.Abs(answer.Probabilities.Values.Sum() - 1) > tolerance) { return false; }
        if (provider != LlmTestServerDecisionProvider.OpenAI && answer.Kind == LlmTestServerDecisionKind.Choice && answer.Probabilities[answer.Choice!] + _floatingPointTolerance < answer.Probabilities.Values.Max()) { return false; }
        return provider != LlmTestServerDecisionProvider.Cloudflare
            || Math.Abs(answer.Confidence.Value - answer.Probabilities.Values.Max()) <= _probabilityRoundingError + _floatingPointTolerance;
    }
}
