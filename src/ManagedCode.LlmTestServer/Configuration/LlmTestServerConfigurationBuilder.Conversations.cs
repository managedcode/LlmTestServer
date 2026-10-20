using ManagedCode.LlmTestServer.Conversations;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Configuration;

public sealed partial class LlmTestServerConfigurationBuilder
{
    public LlmTestServerConfigurationBuilder AddConversation(string id, Action<LlmTestServerConversationBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new LlmTestServerConversationBuilder(id);
        configure(builder);
        var scenario = builder.Build();
        _configuration.ChatScenarios.RemoveAll(existing => string.Equals(existing.Id, id, StringComparison.OrdinalIgnoreCase));
        _configuration.ChatScenarios.Add(scenario);
        return this;
    }

    public LlmTestServerConfigurationBuilder WithConversationCapacity(int maximumConversations, long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumConversations);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);
        _configuration = _configuration with { MaxConversations = maximumConversations, MaxConversationBytes = maximumBytes };
        return this;
    }

    private static LlmTestServerConversation? SnapshotConversation(LlmTestServerScenario scenario)
    {
        var graph = scenario.Conversation;
        if (graph is null) { return null; }
        ValidateIdentifier(scenario.Id);
        ArgumentNullException.ThrowIfNull(graph.Steps);
        if (scenario.Responses.Count != 0 || graph.Steps.Count is < 1 or > 256 || graph.MaxSteps is < 1 or > 4096
            || !Enum.IsDefined(graph.Exhaustion)) { throw new ArgumentException("Invalid conversation shape or limits.", nameof(scenario)); }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in graph.Steps)
        {
            ArgumentNullException.ThrowIfNull(step);
            ValidateIdentifier(step.Id);
            if (!ids.Add(step.Id)) { throw new ArgumentException("Duplicate conversation step ID.", nameof(scenario)); }
        }
        if (!ids.Contains(graph.StartStepId)) { throw new ArgumentException("Unknown conversation start step.", nameof(scenario)); }
        return graph with
        {
            Steps = graph.Steps.Select(step =>
            {
                ArgumentNullException.ThrowIfNull(step.Match);
                ArgumentNullException.ThrowIfNull(step.Match.JsonFields);
                ArgumentNullException.ThrowIfNull(step.NextSteps);
                if (step.Terminal == (step.NextSteps.Count > 0) || step.NextSteps.Any(next => !ids.Contains(next))
                    || step.NextSteps.Distinct(StringComparer.Ordinal).Count() != step.NextSteps.Count)
                { throw new ArgumentException("A step must terminate or transition to distinct known steps.", nameof(scenario)); }
                if (step.Match.JsonFields.Count > 32) { throw new ArgumentException("A conversation step supports at most 32 JSON predicates.", nameof(scenario)); }
                var match = step.Match;
                if (match.TurnIndex is < 1 || match.LatestRole is not (null or "user" or "assistant" or "tool" or "system" or "developer"))
                { throw new ArgumentException("Invalid conversation match predicate.", nameof(scenario)); }
                if (match.ToolName is not null) { ValidateIdentifier(match.ToolName); }
                if (match.ToolCallId is not null) { ValidateIdentifier(match.ToolCallId); }
                if (match.ToolArgumentsJson is not null) { LlmTestServerJsonSchema.ValidateJson(match.ToolArgumentsJson); }
                foreach (var field in match.JsonFields)
                {
                    ArgumentNullException.ThrowIfNull(field);
                    LlmTestServerJsonFieldSelector.Parse(field.Path);
                    LlmTestServerJsonSchema.ValidateJson(field.EqualsJson);
                }
                return step with
                {
                    Response = SnapshotResponse(step.Response),
                    NextSteps = [.. step.NextSteps],
                    Match = match with { JsonFields = [.. match.JsonFields.Select(field => field with { })] }
                };
            }).ToList(),
        };
    }

    private static void ValidateIdentifier(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 128) { throw new ArgumentException("Conversation identifiers are limited to 128 characters.", nameof(value)); }
    }
}
