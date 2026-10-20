using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Diagnostics;
using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenAI;
using OpenAI.Chat;

namespace ManagedCode.LlmTestServer.Tests.Policy;

public sealed partial class OutputDispositionIntegrationTests
{
    private const string _key = "policy-sdk-key";
    private const string _text = "Visible fixture text";
    private const string _recovery = "Explicit recovery";

    private static async Task<PolicyHost> StartAsync(Action<LlmTestServerConfigurationBuilder> configure, int? traceBudgetBytes = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddLlmTestServer(configure);
        if (traceBudgetBytes is { } budget)
        {
            builder.Services.Configure<LlmTestServerProviderHttpTraceOptions>(options =>
            { options.MaxPreviewBytes = 128; options.MaxRetainedBytes = budget; });
        }
        builder.AddDefaultHealthChecks();
        var app = builder.Build();
        app.MapLlmTestServer();
        app.MapDefaultEndpoints();
        var host = new PolicyHost(app);
        try
        {
            using var startup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await app.StartAsync(startup.Token);
            using var warmup = host.Client();
            using var health = await warmup.GetAsync("/health", startup.Token);
            health.EnsureSuccessStatusCode();
            return host;
        }
        catch { await host.DisposeAsync(); throw; }
    }

    private sealed class PolicyHost(WebApplication app) : IAsyncDisposable
    {
        public HttpClient Client()
        {
            var server = app.Services.GetRequiredService<IServer>();
            var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new() { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(60) };
        }

        public async ValueTask DisposeAsync()
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await app.StopAsync(stop.Token); }
            finally { await app.DisposeAsync(); }
        }
    }

    private static ChatClient ChatSdk(HttpClient http, string provider = "openai", string model = LlmTestServerKnownModelIds.Gpt41Mini)
    {
        return new(model, new ApiKeyCredential(_key), new OpenAIClientOptions
        {
            Endpoint = new Uri(http.BaseAddress!, "/" + provider + "/v1/"),
            Transport = new HttpClientPipelineTransport(http),
            RetryPolicy = new ClientRetryPolicy(0),
        });
    }

    private static void ConfigureQueue(LlmTestServerConfigurationBuilder config, string model,
        LlmTestServerOutputDispositionKind kind, bool empty = false, bool metadata = false)
    {
        config.AddModel(model, LlmTestServerModelKind.Chat).AddChatScenario("policy", scenario => scenario.ForModel(model)
            .Responds(empty ? string.Empty : _text, empty ? [string.Empty] : ["Visible ", "fixture text"])
            .WithOutputDisposition(new() { Kind = kind, Category = metadata ? "cyber" : null, Explanation = metadata ? "Explicit fixture policy" : null })
            .Responds(_recovery));
    }

    private static async Task<LlmTestServerProviderHttpTrace> CompletedAsync(LlmTestServerClient control, int count)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        while (true)
        {
            var snapshot = await control.GetRequestsAsync(cancellationToken: deadline.Token);
            if (snapshot.Requests.Count >= count && snapshot.Requests.All(trace => trace.CompletedAt is not null))
            {
                return snapshot.Requests.OrderBy(trace => trace.Sequence).ElementAt(count - 1);
            }

            await Task.Delay(20, deadline.Token);
        }
    }

    private static async Task EvidenceAsync(LlmTestServerClient control, LlmTestServerOutputDispositionKind kind, bool streaming,
        bool metadata = false)
    {
        var trace = await CompletedAsync(control, 1);
        await Assert.That(trace.StatusCode).IsEqualTo(200);
        await Assert.That(trace.TransportState).IsEqualTo(LlmTestServerProviderHttpTraceTransportState.Completed);
        await Assert.That(trace.OutputDisposition!.Kind).IsEqualTo(kind);
        await Assert.That(trace.OutputDisposition.Category).IsEqualTo(metadata ? "cyber" : null);
        await Assert.That(trace.OutputDisposition.Explanation).IsEqualTo(metadata ? "Explicit fixture policy" : null);
        await Assert.That(trace.Usage).IsNotNull();
        await Assert.That(trace.StreamDelivery).IsNull();
        if (streaming)
        {
            await Assert.That(trace.TerminalMarkerObserved).IsTrue();
            var verification = await control.VerifyAsync(new()
            { Requests = [new() { Query = new() { RequestId = trace.RequestId }, RequireStreamingTerminalMarker = true }] });
            await Assert.That(verification.Passed).IsTrue();
        }
        var exported = await control.ExportRequestsAsync(new() { RequestId = trace.RequestId });
        await Assert.That(exported.Evidence.Requests.Single().OutputDisposition!.Kind).IsEqualTo(kind);
        var summary = await control.GetAssertionsAsync();
        await Assert.That(summary.Matched).IsEqualTo(1);
        await Assert.That(summary.ErrorsReturned).IsEqualTo(0);
        await Assert.That(summary.Events.Single().OutputDisposition!.Kind).IsEqualTo(kind);
    }

    private static async Task RecoveredAsync(LlmTestServerClient control)
    {
        var trace = await CompletedAsync(control, 2);
        await Assert.That(trace.StatusCode).IsEqualTo(200);
        await Assert.That(trace.OutputDisposition).IsNull();
        await Assert.That((await control.GetAssertionsAsync()).Matched).IsEqualTo(2);
    }
}
