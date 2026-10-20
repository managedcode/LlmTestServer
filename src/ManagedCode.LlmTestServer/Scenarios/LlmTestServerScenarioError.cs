namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerScenarioError
{
    public int StatusCode { get; init; } = 500;

    public string Code { get; init; } = "llm_test_server_error";

    public string Message { get; init; } = "Scripted LLM Test Server error.";
}
