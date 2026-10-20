using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Cloudflare;

public static class CloudflareDecisionMapper
{
    public static SystemOneDecisionRequest ToSystemOne(CloudflareDecisionRequest request)
    {
        return new()
        {
            Model = request.Model.Trim(),
            State = request.State,
            Questions = request.Questions,
        };
    }

    public static LlmTestServerDecisionRequest ToRequest(CloudflareDecisionRequest request)
    {
        var native = SystemOneDecisionMapper.ToRequest(ToSystemOne(request), LlmTestServerDecisionProvider.Cloudflare);
        return request.Images is null ? native : native with
        {
            Input = JsonSerializer.Serialize(new CloudflareDecisionInput { State = request.State, Images = request.Images }),
        };
    }

    private sealed record CloudflareDecisionInput
    {
        [JsonPropertyName("state")] public required JsonElement State { get; init; }
        [JsonPropertyName("images")] public required List<CloudflareDecisionImage> Images { get; init; }
    }
}
