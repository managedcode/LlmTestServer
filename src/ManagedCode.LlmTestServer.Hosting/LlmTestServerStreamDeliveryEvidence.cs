using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.AspNetCore.Connections;
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
    internal static void Begin(HttpContext context, LlmTestServerStreamDelivery? plan)
    {
        if (plan is not null) { Record(context, plan, 0, false); }
    }

    internal static async Task<bool> AfterContentFrameAsync(HttpContext context, string content, CancellationToken cancellationToken)
    {
        if (content.Length == 0 || Read(context) is not { } evidence) { return true; }
        var frames = evidence.EmittedContentFrames + 1;
        var atBoundary = !evidence.Applied && frames == evidence.Plan.AfterContentFrames;
        Record(context, evidence.Plan, frames, evidence.Applied || atBoundary);
        if (!atBoundary) { return true; }
        switch (evidence.Plan.Mode)
        {
            case LlmTestServerStreamDeliveryMode.EarlyCleanEnd:
                return false;
            case LlmTestServerStreamDeliveryMode.DelayThenContinue:
                await Task.Delay(evidence.Plan.DelayMilliseconds, cancellationToken).ConfigureAwait(false);
                return true;
            case LlmTestServerStreamDeliveryMode.AbortConnection:
                context.Abort();
                throw new ConnectionAbortedException("Configured stream delivery aborted the connection after a content frame.");
            default:
                throw new InvalidOperationException("The admitted stream delivery mode is not supported.");
        }
    }

}
