using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Conversations;

public sealed class LlmTestServerConversationBuilder(string id)
{
    private string _modelId = LlmTestServerKnownModelIds.Gpt41Mini;
    private string? _token;
    private LlmTestServerConversation _conversation = new();

    public LlmTestServerConversationBuilder ForModel(string modelId) { ArgumentException.ThrowIfNullOrWhiteSpace(modelId); _modelId = modelId; return this; }
    public LlmTestServerConversationBuilder RequireBearerToken(string token) { ArgumentException.ThrowIfNullOrWhiteSpace(token); _token = token; return this; }
    public LlmTestServerConversationBuilder StartAt(string stepId) { ArgumentException.ThrowIfNullOrWhiteSpace(stepId); _conversation = _conversation with { StartStepId = stepId }; return this; }
    public LlmTestServerConversationBuilder WithMaxSteps(int maximum) { _conversation = _conversation with { MaxSteps = maximum }; return this; }
    public LlmTestServerConversationBuilder ReplaySuccessfulRequests(bool enabled = true) { _conversation = _conversation with { ReplaySuccessfulRequests = enabled }; return this; }
    public LlmTestServerConversationBuilder OnExhaustion(LlmTestServerConversationExhaustion policy) { _conversation = _conversation with { Exhaustion = policy }; return this; }

    public LlmTestServerConversationBuilder Step(string stepId, Action<LlmTestServerConversationStepBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var step = new LlmTestServerConversationStepBuilder(stepId);
        configure(step);
        _conversation.Steps.Add(step.Build());
        return this;
    }

    public LlmTestServerScenario Build()
    {
        var graph = _conversation with { StartStepId = _conversation.StartStepId.Length > 0 ? _conversation.StartStepId : _conversation.Steps.FirstOrDefault()?.Id ?? string.Empty };
        return LlmTestServerConfigurationBuilder.SnapshotScenario(new LlmTestServerScenario
        { Id = id, ModelId = _modelId, RequiredBearerToken = _token, Conversation = graph });
    }
}
