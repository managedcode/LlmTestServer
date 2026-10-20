namespace ManagedCode.LlmTestServer.Configuration;

public sealed partial class LlmTestServerConfigurationBuilder
{
    public LlmTestServerConfigurationBuilder WithDeterminism(string seed, DateTimeOffset fixedUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seed);
        if (seed.Length > 256) { throw new ArgumentException("Deterministic seeds are limited to 256 characters.", nameof(seed)); }
        _configuration = _configuration with { Determinism = new() { Seed = seed, FixedUtc = fixedUtc.ToUniversalTime() } };
        return this;
    }

}
