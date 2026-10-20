using Microsoft.ML.Tokenizers;

namespace ManagedCode.LlmTestServer.Runtime;

public static class LlmTestServerTokenCounter
{
    public const string DefaultTokenizerModel = "gpt-5";

    private static readonly Lazy<Tokenizer> _tokenizer = new(
        () => TiktokenTokenizer.CreateForModel(DefaultTokenizerModel)
    );

    public static int CountTextTokens(string? value)
    {
        return string.IsNullOrEmpty(value) ? 0 : _tokenizer.Value.CountTokens(value);
    }
}
