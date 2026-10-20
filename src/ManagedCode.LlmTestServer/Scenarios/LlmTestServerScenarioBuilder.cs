namespace ManagedCode.LlmTestServer.Scenarios;

public sealed class LlmTestServerScenarioBuilder(string id)
{
    private LlmTestServerScenario _scenario = new() { Id = id };

    public LlmTestServerScenarioBuilder ForModel(string modelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        _scenario = _scenario with { ModelId = modelId };
        return this;
    }

    public LlmTestServerScenarioBuilder RequireBearerToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _scenario = _scenario with { RequiredBearerToken = token };
        return this;
    }

    public LlmTestServerScenarioBuilder WhenUserContains(string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        _scenario.Match.Messages.Add(new LlmTestServerMessage { Role = "user", Content = content });
        return this;
    }

    public LlmTestServerScenarioBuilder WithExactMatch(params LlmTestServerMessage[] messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        _scenario.Match.Messages.Clear();
        _scenario.Match.Messages.AddRange(messages);
        _scenario = _scenario with
        {
            Match = _scenario.Match with { Mode = LlmTestServerMatchMode.Exact },
        };
        return this;
    }

    public LlmTestServerScenarioBuilder Responds(string content, params string[] streamChunks)
    {
        ArgumentNullException.ThrowIfNull(content);
        _scenario.Responses.Add(
            new LlmTestServerScenarioResponse
            {
                Content = content,
                StreamChunks = streamChunks.Length > 0 ? [.. streamChunks] : [content],
            }
        );
        return this;
    }

    public LlmTestServerScenarioBuilder CallsTool(string name, string argumentsJson, string callId = "call_fixture")
    {
        return CallsTools(new LlmTestServerToolCall { Id = callId, Name = name, ArgumentsJson = argumentsJson });
    }

    public LlmTestServerScenarioBuilder CallsTools(params LlmTestServerToolCall[] calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        if (calls.Length == 0)
        {
            throw new ArgumentException("At least one tool call is required.", nameof(calls));
        }

        foreach (var call in calls)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(call.Id);
            LlmTestServerJsonSchema.ValidateJson(call.ArgumentsJson);
        }
        _scenario.Responses.Add(new() { ToolCalls = [.. calls] });
        return this;
    }

    public LlmTestServerScenarioBuilder RespondsJson(string json)
    {
        LlmTestServerJsonSchema.ValidateJson(json);
        return Responds(json);
    }

    public LlmTestServerScenarioBuilder RetryAfter(int seconds)
    {
        if (seconds is < 0 or > 86400) { throw new ArgumentOutOfRangeException(nameof(seconds)); }
        if (_scenario.Responses.LastOrDefault()?.Error is not { } error) { throw new InvalidOperationException("RetryAfter requires a preceding scripted error."); }
        var response = _scenario.Responses[^1];
        _scenario.Responses[^1] = response with { Error = error with { RetryAfterSeconds = seconds } };
        return this;
    }

    public LlmTestServerScenarioBuilder Fails(int statusCode, string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _scenario.Responses.Add(
            new LlmTestServerScenarioResponse
            {
                Error = new LlmTestServerScenarioError
                {
                    StatusCode = statusCode,
                    Code = code,
                    Message = message,
                },
            }
        );
        return this;
    }

    public LlmTestServerScenarioBuilder DelaysBy(int milliseconds)
    {
        if (milliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(milliseconds), "Delay cannot be negative.");
        }

        if (_scenario.Responses.Count == 0)
        {
            _scenario.Responses.Add(new LlmTestServerScenarioResponse());
        }

        var index = _scenario.Responses.Count - 1;
        _scenario.Responses[index] = _scenario.Responses[index] with
        {
            DelayMilliseconds = milliseconds,
        };
        return this;
    }

    public LlmTestServerScenario Build()
    {
        if (_scenario.Responses.Count == 0)
        {
            throw new InvalidOperationException("A scenario requires at least one response.");
        }

        return _scenario with
        {
            Match = _scenario.Match with { Messages = _scenario.Match.Messages.Select(message => message with { ToolCalls = [.. message.ToolCalls] }).ToList() },
            Responses =
            [
                .. _scenario.Responses.Select(response => response with
                {
                    StreamChunks = [.. response.StreamChunks],
                    ToolCalls = [.. response.ToolCalls],
                }),
            ],
        };
    }
}
