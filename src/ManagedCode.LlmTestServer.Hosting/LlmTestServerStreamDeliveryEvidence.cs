using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

internal sealed record LlmTestServerStreamDeliveryEvidence(LlmTestServerStreamDelivery Plan, int EmittedContentFrames, bool Applied)
{
    private const string _key = "ManagedCode.LlmTestServer.StreamDelivery";

    internal static LlmTestServerStreamDeliveryEvidence? Read(HttpContext context)
    {
        return context.Items.TryGetValue(_key, out var value) ? value as LlmTestServerStreamDeliveryEvidence : null;
    }

    internal static void Record(HttpContext context, LlmTestServerStreamDelivery plan, int frames, bool applied)
    {
        context.Items[_key] = new LlmTestServerStreamDeliveryEvidence(plan, frames, applied);
    }
}
