using System.Globalization;

namespace ManagedCode.LlmTestServer.Cloudflare;

public static class CloudflareDecisionErrors
{
    public const int InvalidData = 5004;
    public const int IncompleteRequest = 3003;
    public const int RequestTooLarge = 3006;
    public const int Timeout = 3007;
    public const int Aborted = 3008;
    public const int AccountLimited = 3036;
    public const int Capacity = 3040;
    public const int InvalidModel = 3042;
    public const int AccountBlocked = 3023;
    public const int Authentication = 10000;

    public static CloudflareDecisionResponse Create(int status, string message, string? configuredCode = null)
    {
        return new()
        {
            Errors = [new() { Code = NumericCode(status, configuredCode), Message = message }],
        };
    }

    private static int NumericCode(int status, string? configuredCode)
    {
        if (int.TryParse(configuredCode, NumberStyles.None, CultureInfo.InvariantCulture, out var native) && native > 0) { return native; }
        return status switch
        {
            400 => InvalidData,
            401 => Authentication,
            403 => AccountBlocked,
            404 => InvalidModel,
            408 => Timeout,
            413 => RequestTooLarge,
            429 => Capacity,
            _ => status,
        };
    }
}
