namespace ManagedCode.LlmTestServer.Cloudflare;

public sealed record CloudflareDecisionValidationResult(
    string? Error = null, int StatusCode = 400, int ErrorCode = CloudflareDecisionErrors.InvalidData)
{
    public bool IsValid => Error is null;
}
