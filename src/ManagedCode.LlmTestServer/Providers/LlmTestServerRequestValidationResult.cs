namespace ManagedCode.LlmTestServer.Providers;

/// <summary>Provider-neutral result of validating a provider request before mapping it.</summary>
public sealed record LlmTestServerRequestValidationResult(string? Error = null)
{
    public bool IsValid => Error is null;
}
