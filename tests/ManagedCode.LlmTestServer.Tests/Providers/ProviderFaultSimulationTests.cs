using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Providers;

public sealed class ProviderFaultSimulationTests
{
    private const string _blockedTerm = "blocked-provider-term";
    private const string _allowedText = "provider fault simulation";
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task ProviderFamilies_ReturnConfiguredContentFilterErrorsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .SimulateContentFilter(_blockedTerm));
        using var client = host.GetTestClient();

        foreach (var provider in GetProviderFaultCases())
        {
            using var response = await provider.SendAsync(client, _blockedTerm);
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(body).Contains("content_filter");
        }
    }

    [Test]
    public async Task ProviderFamilies_ReturnConfiguredRateLimitErrorsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync(options => options
                .SimulateRateLimitAfter(0));
        using var client = host.GetTestClient();

        foreach (var provider in GetProviderFaultCases())
        {
            using var response = await provider.SendAsync(client, _allowedText);
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(body).Contains("too_many_requests");
        }
    }

    internal static ProviderFaultCase[] GetProviderFaultCases()
    {
        return
        [
            new("OpenAI", (client, content) => PostOpenAiChatAsync(client, "/openai/v1/chat/completions", content)),
            new(
                "Azure OpenAI",
                (client, content) => PostOpenAiChatAsync(
                    client,
                    "/azure-openai/openai/deployments/gpt-4.1-mini/chat/completions?api-version=2024-10-21",
                    content
                )
            ),
            new(
                "Microsoft Foundry",
                (client, content) => PostOpenAiChatAsync(client, "/microsoft-foundry/chat/completions?api-version=2024-05-01-preview", content)
            ),
            new("Anthropic", PostAnthropicMessagesAsync),
            new("Gemini", PostGeminiGenerateContentAsync),
            new("Groq", (client, content) => PostOpenAiChatAsync(client, "/groq/openai/v1/chat/completions", content)),
            new("Mistral", (client, content) => PostOpenAiChatAsync(client, "/mistral/v1/chat/completions", content)),
            new("Ollama", PostOllamaChatAsync),
            new("Cohere", PostCohereChatAsync),
            new("Bedrock", PostBedrockConverseAsync),
            new(
                "OpenRouter",
                (client, content) => PostOpenAiChatAsync(client, "/openrouter/api/v1/chat/completions", content)
            ),
            new("DeepSeek", (client, content) => PostOpenAiChatAsync(client, "/deepseek/v1/chat/completions", content)),
            new("Perplexity", (client, content) => PostOpenAiChatAsync(client, "/perplexity/v1/sonar", content)),
        ];
    }

    private static Task<HttpResponseMessage> PostOpenAiChatAsync(
        HttpClient client,
        string path,
        string content
    )
    {
        return client.PostAsJsonAsync(
            path,
            new
            {
                model = "gpt-4.1-mini",
                messages = new[] { new { role = "user", content } },
            },
            _jsonOptions
        );
    }

    private static async Task<HttpResponseMessage> PostAnthropicMessagesAsync(
        HttpClient client,
        string content
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/anthropic/v1/messages");
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(
            new
            {
                model = "gpt-4.1-mini",
                max_tokens = 256,
                messages = new[] { new { role = "user", content } },
            },
            options: _jsonOptions
        );

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    private static Task<HttpResponseMessage> PostGeminiGenerateContentAsync(
        HttpClient client,
        string content
    )
    {
        return client.PostAsJsonAsync(
            "/gemini/v1beta/models/gpt-4.1-mini:generateContent?key=test-key",
            new
            {
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = content } },
                    },
                },
            },
            _jsonOptions
        );
    }

    private static Task<HttpResponseMessage> PostOllamaChatAsync(
        HttpClient client,
        string content
    )
    {
        return client.PostAsJsonAsync(
            "/ollama/api/chat",
            new
            {
                model = "gpt-4.1-mini",
                stream = false,
                messages = new[] { new { role = "user", content } },
            },
            _jsonOptions
        );
    }

    private static Task<HttpResponseMessage> PostCohereChatAsync(
        HttpClient client,
        string content
    )
    {
        return client.PostAsJsonAsync(
            "/cohere/v2/chat",
            new
            {
                model = "gpt-4.1-mini",
                messages = new[] { new { role = "user", content } },
            },
            _jsonOptions
        );
    }

    private static Task<HttpResponseMessage> PostBedrockConverseAsync(
        HttpClient client,
        string content
    )
    {
        return client.PostAsJsonAsync(
            "/bedrock/model/gpt-4.1-mini/converse",
            new
            {
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = new[] { new { text = content } },
                    },
                },
            },
            _jsonOptions
        );
    }

    internal sealed record ProviderFaultCase(
        string ProviderName,
        Func<HttpClient, string, Task<HttpResponseMessage>> SendAsync
    );
}
