namespace ManagedCode.LlmTestServer.Scenarios;

public sealed record LlmTestServerToolDefinition
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string ParametersJson { get; init; } = "{\"type\":\"object\"}";
}

public sealed record LlmTestServerToolCall
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string ArgumentsJson { get; init; } = "{}";
}

public enum LlmTestServerToolChoice { Auto, None, Required }
