using System.Text.Json;
using ManagedCode.LlmTestServer.Anthropic;
using ManagedCode.LlmTestServer.Bedrock;
using ManagedCode.LlmTestServer.Cohere;
using ManagedCode.LlmTestServer.Gemini;
using ManagedCode.LlmTestServer.Ollama;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static IResult? ValidateAnthropicVersion(HttpContext context)
    {
        var version = context.Request.Headers[AnthropicWireMapper.VersionHeaderName].ToString();
        return string.Equals(version, AnthropicWireMapper.SupportedVersion, StringComparison.Ordinal)
            ? null
            : AnthropicInvalidRequest(context,
                $"The {AnthropicWireMapper.VersionHeaderName} header must be {AnthropicWireMapper.SupportedVersion}."
            );
    }

    private static IResult? ValidateAnthropicMessageRequest(AnthropicMessagesRequest request, HttpContext context)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return AnthropicInvalidRequest(context, "Missing message model.");
        }

        if (request.MaxTokens is null or < 0)
        {
            return AnthropicInvalidRequest(context, "Missing or invalid max_tokens.");
        }

        if (request.Messages is null || request.Messages.Count == 0)
        {
            return AnthropicInvalidRequest(context, "At least one message is required.");
        }

        return request.Messages.Any(message =>
                message is null
                || message.Role is not ("user" or "assistant")
                || message.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            )
            ? AnthropicInvalidRequest(context, "Every message requires a user or assistant role and content.")
            : null;
    }

    private static IResult? ValidateOllamaChatRequest(OllamaChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return OllamaInvalidRequest("Missing chat model.");
        }

        if (request.Messages is null || request.Messages.Count == 0)
        {
            return OllamaInvalidRequest("At least one chat message is required.");
        }

        return request.Messages.Any(message =>
                message is null
                || string.IsNullOrWhiteSpace(message.Role)
                || message.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            )
            ? OllamaInvalidRequest("Every chat message requires a role and content.")
            : null;
    }

    private static IResult? ValidateOllamaEmbeddingRequest(OllamaEmbedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return OllamaInvalidRequest("Missing embedding model.");
        }

        if (request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return OllamaInvalidRequest("Missing embedding input.");
        }

        return OllamaWireMapper.ReadEmbeddingInputs(request).Count == 0
            ? OllamaInvalidRequest("Missing embedding input.")
            : null;
    }

    private static IResult? ValidateCohereChatRequest(CohereChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return CohereInvalidRequest("Missing chat model.");
        }

        if (request.Messages is null || request.Messages.Count == 0)
        {
            return CohereInvalidRequest("At least one chat message is required.");
        }

        return request.Messages.Any(message =>
                message is null
                || string.IsNullOrWhiteSpace(message.Role)
                || message.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            )
            ? CohereInvalidRequest("Every chat message requires a role and content.")
            : null;
    }

    private static IResult? ValidateCohereEmbeddingRequest(CohereEmbedRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return CohereInvalidRequest("Missing embedding model.");
        }

        if (string.IsNullOrWhiteSpace(request.InputType))
        {
            return CohereInvalidRequest("Missing embedding input_type.");
        }

        return CohereWireMapper.ReadEmbeddingInputs(request).Count == 0
            ? CohereInvalidRequest("Missing embedding texts or inputs.")
            : null;
    }

    private static IResult? ValidateGeminiGenerateContentRequest(
        string model,
        GeminiGenerateContentRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return GeminiInvalidRequest("Missing model path parameter.");
        }

        if (request.Contents is null || request.Contents.Count == 0)
        {
            return GeminiInvalidRequest("At least one content item is required.");
        }

        if (request.SystemInstruction is { } instruction && (instruction.Parts is null || instruction.Parts.Any(part => part is null)))
        {
            return GeminiInvalidRequest("System instruction requires valid parts.");
        }

        if (request.Contents.Any(content => content is null || content.Parts is null || content.Parts.Count == 0 || content.Parts.Any(part => part is null)))
        { return GeminiInvalidRequest("Every content item requires at least one part."); }
        foreach (var part in request.Contents.SelectMany(content => content.Parts))
        {
            if (part.ThoughtSignature is { } signature && (signature.Length == 0 || signature.Length > 16384
                || signature.Any(char.IsWhiteSpace) || !Convert.TryFromBase64String(signature, new byte[signature.Length], out var written) || written == 0))
            { return GeminiInvalidRequest("Thought signatures require bounded, nonempty base64."); }
        }
        if (GeminiWireMapper.RequiresToolContinuationTokens(model))
        {
            var start = request.Contents.FindLastIndex(content => content.Role == "user" && content.Parts.Any(part =>
                part.Text is not null || part.InlineData is not null || part.InlineDataSnakeCase is not null || part.FileData is not null || part.FileDataSnakeCase is not null));
            foreach (var content in request.Contents.Skip(Math.Max(0, start + 1)).Where(content => content.Role == "model"))
            {
                var firstCall = content.Parts.FirstOrDefault(part => part.FunctionCall is not null);
                if (firstCall is not null && firstCall.ThoughtSignature is null)
                { return GeminiInvalidRequest("The first function call in each current-turn step requires its original thought signature."); }
            }
        }
        return null;
    }

    private static IResult? ValidateGeminiEmbeddingRequest(
        string model,
        GeminiEmbedContentRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return GeminiInvalidRequest("Missing model path parameter.");
        }

        if (request.Content?.Parts is not { Count: > 0 } || request.Content.Parts.Any(part => part is null))
        {
            return GeminiInvalidRequest("Embedding content requires at least one part.");
        }

        return string.IsNullOrWhiteSpace(GeminiWireMapper.ReadText(request.Content))
            ? GeminiInvalidRequest("Embedding content requires text.")
            : null;
    }

    private static IResult? ValidateGeminiPredictLongRunningRequest(
        string model,
        GeminiPredictLongRunningRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return GeminiInvalidRequest("Missing model path parameter.");
        }

        if (request.Instances is null || request.Instances.Count == 0 || request.Instances.Any(instance => instance.ValueKind != JsonValueKind.Object))
        {
            return GeminiInvalidRequest("At least one prediction instance is required.");
        }

        return string.IsNullOrWhiteSpace(GeminiWireMapper.ReadPredictPrompt(request))
            ? GeminiInvalidRequest("Video prediction instances require a prompt.")
            : null;
    }

    private static IResult? ValidateBedrockConverseRequest(
        string modelId,
        BedrockConverseRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return BedrockInvalidRequest("Missing modelId path parameter.");
        }

        if (request.Messages is null || request.Messages.Count == 0)
        {
            return BedrockInvalidRequest("At least one message is required.");
        }

        if (request.System is null || request.System.Any(content => content is null))
        {
            return BedrockInvalidRequest("System content must be an array of content blocks.");
        }

        return request.Messages.Any(message =>
                message is null
                || message.Role is not ("user" or "assistant")
                || message.Content is null || message.Content.Count == 0 || message.Content.Any(content => content is null)
            )
            ? BedrockInvalidRequest("Every message requires a user or assistant role and content.")
            : null;
    }

    private static IResult? ValidateBedrockInvokeRequest(string modelId, JsonElement request)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return BedrockInvalidRequest("Missing modelId path parameter.");
        }

        return string.IsNullOrWhiteSpace(BedrockWireMapper.ReadInvokeInput(request))
            ? BedrockInvalidRequest("Missing invoke input.")
            : null;
    }

    private static IResult? ValidateChatRequest(OpenAiChatCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing chat completion model.");
        }

        if (request.Messages is null || request.Messages.Count == 0)
        {
            return InvalidRequest("At least one chat message is required.");
        }

        return request.Messages.Any(message =>
                message is null
                || string.IsNullOrWhiteSpace(message.Role)
                || (message.Content.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null && message.ToolCalls is not { Count: > 0 } && message.RefusalTextContent is null)
            )
            ? InvalidRequest("Every chat message requires a role and content.")
            : null;
    }

    private static IResult? ValidateResponseRequest(OpenAiResponseRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing response model.");
        }

        return request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? InvalidRequest("Missing response input.")
            : null;
    }

    private static IResult? ValidateEmbeddingRequest(OpenAiEmbeddingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing embedding model.");
        }

        return request.Input.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? InvalidRequest("Missing embedding input.")
            : null;
    }

    private static IResult? ValidateImageRequest(OpenAiImageGenerationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing image model.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return InvalidRequest("Missing image prompt.");
        }

        if (!IsNullOrAllowed(request.ResponseFormat, _openAiImageResponseFormats))
        {
            return InvalidRequest("Unsupported image response_format.");
        }

        if (!IsNullOrAllowed(request.OutputFormat, _openAiImageOutputFormats))
        {
            return InvalidRequest("Unsupported image output_format.");
        }

        if (!IsNullOrAllowed(request.Quality, _openAiImageQualities))
        {
            return InvalidRequest("Unsupported image quality.");
        }

        if (!IsNullOrAllowed(request.Background, _openAiImageBackgrounds))
        {
            return InvalidRequest("Unsupported image background.");
        }

        if (!IsNullOrAllowed(request.Size, _openAiImageSizes))
        {
            return InvalidRequest("Unsupported image size.");
        }

        return request.PartialImages is null or >= 0 and <= 3
            ? null
            : InvalidRequest("Image partial_images must be between 0 and 3.");
    }

    private static IResult? ValidateImageEditRequest(OpenAiImageEditRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing image edit model.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return InvalidRequest("Missing image edit prompt.");
        }

        var hasJsonImage = request.Image.ValueKind is not JsonValueKind.Undefined and not JsonValueKind.Null;
        if (request.Images.Count == 0 && !hasJsonImage)
        {
            return InvalidRequest("Image edits require at least one image.");
        }

        if (!IsNullOrAllowed(request.OutputFormat, _openAiImageOutputFormats))
        {
            return InvalidRequest("Unsupported image edit output_format.");
        }

        if (!IsNullOrAllowed(request.Quality, _openAiImageQualities))
        {
            return InvalidRequest("Unsupported image edit quality.");
        }

        if (!IsNullOrAllowed(request.Background, _openAiImageBackgrounds))
        {
            return InvalidRequest("Unsupported image edit background.");
        }

        if (!IsNullOrAllowed(request.Size, _openAiImageSizes))
        {
            return InvalidRequest("Unsupported image edit size.");
        }

        return request.PartialImages is null or >= 0 and <= 3
            ? null
            : InvalidRequest("Image edit partial_images must be between 0 and 3.");
    }

    private static IResult? ValidateImageVariationRequest(OpenAiImageVariationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing image variation model.");
        }

        if (request.Count is < 1 or > 10)
        {
            return InvalidRequest("Image variation n must be between 1 and 10.");
        }

        if (!IsNullOrAllowed(request.ResponseFormat, _openAiImageResponseFormats))
        {
            return InvalidRequest("Unsupported image variation response_format.");
        }

        return IsNullOrAllowed(request.Size, _openAiImageVariationSizes)
            ? null
            : InvalidRequest("Unsupported image variation size.");
    }

    private static IResult? ValidateAudioRequest(
        OpenAiAudioSpeechRequest request,
        string[] allowedResponseFormats
    )
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing audio model.");
        }

        if (string.IsNullOrWhiteSpace(request.Input))
        {
            return InvalidRequest("Missing audio input.");
        }

        if (string.IsNullOrWhiteSpace(request.Voice))
        {
            return InvalidRequest("Missing audio voice.");
        }

        return string.IsNullOrWhiteSpace(request.ResponseFormat)
            || IsSupportedAudioResponseFormat(request.ResponseFormat, allowedResponseFormats)
            ? null
            : InvalidRequest("Unsupported audio response_format.");
    }

    private static IResult ToOpenAiVideoError(LlmTestServerVideoResult result)
    {
        return Results.Json(
            OpenAiWireMapper.ToError(result.ErrorCode!, result.ErrorMessage!),
            statusCode: result.StatusCode
        );
    }

    private static IResult ToGeminiVideoError(LlmTestServerVideoResult result)
    {
        return Results.Json(
            GeminiWireMapper.ToError(result.StatusCode, result.ErrorMessage!),
            statusCode: result.StatusCode
        );
    }

    private static string CreateGeminiFileUri(HttpContext context, string fileId, bool media)
    {
        var path = ProviderRoutes.ForProvider(
            ProviderRoutes.Gemini,
            $"/v1beta/files/{Uri.EscapeDataString(fileId)}"
        );
        var query = media ? "?alt=media" : string.Empty;

        return context.Request.Host.HasValue
            ? $"{context.Request.Scheme}://{context.Request.Host}{path}{query}"
            : $"{path}{query}";
    }

    private static IResult? ValidateOpenAiVideoCreateRequest(OpenAiVideoCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing video model.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return InvalidRequest("Missing video prompt.");
        }

        if (!IsAllowedOpenAiVideoSeconds(request.Seconds))
        {
            return InvalidRequest("Unsupported video seconds.");
        }

        return IsAllowedOpenAiVideoSize(request.Size)
            ? null
            : InvalidRequest("Unsupported video size.");
    }

    private static IResult? ValidateOpenAiVideoListQuery(HttpContext context)
    {
        var order = context.Request.Query["order"].ToString();
        if (!string.IsNullOrWhiteSpace(order) && order is not ("asc" or "desc"))
        {
            return InvalidRequest("Unsupported video list order.");
        }

        var limit = context.Request.Query["limit"].ToString();
        return string.IsNullOrWhiteSpace(limit)
            || (int.TryParse(limit, out var value) && value is >= 0 and <= 100)
            ? null
            : InvalidRequest("Unsupported video list limit.");
    }

    private static IResult? ValidateAzureVideoGenerationJobRequest(
        AzureVideoGenerationJobRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request.Model))
        {
            return InvalidRequest("Missing video generation model.");
        }

        if (string.IsNullOrWhiteSpace(request.Prompt))
        {
            return InvalidRequest("Missing video generation prompt.");
        }

        if (request.Width <= 0 || request.Height <= 0)
        {
            return InvalidRequest("Video generation width and height must be positive.");
        }

        if (request.NSeconds is < 1 or > 20)
        {
            return InvalidRequest("Video generation n_seconds must be between 1 and 20.");
        }

        return request.NVariants is < 1 or > 5
            ? InvalidRequest("Video generation n_variants must be between 1 and 5.")
            : null;
    }

    private static bool IsAllowedOpenAiVideoSeconds(string? seconds)
    {
        return string.IsNullOrWhiteSpace(seconds)
            || Array.Exists(
                _openAiVideoSeconds,
                value => string.Equals(value, seconds, StringComparison.Ordinal)
            );
    }

    private static bool IsAllowedOpenAiVideoExtensionSeconds(string? seconds)
    {
        return string.IsNullOrWhiteSpace(seconds)
            || Array.Exists(
                _openAiVideoExtensionSeconds,
                value => string.Equals(value, seconds, StringComparison.Ordinal)
            );
    }

    private static bool IsAllowedOpenAiVideoSize(string? size)
    {
        return string.IsNullOrWhiteSpace(size)
            || Array.Exists(
                _openAiVideoSizes,
                value => string.Equals(value, size, StringComparison.Ordinal)
            );
    }

    private static bool IsNullOrAllowed(string? value, string[] allowedValues)
    {
        return string.IsNullOrWhiteSpace(value)
            || Array.Exists(allowedValues, item => string.Equals(item, value, StringComparison.Ordinal));
    }

    private static int? TryReadInt(string value)
    {
        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static IResult InvalidRequest(string message)
    {
        return Results.Json(
            OpenAiWireMapper.ToError("invalid_request", message),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static IResult AnthropicInvalidRequest(HttpContext context, string message)
    {
        return Results.Json(
            AnthropicWireMapper.ToError("invalid_request_error", message, WireMetadata(context)),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static IResult OllamaInvalidRequest(string message)
    {
        return Results.Json(
            OllamaWireMapper.ToError(message),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static IResult CohereInvalidRequest(string message)
    {
        return Results.Json(
            CohereWireMapper.ToError(message),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static IResult GeminiInvalidRequest(string message)
    {
        return Results.Json(
            GeminiWireMapper.ToError(StatusCodes.Status400BadRequest, message),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static IResult BedrockInvalidRequest(string message)
    {
        return Results.Json(
            BedrockWireMapper.ToError(message),
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    private static string ToAnthropicErrorType(int statusCode, string code)
    {
        return statusCode switch
        {
            StatusCodes.Status401Unauthorized => "authentication_error",
            StatusCodes.Status403Forbidden => "permission_error",
            StatusCodes.Status404NotFound => "not_found_error",
            StatusCodes.Status413PayloadTooLarge => "request_too_large",
            StatusCodes.Status429TooManyRequests => "rate_limit_error",
            >= StatusCodes.Status500InternalServerError => statusCode == 529
                ? "overloaded_error"
                : "api_error",
            _ when string.Equals(code, "invalid_request", StringComparison.Ordinal) => "invalid_request_error",
            _ => "invalid_request_error",
        };
    }

    private readonly record struct JsonReadResult<T>(T? Value, IResult? Error);

    private readonly record struct FormReadResult(AudioFormRequest Value, IResult? Error);

    private readonly record struct VideoFixtureReadResult(LlmTestServerVideoResult? Value, IResult? Error);

    private readonly record struct AudioFormRequest(
        string Model,
        string FileName,
        string? Prompt,
        string ResponseFormat,
        bool Stream
    );

}
