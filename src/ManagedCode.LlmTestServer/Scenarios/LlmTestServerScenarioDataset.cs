namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerScenarioDataset
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public List<LlmTestServerScenario> ChatScenarios { get; init; } = [];
}
