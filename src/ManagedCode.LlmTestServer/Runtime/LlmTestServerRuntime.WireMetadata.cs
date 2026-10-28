using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public interface ILlmTestServerWireMetadataSource
{
    LlmTestServerResponseMetadata GetResponseMetadata();
}

public sealed partial class LlmTestServerRuntime : ILlmTestServerWireMetadataSource
{
    private readonly Dictionary<string, long> _wireSequences = new(StringComparer.Ordinal);

    private LlmTestServerResponseMetadata ReserveResponseMetadata(string provider)
    {
        var sequence = checked(_wireSequences.GetValueOrDefault(provider) + 1);
        _wireSequences[provider] = sequence;
        return LlmTestServerResponseMetadata.Create(_configuration.Determinism, provider, sequence);
    }

    public LlmTestServerResponseMetadata GetResponseMetadata()
    {
        lock (_gate) { return _activeRequest.Value?.WireMetadata ?? ReserveResponseMetadata(string.Empty); }
    }

    public async Task<LlmTestServerChatResult> CompleteChatAsync(LlmTestServerChatRequest request, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(request.ModelId);
        var metadata = GetResponseMetadata();
        var result = await CompleteChatCoreAsync(request, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerEmbeddingResult> CreateEmbeddingAsync(string modelId, IReadOnlyList<string> inputs, string? bearerToken = null, IReadOnlyList<int?>? outputDimensions = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await CreateEmbeddingCoreAsync(modelId, inputs, bearerToken, outputDimensions, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerImageResult> GenerateImageAsync(string modelId, string prompt, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await GenerateImageCoreAsync(modelId, prompt, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerAudioResult> GenerateAudioAsync(string modelId, string input, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await GenerateAudioCoreAsync(modelId, input, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerTranscriptionResult> TranscribeAudioAsync(string modelId, string fileName, string? prompt = null, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await TranscribeAudioCoreAsync(modelId, fileName, prompt, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerTranscriptionResult> TranslateAudioAsync(string modelId, string fileName, string? prompt = null, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await TranslateAudioCoreAsync(modelId, fileName, prompt, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }

    public async Task<LlmTestServerVideoResult> GenerateVideoAsync(string modelId, string prompt, string? bearerToken = null, CancellationToken cancellationToken = default)
    {
        using var operation = _activeRequest.Value is null ? BeginRequest("runtime-" + Guid.NewGuid().ToString("N")) : null;
        _activeRequest.Value?.ModelObserved?.Invoke(modelId);
        var metadata = GetResponseMetadata();
        var result = await GenerateVideoCoreAsync(modelId, prompt, bearerToken, cancellationToken).ConfigureAwait(false);
        return result with { WireMetadata = result.WireMetadata ?? metadata };
    }
}
