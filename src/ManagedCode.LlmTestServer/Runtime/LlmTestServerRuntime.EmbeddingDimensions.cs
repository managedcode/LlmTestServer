namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private LlmTestServerEmbeddingResult? ValidateEmbeddingDimensions(string modelId, int inputCount, IReadOnlyList<int?>? dimensions)
    {
        if (dimensions is null) { return null; }
        var invalid = dimensions.Count != inputCount || dimensions.Any(dimension => dimension is < 1);
        var incompatible = dimensions.Any(dimension => dimension > _configuration.DefaultEmbeddingVector.Count);
        if (!invalid && !incompatible) { return null; }
        var message = invalid ? "Embedding dimensions must contain one positive or omitted value per input."
            : "The configured embedding vector has fewer coordinates than the requested dimensions.";
        AddEvent(LlmTestServerEventKind.ErrorReturned, null, modelId, message);
        return new()
        {
            IsSuccess = false,
            StatusCode = invalid ? 400 : 409,
            ModelId = modelId,
            ErrorCode = invalid ? "invalid_request_error" : "llm_test_server_embedding_fixture_mismatch",
            ErrorMessage = message,
        };
    }
}
