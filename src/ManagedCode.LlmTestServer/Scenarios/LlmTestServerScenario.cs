using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerScenario
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public string ModelId { get; init; } = LlmTestServerKnownModelIds.Gpt41Mini;

    public string? RequiredBearerToken { get; init; }

    public LlmTestServerRequestMatch Match { get; init; } = new();

    public ManagedCode.LlmTestServer.Conversations.LlmTestServerConversation? Conversation { get; init; }

    public List<LlmTestServerScenarioResponse> Responses { get; init; } = [];
}
