using ManagedCode.LlmTestServer.Gemini;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static async Task<IResult> CreateGeminiBatchEmbeddingsAsync(string model, HttpContext context,
        ILlmTestServerRuntime runtime, CancellationToken cancellationToken)
    {
        var read = await ReadJsonAsync<GeminiBatchEmbedContentsRequest>(context, GeminiInvalidRequest, cancellationToken).ConfigureAwait(false);
        if (read.Error is not null) { return read.Error; }
        if (read.Value?.Requests is not { Count: > 0 and <= 128 } requests)
        { return GeminiInvalidRequest("A batch requires between 1 and 128 embedding requests."); }
        var inputs = new List<string>();
        foreach (var request in requests)
        {
            if (request is null || request.Model != "models/" + model)
            { return GeminiInvalidRequest("Every embedding request model must match the batch path model."); }
            var validation = ValidateGeminiEmbeddingRequest(model, new() { Model = request.Model, Content = request.Content });
            if (validation is not null) { return validation; }
            if (request.Content.Parts.Count > 128 || request.Content.Parts.Any(part => part.Text is null
                || part.FunctionCall is not null || part.FunctionResponse is not null || part.InlineData is not null
                || part.InlineDataSnakeCase is not null || part.FileData is not null || part.FileDataSnakeCase is not null))
            { return GeminiInvalidRequest("This embedding fixture supports only text parts."); }
            if (request.OutputDimensionality is < 128 or > 3072)
            { return GeminiInvalidRequest("The supported reduced embedding dimensionality is between 128 and 3072."); }
            inputs.Add(GeminiWireMapper.ReadText(request.Content));
        }
        var result = await runtime.CreateEmbeddingAsync(model, inputs, ReadAccessToken(context), requests.Select(request => request.OutputDimensionality).ToArray(), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        { return Results.Json(GeminiWireMapper.ToError(result.StatusCode, result.ErrorMessage!), statusCode: result.StatusCode); }
        return Results.Json(new GeminiBatchEmbedContentsResponse
        {
            Embeddings = result.Vectors.Select(vector => new GeminiContentEmbedding { Values = vector }).ToList(),
            UsageMetadata = new() { PromptTokenCount = inputs.Sum(LlmTestServerTokenCounter.CountTextTokens) },
        });
    }
}
