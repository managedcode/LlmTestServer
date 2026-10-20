using ManagedCode.LlmTestServer.Anthropic;
using ManagedCode.LlmTestServer.AzureOpenAI;
using ManagedCode.LlmTestServer.Bedrock;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Cohere;
using ManagedCode.LlmTestServer.DeepSeek;
using ManagedCode.LlmTestServer.Foundry;
using ManagedCode.LlmTestServer.Gemini;
using ManagedCode.LlmTestServer.Groq;
using ManagedCode.LlmTestServer.Mistral;
using ManagedCode.LlmTestServer.Ollama;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.OpenRouter;
using ManagedCode.LlmTestServer.Perplexity;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.SystemOne;

namespace ManagedCode.LlmTestServer.Tests.TestSupport;

internal static class LlmTestServerProviderFixtures
{
    internal static LlmTestServerProviderProfile[] GetProfiles()
    {
        return
        [
            SystemOneCompatibility.Profile,
            CloudflareCompatibility.Profile,
            OpenAiCompatibility.Profile,
            AzureOpenAiCompatibility.Profile,
            MicrosoftFoundryCompatibility.Profile,
            AnthropicCompatibility.Profile,
            GeminiCompatibility.Profile,
            GroqCompatibility.Profile,
            MistralCompatibility.Profile,
            OllamaCompatibility.Profile,
            CohereCompatibility.Profile,
            BedrockCompatibility.Profile,
            OpenRouterCompatibility.Profile,
            DeepSeekCompatibility.Profile,
            PerplexityCompatibility.Profile,
        ];
    }

}
