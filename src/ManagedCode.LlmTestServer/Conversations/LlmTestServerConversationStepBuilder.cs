using System.Text.Json;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Conversations;

public sealed class LlmTestServerConversationStepBuilder(string id)
{
    private readonly LlmTestServerScenarioBuilder _response = new(id);
    private LlmTestServerConversationMatch _match = new();
    private List<string> _next = [];
    private bool _terminal;

    public LlmTestServerConversationStepBuilder WhenLatestRole(string role) { _match = _match with { LatestRole = role }; return this; }
    public LlmTestServerConversationStepBuilder WhenLatestUserContains(string text) { return WhenLatestRole("user").WhenLatestTextContains(text); }
    public LlmTestServerConversationStepBuilder WhenLatestUserEquals(string text) { _match = _match with { LatestRole = "user", TextEquals = text }; return this; }
    public LlmTestServerConversationStepBuilder WhenLatestTextContains(string text) { _match = _match with { TextContains = text }; return this; }
    public LlmTestServerConversationStepBuilder WhenTurn(int index) { _match = _match with { TurnIndex = index }; return this; }
    public LlmTestServerConversationStepBuilder WhenToolResult(string name, string? callId = null) { _match = _match with { LatestRole = "tool", ToolName = name, ToolCallId = callId }; return this; }
    public LlmTestServerConversationStepBuilder WithToolArgumentsJson(string json) { _match = _match with { ToolArgumentsJson = json }; return this; }
    public LlmTestServerConversationStepBuilder WhenLatestJson(string path, string expectedJson) { _match.JsonFields.Add(new() { Path = path, EqualsJson = expectedJson }); return this; }
    public LlmTestServerConversationStepBuilder WhenLatestJsonValue<T>(string path, T value) { return WhenLatestJson(path, JsonSerializer.Serialize(value)); }
    public LlmTestServerConversationStepBuilder Responds(string text, params string[] chunks) { _response.Responds(text, chunks); return this; }
    public LlmTestServerConversationStepBuilder RespondsJson(string json) { _response.RespondsJson(json); return this; }
    public LlmTestServerConversationStepBuilder CallsTool(string name, string argumentsJson, string callId = "call_fixture") { _response.CallsTool(name, argumentsJson, callId); return this; }
    public LlmTestServerConversationStepBuilder CallsTools(params LlmTestServerToolCall[] calls) { _response.CallsTools(calls); return this; }
    public LlmTestServerConversationStepBuilder Fails(int status, string code, string message) { _response.Fails(status, code, message); return this; }
    public LlmTestServerConversationStepBuilder RetryAfter(int seconds) { _response.RetryAfter(seconds); return this; }
    public LlmTestServerConversationStepBuilder DelaysBy(int milliseconds) { _response.DelaysBy(milliseconds); return this; }
    public LlmTestServerConversationStepBuilder Then(params string[] stepIds) { _next = [.. stepIds]; _terminal = false; return this; }
    public LlmTestServerConversationStepBuilder Complete() { _next = []; _terminal = true; return this; }

    internal LlmTestServerConversationStep Build()
    {
        var responses = _response.Build().Responses;
        if (responses.Count != 1) { throw new ArgumentException("A conversation step requires exactly one response.", nameof(id)); }
        return new() { Id = id, Match = _match, Response = responses[0], NextSteps = _next, Terminal = _terminal };
    }
}
