namespace ManagedCode.LlmTestServer.Decisions;

/// <summary>Explicit native decision fixture contracts, independently documented by their providers.</summary>
public static class LlmTestServerDecisionModelIds
{
    /// <summary>TypeSafe native alias: https://api.typesafe.ai/openapi.json .</summary>
    public const string TypeSafeJev = "jev-latest";
    /// <summary>Original Kev request default: https://github.com/jaredpalmer/kev/blob/main/kev/api.py .</summary>
    public const string Kev = "kev-latest";
    /// <summary>Cloudflare native body model: https://developers.cloudflare.com/workers-ai/models/clef/ .</summary>
    public const string CloudflareClef = "clef";
    /// <summary>Cloudflare native body model: https://developers.cloudflare.com/workers-ai/models/clef-flash/ .</summary>
    public const string CloudflareClefFlash = "clef-flash";
}
