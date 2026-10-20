#pragma warning disable OPENAI001 // Exercise the pinned official Responses streaming SDK against the running server.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.Streaming;

public sealed partial class StreamDeliveryIntegrationTests
{
    [Test]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.EarlyCleanEnd, false, true)]
    [Arguments(LlmTestServerStreamDeliveryMode.AbortConnection, false, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, true, false)]
    [Arguments(LlmTestServerStreamDeliveryMode.DelayThenContinue, false, false)]
    public async Task OfficialResponsesSdk_TextDeliveryFaultsPreserveObservedPrefixAndRecoveryAsync(LlmTestServerStreamDeliveryMode mode, bool cancel, bool afterAllText)
    {
        await AssertProtocolDeliveryAsync(LlmTestServerKnownModelIds.Gpt41Mini, "/openai/v1/responses", "first ", mode, cancel,
            async (http, first, token, _) =>
            {
                var sdk = ResponsesSdk(http);
                var text = new StringBuilder();
                var terminal = false;
                Exception? failure = null;
                try
                {
                    await foreach (var update in sdk.CreateResponseStreamingAsync(new CreateResponseOptions
                    { Model = LlmTestServerKnownModelIds.Gpt41Mini, StreamingEnabled = true, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("stream") } }, token))
                    {
                        if (update is StreamingResponseOutputTextDeltaUpdate delta)
                        {
                            text.Append(delta.Delta);
                            if (!string.IsNullOrEmpty(delta.Delta)) { first(); }
                        }
                        if (update is StreamingResponseCompletedUpdate) { terminal = true; }
                    }
                }
                catch (Exception exception) when (ProtocolStreamException(exception)) { failure = exception; }
                return new(text.ToString(), terminal, failure);
            },
            async (http, token, _) => (await ResponsesSdk(http).CreateResponseAsync(new CreateResponseOptions
            { Model = LlmTestServerKnownModelIds.Gpt41Mini, StoredOutputEnabled = false, InputItems = { ResponseItem.CreateUserMessageItem("explicit retry") } }, token)).Value.GetOutputText(), afterAllText);
    }

    private static ResponsesClient ResponsesSdk(HttpClient http)
    {
        return new(new ApiKeyCredential("responses-delivery-key"), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0) });
    }
}
