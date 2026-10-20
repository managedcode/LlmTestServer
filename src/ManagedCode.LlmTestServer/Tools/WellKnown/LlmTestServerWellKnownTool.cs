using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Tools.WellKnown;

public sealed record LlmTestServerWellKnownToolDescriptor
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string ParametersJson { get; init; } = "{}";
    public string ResultSchemaJson { get; init; } = "{}";
    public string ErrorSchemaJson { get; init; } = "{}";
}

/// <summary>Scripted function-tool data consumed by existing scenario and conversation APIs.</summary>
public sealed record LlmTestServerWellKnownToolFixture
{
    public required LlmTestServerToolDefinition Definition { get; init; }
    public required LlmTestServerToolCall Call { get; init; }
    public required LlmTestServerMessage Result { get; init; }
    public bool IsError { get; init; }
    public int DelayMilliseconds { get; init; }
}

/// <summary>Defines typed fixture contracts. It does not execute the represented operation.</summary>
public sealed class LlmTestServerWellKnownTool<TArguments, TResult>
    where TArguments : class
    where TResult : class
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal LlmTestServerWellKnownTool(LlmTestServerWellKnownToolDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public LlmTestServerWellKnownToolDescriptor Descriptor { get; }

    public LlmTestServerToolDefinition Definition => new()
    {
        Name = Descriptor.Name,
        Description = Descriptor.Description,
        ParametersJson = Descriptor.ParametersJson,
    };

    public LlmTestServerToolCall CreateCall(TArguments arguments, string? callId = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return new()
        {
            Id = ValidateCallId(callId ?? "call_" + Descriptor.Name),
            Name = Descriptor.Name,
            ArgumentsJson = Serialize(arguments, Descriptor.ParametersJson),
        };
    }

    /// <summary>Serializes a successful result as its typed JSON root, without an envelope.</summary>
    public LlmTestServerMessage CreateResult(TResult result, string? callId = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new()
        {
            Role = "tool",
            ToolCallId = ValidateCallId(callId ?? "call_" + Descriptor.Name),
            Content = Serialize(result, Descriptor.ResultSchemaJson),
        };
    }

    /// <summary>Serializes the explicitly selected {"error":{"code","message","retryable"}} fixture envelope.</summary>
    public LlmTestServerMessage CreateError(LlmTestServerToolFixtureError error, string? callId = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new()
        {
            Role = "tool",
            ToolCallId = ValidateCallId(callId ?? "call_" + Descriptor.Name),
            Content = Serialize(new LlmTestServerToolFixtureErrorResult { Error = error }, Descriptor.ErrorSchemaJson),
        };
    }

    public LlmTestServerWellKnownToolFixture Success(TArguments arguments, TResult result, string? callId = null, int delayMilliseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);
        return new() { Definition = Definition, Call = CreateCall(arguments, callId), Result = CreateResult(result, callId), DelayMilliseconds = delayMilliseconds };
    }

    public LlmTestServerWellKnownToolFixture Failure(TArguments arguments, LlmTestServerToolFixtureError error, string? callId = null, int delayMilliseconds = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(delayMilliseconds);
        return new() { Definition = Definition, Call = CreateCall(arguments, callId), Result = CreateError(error, callId), IsError = true, DelayMilliseconds = delayMilliseconds };
    }

    private static string Serialize<T>(T value, string schema)
    {
        var json = JsonSerializer.Serialize(value, _json);
        if (!LlmTestServerJsonSchema.Matches(json, schema))
        { throw new ArgumentException("The typed fixture does not satisfy its well-known tool schema.", nameof(value)); }
        return json;
    }

    private static string ValidateCallId(string callId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(callId);
        if (callId.Length > 128) { throw new ArgumentException("Tool call IDs are limited to 128 characters.", nameof(callId)); }
        return callId;
    }
}
