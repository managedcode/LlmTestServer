using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Verification;

public sealed partial class VerificationIntegrationTests
{
    [Test]
    public async Task Verify_OfficialOpenAiSdkRetriesRateLimitAndProducesBoundedRetryEvidenceAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config
            .RequireBearerToken("sdk-retry-key")
            .AddChatScenario("sdk-retry", scenario => scenario
                .Fails(429, "too_many_requests", "Retry this request").RetryAfter(0)
                .Responds("retried")));
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add("X-Llm-Test-Server-Retry-Group", "official-sdk-retry");
        var sdk = new ChatClient("gpt-4.1-mini", new ApiKeyCredential("sdk-retry-key"), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "openai/v1"),
            Transport = new HttpClientPipelineTransport(http),
        });
        var completion = await sdk.CompleteChatAsync([new UserChatMessage("retry verification")]);
        await Assert.That(completion.Value.Content.Single().Text).IsEqualTo("retried");

        var control = new LlmTestServerClient(http, "sdk-retry-key");
        var result = await control.VerifyAsync(new()
        {
            NoUnexpectedCalls = true,
            Retries = [new()
            {
                GroupId = "official-sdk-retry",
                MinimumAttempts = 2,
                MaximumAttempts = 2,
                StatusSequence = [429, 200],
                MinimumRetryAfterSeconds = 0,
                RespectRetryAfter = true,
            }],
        });
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.EvidenceComplete).IsTrue();
        await Assert.That(result.Failures).IsEmpty();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "sdk-retry-key");
        var traces = (await http.GetFromJsonAsync<List<LlmTestServerProviderHttpTrace>>("/admin-api/requests"))!
            .OrderBy(trace => trace.Sequence).ToArray();
        await Assert.That(traces.Length).IsEqualTo(2);
        await Assert.That(traces.Select(trace => trace.StatusCode)).IsEquivalentTo(new int?[] { 429, 200 });
        await Assert.That(traces.All(trace => trace.RetryGroupId == "official-sdk-retry")).IsTrue();
        await Assert.That(traces[0].RetryAfter).IsEqualTo("0");
        await Assert.That(traces[1].RetryAfter).IsNull();
        foreach (var trace in traces)
        {
            await Assert.That(trace.StartedMonotonicSeconds.HasValue).IsTrue();
            await Assert.That(trace.CompletedMonotonicSeconds.HasValue).IsTrue();
            await Assert.That(double.IsFinite(trace.StartedMonotonicSeconds!.Value)).IsTrue();
            await Assert.That(double.IsFinite(trace.CompletedMonotonicSeconds!.Value)).IsTrue();
            await Assert.That(trace.CompletedMonotonicSeconds.Value).IsGreaterThanOrEqualTo(trace.StartedMonotonicSeconds.Value);
        }
        await Assert.That(traces[1].StartedMonotonicSeconds!.Value).IsGreaterThanOrEqualTo(traces[0].CompletedMonotonicSeconds!.Value);
        await Assert.That(result.MatchedRequestIds).IsEquivalentTo(traces.Select(trace => trace.RequestId).ToArray());
    }
}
