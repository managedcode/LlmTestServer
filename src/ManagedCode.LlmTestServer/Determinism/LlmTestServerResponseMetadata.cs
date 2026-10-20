using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ManagedCode.LlmTestServer.Determinism;

public sealed record LlmTestServerDeterminism
{
    public string Seed { get; init; } = string.Empty;
    public DateTimeOffset FixedUtc { get; init; }
}

public sealed record LlmTestServerResponseMetadata
{
    public string Identity { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ClockMode { get; init; } = "live";
    public long Sequence { get; init; }

    public string IdFor(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Identity + ":" + domain)))[..32];
    }

    public static LlmTestServerResponseMetadata Live()
    {
        return new();
    }

    internal static LlmTestServerResponseMetadata Create(LlmTestServerDeterminism? settings, string provider, long sequence)
    {
        return settings is null ? new() { Sequence = sequence } : new()
        {
            Identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                settings.Seed + "\n" + provider + "\n" + sequence.ToString(CultureInfo.InvariantCulture)))),
            CreatedAt = settings.FixedUtc.ToUniversalTime(),
            ClockMode = "fixed",
            Sequence = sequence,
        };
    }
}
