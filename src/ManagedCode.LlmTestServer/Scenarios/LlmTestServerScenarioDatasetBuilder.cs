using ManagedCode.LlmTestServer.Configuration;

namespace ManagedCode.LlmTestServer.Scenarios;

public sealed class LlmTestServerScenarioDatasetBuilder(string id)
{
    private readonly LlmTestServerScenarioDataset _dataset = new() { Id = id };

    public LlmTestServerScenarioDatasetBuilder AddChatScenario(
        string scenarioId,
        Action<LlmTestServerScenarioBuilder> configure
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioId);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new LlmTestServerScenarioBuilder(scenarioId);
        configure(builder);
        var scenario = builder.Build();

        _dataset.ChatScenarios.RemoveAll(existing =>
            string.Equals(existing.Id, scenarioId, StringComparison.OrdinalIgnoreCase)
        );
        _dataset.ChatScenarios.Add(scenario);

        return this;
    }

    public LlmTestServerScenarioDataset Build()
    {
        return _dataset with
        {
            ChatScenarios =
            [
                .. _dataset.ChatScenarios.Select(LlmTestServerConfigurationBuilder.SnapshotScenario),
            ],
        };
    }
}
