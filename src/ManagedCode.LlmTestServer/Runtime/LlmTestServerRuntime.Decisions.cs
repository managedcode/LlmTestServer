using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    public Task<LlmTestServerDecisionResult> DecideAsync(LlmTestServerDecisionRequest request,
        string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        ArgumentNullException.ThrowIfNull(request.Questions);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return Task.FromResult(DecideCore(request, bearerToken));
        }
    }

    private LlmTestServerDecisionResult DecideCore(LlmTestServerDecisionRequest request, string? bearerToken)
    {
        if (IsActiveRequestSuperseded())
        {
            return DecisionFailure(request, 409, _requestSupersededCode, _requestSupersededMessage);
        }
        if (!HasRequiredToken(_configuration.RequiredBearerToken, bearerToken))
        {
            return DecisionFailure(request, 401, "invalid_api_key", "The bearer token did not match.", kind: LlmTestServerEventKind.AuthFailed);
        }
        if (!IsConfiguredModel(request.ModelId, LlmTestServerModelKind.Decision))
        {
            return DecisionFailure(request, 404, "model_not_found", "The decision model is not configured.", kind: LlmTestServerEventKind.ModelNotFound);
        }
        var fault = TryCreateFault(request.ModelId, [request.Input], JsonSerializer.Serialize(request));
        if (fault is not null)
        {
            return new() { ModelId = request.ModelId, StatusCode = fault.StatusCode, ErrorCode = fault.Code, ErrorMessage = fault.Message };
        }
        var scenario = _configuration.DecisionScenarios.Find(candidate =>
            candidate.ModelId == request.ModelId && (candidate.ExpectedInput is null || candidate.ExpectedInput == request.Input));
        if (scenario is null)
        {
            return DecisionFailure(request, 404, "llm_test_server_scenario_not_found", "No decision scenario matched.", kind: LlmTestServerEventKind.Unmatched);
        }
        if (scenario.Error is { } error)
        {
            return DecisionFailure(request, error.StatusCode, error.Code, error.Message, scenario.Id);
        }
        if (!DecisionAnswersMatch(request, scenario))
        {
            return DecisionFailure(request, 409, "llm_test_server_fixture_mismatch", "Configured decision answers do not match the requested questions.", scenario.Id);
        }
        var answers = scenario.Answers.ToDictionary(entry => entry.Key,
            entry => entry.Value with { Probabilities = new(entry.Value.Probabilities) }, StringComparer.Ordinal);
        AddEvent(LlmTestServerEventKind.Matched, scenario.Id, request.ModelId, "Decision scenario matched.",
            JsonSerializer.Serialize(request), JsonSerializer.Serialize(answers), scenario.Usage);
        return new() { IsSuccess = true, ModelId = scenario.ResolvedModelId ?? request.ModelId, ScenarioId = scenario.Id, Answers = answers, Usage = scenario.Usage, Metadata = scenario.Metadata };
    }

    private static bool DecisionAnswersMatch(LlmTestServerDecisionRequest request, LlmTestServerDecisionScenario scenario)
    {
        return request.Questions.Count == scenario.Answers.Count
            && request.Questions.Select(question => question.Name).Distinct(StringComparer.Ordinal).Count() == request.Questions.Count
            && request.Questions.All(question => scenario.Answers.TryGetValue(question.Name, out var answer)
                && answer.Kind == question.Kind && (answer.Refused || (DecisionAnswerFitsQuestion(question, answer) && (scenario.AllowMalformedResponse || LlmTestServerDecisionFixtureValidation.IsConformant(request.Provider, question, answer)))));
    }

    private static bool DecisionAnswerFitsQuestion(LlmTestServerDecisionQuestion question, LlmTestServerDecisionAnswer answer)
    {
        if (question.Kind == LlmTestServerDecisionKind.Choice)
        { return question.Options.Contains(answer.Choice!, StringComparer.Ordinal) && answer.Probabilities.Keys.All(key => question.Options.Contains(key, StringComparer.Ordinal)); }
        if (question.Kind == LlmTestServerDecisionKind.Score)
        { return answer.Score <= question.Options.Count - 1 && answer.Probabilities.Keys.All(key => int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < question.Options.Count); }
        return true;
    }

    private LlmTestServerDecisionResult DecisionFailure(LlmTestServerDecisionRequest request, int status, string code,
        string message, string? scenario = null, LlmTestServerEventKind kind = LlmTestServerEventKind.ErrorReturned)
    {
        AddEvent(kind, scenario, request.ModelId, message, JsonSerializer.Serialize(request), message);
        return new() { ModelId = request.ModelId, ScenarioId = scenario, StatusCode = status, ErrorCode = code, ErrorMessage = message };
    }
}
