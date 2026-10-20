using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public interface ILlmTestServerRuntime
{
    Task ConfigureAsync(LlmTestServerConfiguration configuration, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);

    bool IsBearerTokenAccepted(string? bearerToken);

    IReadOnlyList<LlmTestServerModel> GetModels();

    Task<LlmTestServerChatResult> CompleteChatAsync(
        LlmTestServerChatRequest request,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerEmbeddingResult> CreateEmbeddingAsync(
        string modelId,
        IReadOnlyList<string> inputs,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerImageResult> GenerateImageAsync(
        string modelId,
        string prompt,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerAudioResult> GenerateAudioAsync(
        string modelId,
        string input,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerTranscriptionResult> TranscribeAudioAsync(
        string modelId,
        string fileName,
        string? prompt = null,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerTranscriptionResult> TranslateAudioAsync(
        string modelId,
        string fileName,
        string? prompt = null,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<LlmTestServerVideoResult> GenerateVideoAsync(
        string modelId,
        string prompt,
        string? bearerToken = null,
        CancellationToken cancellationToken = default
    );

    Task<ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionResult> DecideAsync(
        ManagedCode.LlmTestServer.Decisions.LlmTestServerDecisionRequest request,
        string? bearerToken = null,
        CancellationToken cancellationToken = default);

    LlmTestServerAssertionSummary GetAssertionSummary();
}
