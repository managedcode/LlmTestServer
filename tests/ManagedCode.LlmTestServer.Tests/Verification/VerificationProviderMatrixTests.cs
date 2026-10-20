using System.Net;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Tests.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using ManagedCode.LlmTestServer.Verification;

namespace ManagedCode.LlmTestServer.Tests.Verification;

public sealed partial class VerificationIntegrationTests
{
    [Test]
    public async Task Verify_ProviderMatrixRetriesRecoverOnSameLogicalTurnWithIndependentConversationStateAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(config => config
            .AddConversation("matrix-retry", graph => graph
                .Step("error", step => step.WhenTurn(1).Fails(429, "too_many_requests", "Retry the same turn").RetryAfter(0).Then("recover"))
                .Step("recover", step => step.WhenTurn(1).Responds("recovered").Complete())));
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        var retries = new List<LlmTestServerRetryExpectation>();
        var observedRequestIds = new List<string>();
        var providers = ProviderFaultSimulationTests.GetProviderFaultCases();
        await Assert.That(providers.Length).IsEqualTo(13);
        for (var index = 0; index < providers.Length; index++)
        {
            var provider = providers[index];
            var groupId = $"provider-retry-{index}";
            http.DefaultRequestHeaders.Remove(LlmTestServerRequestHeaders.RetryGroup);
            http.DefaultRequestHeaders.Add(LlmTestServerRequestHeaders.RetryGroup, groupId);
            using var limited = await provider.SendAsync(http, "matrix retry");
            await Assert.That(limited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(limited.Headers.RetryAfter!.Delta).IsEqualTo(TimeSpan.Zero);
            await Assert.That(await limited.Content.ReadAsStringAsync()).Contains("Retry the same turn");
            var firstId = limited.Headers.GetValues(LlmTestServerProviderHttpTraceOptions.DefaultRequestIdHeaderName).Single();
            observedRequestIds.Add(firstId);
            var firstEvidence = await control.GetRequestsAsync(new() { RequestId = firstId });
            await Assert.That(firstEvidence.TotalMatched).IsEqualTo(1);
            await Assert.That(firstEvidence.Requests.Single().RetryGroupId).IsEqualTo(groupId);
            await Assert.That(firstEvidence.Requests.Single().StatusCode).IsEqualTo(429);
            await Assert.That(firstEvidence.Requests.Single().RetryAfter).IsEqualTo("0");

            using var recovered = await provider.SendAsync(http, "matrix retry");
            await Assert.That(recovered.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await recovered.Content.ReadAsStringAsync()).Contains("recovered");
            var secondId = recovered.Headers.GetValues(LlmTestServerProviderHttpTraceOptions.DefaultRequestIdHeaderName).Single();
            observedRequestIds.Add(secondId);
            var secondEvidence = await control.GetRequestsAsync(new() { RequestId = secondId });
            await Assert.That(secondEvidence.TotalMatched).IsEqualTo(1);
            await Assert.That(secondEvidence.Requests.Single().RetryGroupId).IsEqualTo(groupId);
            await Assert.That(secondEvidence.Requests.Single().StatusCode).IsEqualTo(200);
            retries.Add(new()
            {
                GroupId = groupId,
                MinimumAttempts = 2,
                MaximumAttempts = 2,
                StatusSequence = [429, 200],
                MinimumRetryAfterSeconds = 0,
                RespectRetryAfter = true,
            });
        }
        http.DefaultRequestHeaders.Remove(LlmTestServerRequestHeaders.RetryGroup);
        var result = await control.VerifyAsync(new() { NoUnexpectedCalls = true, Retries = retries });
        await Assert.That(result.Passed).IsTrue();
        await Assert.That(result.EvidenceComplete).IsTrue();
        await Assert.That(result.Failures).IsEmpty();
        await Assert.That(result.MatchedRequestIds.Count).IsEqualTo(26);
        await Assert.That(result.MatchedRequestIds).IsEquivalentTo(observedRequestIds);
        var journal = await control.GetRequestsAsync(new() { Order = LlmTestServerRequestOrder.Ascending });
        await Assert.That(journal.TotalMatched).IsEqualTo(26);
        await Assert.That(journal.TotalObserved).IsEqualTo(26);
        await Assert.That(journal.Requests.Select(trace => trace.ProviderNamespace).Distinct().Count()).IsEqualTo(13);
        var conversations = await control.GetConversationsAsync();
        await Assert.That(conversations.Count).IsEqualTo(13);
        await Assert.That(conversations.Select(state => state.ProviderNamespace).Distinct().Count()).IsEqualTo(13);
        foreach (var conversation in conversations)
        {
            await Assert.That(conversation.ScenarioId).IsEqualTo("matrix-retry");
            await Assert.That(conversation.ExecutedSteps).IsEqualTo(2);
            await Assert.That(conversation.LastTurnIndex).IsEqualTo(1);
            await Assert.That(conversation.IsTerminal).IsTrue();
            await Assert.That(conversation.VisitedSteps).IsEquivalentTo(new[] { "error", "recover" });
        }
    }
}
