using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Runtime;
using Microsoft.AspNetCore.Http;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private static async Task<IResult> CreateOpenAiVideoAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var read = await ReadOpenAiVideoCreateRequestAsync(context, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        if (read.Value is null)
        {
            return InvalidRequest("Missing video body.");
        }

        var validationError = ValidateOpenAiVideoCreateRequest(read.Value);
        if (validationError is not null)
        {
            return validationError;
        }

        var result = await runtime
            .GenerateVideoAsync(
                read.Value.Model,
                read.Value.Prompt,
                ReadAccessToken(context),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToOpenAiVideoError(result);
        }

        result = ((ILlmTestServerVideoStore)runtime).StoreVideo(ProviderRoutes.OpenAI, result with
        {
            Seconds = read.Value.Seconds ?? result.Seconds,
            Size = read.Value.Size ?? result.Size,
        });
        return result.IsSuccess ? Results.Json(OpenAiWireMapper.ToVideoResponse(result)) : ToOpenAiVideoError(result);
    }

    private static IResult ListOpenAiVideosAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var validationError = ValidateOpenAiVideoListQuery(context);
        if (validationError is not null)
        {
            return validationError;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var videos = ((ILlmTestServerVideoStore)runtime).ListVideos(ProviderRoutes.OpenAI);
        var ordered = context.Request.Query["order"] == "asc" ? videos : videos.Reverse();
        var after = context.Request.Query["after"].ToString();
        if (!string.IsNullOrEmpty(after))
        {
            ordered = ordered.SkipWhile(video => video.VideoId != after).Skip(1);
        }

        var limit = int.TryParse(context.Request.Query["limit"], out var requestedLimit) ? requestedLimit : 20;
        var page = ordered.Take(limit + 1).ToList();
        var data = page.Take(limit).Select(OpenAiWireMapper.ToVideoResponse).ToList();
        return Results.Json(new OpenAiVideoListResponse
        {
            Data = data,
            FirstId = data.FirstOrDefault()?.Id,
            LastId = data.LastOrDefault()?.Id,
            HasMore = page.Count > limit,
        });
    }

    private static async Task<IResult> GetOpenAiVideoAsync(
        string videoId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return InvalidRequest("Missing video_id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.OpenAI, videoId, false, cancellationToken).ConfigureAwait(false);
        return read.Error
            ?? Results.Json(OpenAiWireMapper.ToVideoResponse(read.Value! with { VideoId = videoId }));
    }

    private static IResult DeleteOpenAiVideoAsync(
        string videoId,
        HttpContext context,
        ILlmTestServerRuntime runtime
    )
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return InvalidRequest("Missing video_id path parameter.");
        }

        var unauthorized = AuthorizeControlRequest(context, runtime);
        return unauthorized ?? (((ILlmTestServerVideoStore)runtime).DeleteVideo(ProviderRoutes.OpenAI, videoId)
            ? Results.Json(OpenAiWireMapper.ToVideoDeleteResponse(videoId)) : VideoNotFound());
    }

    private static async Task<IResult> GetOpenAiVideoContentAsync(
        string videoId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return InvalidRequest("Missing video_id path parameter.");
        }

        var variant = context.Request.Query["variant"].ToString();
        if (
            !string.IsNullOrWhiteSpace(variant)
            && variant is not ("video" or "thumbnail" or "spritesheet")
        )
        {
            return InvalidRequest("Unsupported video content variant.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.OpenAI, videoId, false, cancellationToken).ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        return variant is "thumbnail" or "spritesheet"
            ? Results.Bytes(_defaultJpegBytes, "image/jpeg")
            : Results.Bytes(read.Value!.Bytes, read.Value.MediaType);
    }

    private static async Task<IResult> EditOpenAiVideoAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var read = await ReadJsonAsync<OpenAiVideoEditRequest>(context, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        if (read.Value is null)
        {
            return InvalidRequest("Missing video edit body.");
        }

        if (
            string.IsNullOrWhiteSpace(read.Value.Prompt)
            || string.IsNullOrWhiteSpace(read.Value.Video?.Id)
        )
        {
            return InvalidRequest("Video edits require prompt and video.id.");
        }

        return await GenerateOpenAiVideoFromDefaultModelAsync(
                context,
                runtime,
                read.Value.Prompt,
                seconds: null,
                remixedFromVideoId: read.Value.Video.Id,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IResult> ExtendOpenAiVideoAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var read = await ReadJsonAsync<OpenAiVideoExtensionRequest>(context, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        if (read.Value is null)
        {
            return InvalidRequest("Missing video extension body.");
        }

        if (
            string.IsNullOrWhiteSpace(read.Value.Prompt)
            || string.IsNullOrWhiteSpace(read.Value.Video?.Id)
        )
        {
            return InvalidRequest("Video extensions require prompt and video.id.");
        }

        if (!IsAllowedOpenAiVideoExtensionSeconds(read.Value.Seconds))
        {
            return InvalidRequest("Unsupported video extension seconds.");
        }

        return await GenerateOpenAiVideoFromDefaultModelAsync(
                context,
                runtime,
                read.Value.Prompt,
                read.Value.Seconds,
                remixedFromVideoId: read.Value.Video.Id,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IResult> RemixOpenAiVideoAsync(
        string videoId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return InvalidRequest("Missing video_id path parameter.");
        }

        var read = await ReadJsonAsync<OpenAiVideoRemixRequest>(context, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        if (read.Value is null || string.IsNullOrWhiteSpace(read.Value.Prompt))
        {
            return InvalidRequest("Video remix requires prompt.");
        }

        return await GenerateOpenAiVideoFromDefaultModelAsync(
                context,
                runtime,
                read.Value.Prompt,
                seconds: null,
                remixedFromVideoId: videoId,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IResult> CreateOpenAiVideoCharacterAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (!context.Request.HasFormContentType)
        {
            return InvalidRequest("Video character creation requires multipart/form-data.");
        }

        IFormCollection form;
        try
        {
            form = await context.Request.ReadFormAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return InvalidRequest("Malformed multipart form data.");
        }
        catch (BadHttpRequestException)
        {
            return InvalidRequest("Malformed multipart form data.");
        }

        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var name = form["name"].ToString();
        if (string.IsNullOrWhiteSpace(name) || form.Files.GetFile("video") is null)
        {
            return InvalidRequest("Video character creation requires name and video file.");
        }

        return Results.Json(
            OpenAiWireMapper.ToVideoCharacterResponse(
                "char_llm_test_server",
                name,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            )
        );
    }

    private static IResult GetOpenAiVideoCharacterAsync(
        string characterId,
        HttpContext context,
        ILlmTestServerRuntime runtime
    )
    {
        if (string.IsNullOrWhiteSpace(characterId))
        {
            return InvalidRequest("Missing character_id path parameter.");
        }

        var unauthorized = AuthorizeControlRequest(context, runtime);
        return unauthorized
            ?? Results.Json(
                OpenAiWireMapper.ToVideoCharacterResponse(
                    characterId,
                    "LLM Test Server Character",
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                )
            );
    }

    private static async Task<IResult> GenerateOpenAiVideoFromDefaultModelAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        string prompt,
        string? seconds,
        string? remixedFromVideoId,
        CancellationToken cancellationToken
    )
    {
        var source = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.OpenAI, remixedFromVideoId!, false, cancellationToken).ConfigureAwait(false);
        if (source.Error is not null)
        {
            return source.Error;
        }

        var generated = await runtime.GenerateVideoAsync(source.Value!.ModelId, prompt, ReadAccessToken(context), cancellationToken).ConfigureAwait(false);
        if (!generated.IsSuccess)
        {
            return ToOpenAiVideoError(generated);
        }

        var result = ((ILlmTestServerVideoStore)runtime).StoreVideo(ProviderRoutes.OpenAI, generated with
        {
            Seconds = seconds ?? source.Value.Seconds,
            Size = source.Value.Size,
            RemixedFromVideoId = remixedFromVideoId,
        });
        return result.IsSuccess ? Results.Json(OpenAiWireMapper.ToVideoResponse(result)) : ToOpenAiVideoError(result);
    }

    private static async Task<IResult> CreateAzureOpenAiVideoJobAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        var read = await ReadJsonAsync<AzureVideoGenerationJobRequest>(context, cancellationToken)
            .ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        if (read.Value is null)
        {
            return InvalidRequest("Missing video generation job body.");
        }

        var validationError = ValidateAzureVideoGenerationJobRequest(read.Value);
        if (validationError is not null)
        {
            return validationError;
        }

        var result = await runtime
            .GenerateVideoAsync(
                read.Value.Model,
                read.Value.Prompt,
                ReadAccessToken(context),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ToOpenAiVideoError(result);
        }

        result = ((ILlmTestServerVideoStore)runtime).StoreVideo(ProviderRoutes.AzureOpenAI, result with
        {
            Size = $"{read.Value.Width}x{read.Value.Height}",
            Seconds = $"{read.Value.NSeconds}",
        });
        return result.IsSuccess ? Results.Json(OpenAiWireMapper.ToAzureVideoGenerationJobResponse(result)) : ToOpenAiVideoError(result);
    }

    private static IResult ListAzureOpenAiVideoJobsAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var data = ((ILlmTestServerVideoStore)runtime).ListVideos(ProviderRoutes.AzureOpenAI)
            .Select(OpenAiWireMapper.ToAzureVideoGenerationJobResponse).ToList();
        return Results.Json(new AzureVideoGenerationJobListResponse
        {
            Data = data,
            FirstId = data.FirstOrDefault()?.Id,
            LastId = data.LastOrDefault()?.Id,
        });
    }

    private static async Task<IResult> GetAzureOpenAiVideoJobAsync(
        string jobId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return InvalidRequest("Missing job-id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.AzureOpenAI, jobId, false, cancellationToken).ConfigureAwait(false);
        return read.Error
            ?? Results.Json(
                OpenAiWireMapper.ToAzureVideoGenerationJobResponse(
                    read.Value! with { VideoId = jobId }
                )
            );
    }

    private static IResult DeleteAzureOpenAiVideoJobAsync(
        string jobId,
        HttpContext context,
        ILlmTestServerRuntime runtime
    )
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return InvalidRequest("Missing job-id path parameter.");
        }

        var unauthorized = AuthorizeControlRequest(context, runtime);
        return unauthorized ?? (((ILlmTestServerVideoStore)runtime).DeleteVideo(ProviderRoutes.AzureOpenAI, jobId)
            ? Results.NoContent() : VideoNotFound());
    }

    private static async Task<IResult> GetAzureOpenAiVideoGenerationAsync(
        string generationId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return InvalidRequest("Missing generation-id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.AzureOpenAI, generationId, true, cancellationToken).ConfigureAwait(false);
        return read.Error
            ?? Results.Json(
                OpenAiWireMapper.ToAzureVideoGenerationResponse(
                    read.Value! with { GenerationId = generationId }
                )
            );
    }

    private static async Task<IResult> GetAzureOpenAiVideoThumbnailAsync(
        string generationId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return InvalidRequest("Missing generation-id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.AzureOpenAI, generationId, true, cancellationToken).ConfigureAwait(false);
        return read.Error ?? Results.Bytes(_defaultJpegBytes, "image/jpg");
    }

    private static async Task<IResult> GetAzureOpenAiVideoContentAsync(
        string generationId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return InvalidRequest("Missing generation-id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.AzureOpenAI, generationId, true, cancellationToken).ConfigureAwait(false);
        return read.Error ?? Results.Bytes(read.Value!.Bytes, read.Value.MediaType);
    }

    private static async Task<IResult> HeadAzureOpenAiVideoContentAsync(
        string generationId,
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return InvalidRequest("Missing generation-id path parameter.");
        }

        var read = await ReadStoredVideoAsync(context, runtime, ProviderRoutes.AzureOpenAI, generationId, true, cancellationToken).ConfigureAwait(false);
        if (read.Error is not null)
        {
            return read.Error;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = read.Value!.MediaType;
        context.Response.ContentLength = read.Value.Bytes.Length;
        return Results.Empty;
    }

    private static Task<VideoFixtureReadResult> ReadStoredVideoAsync(
        HttpContext context, ILlmTestServerRuntime runtime, string provider, string id, bool generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var unauthorized = AuthorizeControlRequest(context, runtime);
        if (unauthorized is not null)
        {
            return Task.FromResult(new VideoFixtureReadResult(null, unauthorized));
        }

        var video = ((ILlmTestServerVideoStore)runtime).FindVideo(provider, id, generation);
        return Task.FromResult(new VideoFixtureReadResult(video, video is null ? VideoNotFound() : null));
    }

    private static IResult VideoNotFound()
    {
        return Results.Json(
        OpenAiWireMapper.ToError("video_not_found", "The requested video does not exist."), statusCode: 404);
    }

    private static async Task<VideoFixtureReadResult> GenerateDefaultVideoFixtureAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        string prompt,
        CancellationToken cancellationToken
    )
    {
        var modelId = runtime
            .GetModels()
            .FirstOrDefault(model => model.Kind == LlmTestServerModelKind.Video)
            ?.Id ?? LlmTestServerKnownModelIds.Sora2;
        var result = await runtime
            .GenerateVideoAsync(modelId, prompt, ReadAccessToken(context), cancellationToken)
            .ConfigureAwait(false);

        return result.IsSuccess
            ? new(result, null)
            : new(null, ToOpenAiVideoError(result));
    }

    private static async Task<IResult> GenerateAudioAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        return await GenerateAudioCoreAsync(
                context,
                runtime,
                modelOverride: null,
                allowedResponseFormats: _openAiSpeechResponseFormats,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<IResult> GenerateGroqAudioAsync(
        HttpContext context,
        ILlmTestServerRuntime runtime,
        CancellationToken cancellationToken
    )
    {
        return await GenerateAudioCoreAsync(
                context,
                runtime,
                modelOverride: null,
                allowedResponseFormats: _groqSpeechResponseFormats,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

}
