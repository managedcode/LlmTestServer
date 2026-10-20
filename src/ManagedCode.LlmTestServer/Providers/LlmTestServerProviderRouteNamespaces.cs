namespace ManagedCode.LlmTestServer.Providers;

public static class LlmTestServerProviderRouteNamespaces
{
    public const string SystemOne = "/systemone";
    public const string Cloudflare = "/cloudflare";
    public const string Anthropic = "/anthropic";
    public const string AzureOpenAI = "/azure-openai";
    public const string Bedrock = "/bedrock";
    public const string Cohere = "/cohere";
    public const string DeepSeek = "/deepseek";
    public const string Gemini = "/gemini";
    public const string Groq = "/groq";
    public const string MicrosoftFoundry = "/microsoft-foundry";
    public const string Mistral = "/mistral";
    public const string Ollama = "/ollama";
    public const string OpenAI = "/openai";
    public const string OpenRouter = "/openrouter";
    public const string Perplexity = "/perplexity";

    public static string ForProvider(string providerNamespace, string providerNativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerNamespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerNativePath);

        if (!providerNamespace.StartsWith('/'))
        {
            throw new ArgumentException("Provider namespace must start with '/'.", nameof(providerNamespace));
        }

        if (!providerNativePath.StartsWith('/'))
        {
            throw new ArgumentException("Provider native path must start with '/'.", nameof(providerNativePath));
        }

        return providerNamespace + providerNativePath;
    }
}
