using Amazon.BedrockRuntime;
using Amazon.BedrockRuntime.Model;
using Amazon.Runtime;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Bedrock;

public sealed class BedrockSdkCompatibilityTests
{
    [Test]
    public async Task ConverseStream_OfficialSdkReadsTextAndUsageAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
            .AddModel("amazon.nova-lite-v1:0", LlmTestServerModelKind.Chat)
            .AddChatScenario("aws-sdk", scenario => scenario.ForModel("amazon.nova-lite-v1:0")
                .WhenUserContains("whale").Responds("blue whale", "blue ", "whale")));
        using var http = new HttpClient(new CanonicalUriHandler(host.GetTestServer().CreateHandler()))
        {
            BaseAddress = host.GetTestServer().BaseAddress,
        };
        using var sdk = new AmazonBedrockRuntimeClient(new BasicAWSCredentials("test-access-key", "test-secret-key"),
            new AmazonBedrockRuntimeConfig
            {
                ServiceURL = new Uri(http.BaseAddress!, LlmTestServerProviderRouteNamespaces.Bedrock).ToString(),
                AuthenticationRegion = "us-east-1",
                HttpClientFactory = new TestHttpClientFactory(http),
                MaxErrorRetry = 0,
            });
        using var response = await sdk.ConverseStreamAsync(new ConverseStreamRequest
        {
            ModelId = "amazon.nova-lite-v1:0",
            Messages = [new Message { Role = ConversationRole.User, Content = [new ContentBlock { Text = "whale" }] }],
        });
        var chunks = new List<string>();
        var tokens = 0;
        var stopReason = string.Empty;
        response.Stream.ContentBlockDeltaReceived += (_, args) => chunks.Add(args.EventStreamEvent.Delta.Text);
        response.Stream.MetadataReceived += (_, args) => tokens = args.EventStreamEvent.Usage.OutputTokens ?? 0;
        response.Stream.MessageStopReceived += (_, args) => stopReason = args.EventStreamEvent.StopReason;
        await response.Stream.StartProcessingAsync();
        await Assert.That(string.Concat(chunks)).IsEqualTo("blue whale");
        await Assert.That(tokens).IsEqualTo(2);
        await Assert.That(stopReason).IsEqualTo("end_turn");
    }

    // TestHost calls Uri.GetComponents, which rejects the SDK's non-canonicalizing URI.
    private sealed class CanonicalUriHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(request.RequestUri!.AbsoluteUri);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class TestHttpClientFactory(HttpClient client) : HttpClientFactory
    {
        public override HttpClient CreateHttpClient(IClientConfig clientConfig)
        {
            return client;
        }
    }
}
