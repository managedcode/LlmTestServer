using System.Globalization;
using System.Text.Json;
using ManagedCode.LlmTestServer.Cloudflare;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Providers;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.SystemOne;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Hosting;

public static partial class LlmTestServerEndpointRouteBuilderExtensions
{
    private const string _clefModel = "clef";
    private const string _clefFlashModel = "clef-flash";
    private const string _systemOnePath = "/v1/systemone";
    private const string _decisionsPath = "/v1/decisions";
    private const string _clefPath = "/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef";
    private const string _clefFlashPath = "/client/v4/accounts/{accountId}/ai/run/@cf/cloudflare/clef-flash";
    private const string _typeSafeRequestIdHeader = "x-typesafe-request-id";

    private static void MapDecisionEndpoints(RouteGroupBuilder endpoints)
    {
        endpoints.MapPost(ProviderRoutes.ForProvider(ProviderRoutes.SystemOne, _systemOnePath), CreateSystemOneDecisionAsync)
            .WithLlmTestServerProviderOperation(LlmTestServerProviderOperationIds.SystemOne.DecisionsCreate);
        endpoints.MapPost(ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, _decisionsPath), CreateOpenAiDecisionAsync)
            .WithLlmTestServerProviderOperation(LlmTestServerProviderOperationIds.OpenAI.DecisionsCreate);
        endpoints.MapPost(ProviderRoutes.ForProvider(ProviderRoutes.Cloudflare, _clefPath), CreateCloudflareDecisionAsync)
            .WithLlmTestServerProviderOperation(LlmTestServerProviderOperationIds.Cloudflare.ClefDecisionsCreate);
        endpoints.MapPost(ProviderRoutes.ForProvider(ProviderRoutes.Cloudflare, _clefFlashPath), CreateCloudflareDecisionAsync)
            .WithLlmTestServerProviderOperation(LlmTestServerProviderOperationIds.Cloudflare.ClefFlashDecisionsCreate);
    }

    private static async Task<IResult> CreateOpenAiDecisionAsync(HttpContext context, ILlmTestServerRuntime runtime, CancellationToken cancellationToken)
    {
        var read = await ReadJsonAsync<OpenAiDecisionRequest>(context, cancellationToken, OpenAiDecisionMapper.Validate).ConfigureAwait(false);
        if (read.Error is not null) { return read.Error; }
        if (read.Value is null) { return InvalidRequest("Missing decision body."); }
        var result = await runtime.DecideAsync(OpenAiDecisionMapper.ToRequest(read.Value), ReadAccessToken(context), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Json(OpenAiDecisionMapper.ToResponse(read.Value, result))
            : Results.Json(OpenAiWireMapper.ToError(result.ErrorCode!, result.ErrorMessage!), statusCode: result.StatusCode);
    }

    private static async Task<IResult> CreateSystemOneDecisionAsync(HttpContext context, ILlmTestServerRuntime runtime, CancellationToken cancellationToken)
    {
        SetSystemOneRequestId(context);
        var provider = LlmTestServerDecisionProvider.TypeSafe;
        var typeSafeValidation = new TypeSafeValidationResult();
        JsonReadResult<SystemOneDecisionRequest> read;
        if (context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) == true)
        {
            var multipart = await TypeSafeMultipartRequestReader.ReadAsync(context.Request, cancellationToken).ConfigureAwait(false);
            if (multipart.Error is { } error) { return Results.Json(new { detail = error.Error }, statusCode: error.StatusCode); }
            var body = JsonSerializer.SerializeToElement(multipart.Request);
            typeSafeValidation = TypeSafeRequestValidation.Validate(body,
                context.Request.Headers[TypeSafeRequestValidation.ImagesHeader] == TypeSafeRequestValidation.Enabled, multipart: true);
            read = new(multipart.Request, null);
        }
        else
        {
            context.Request.EnableBuffering();
            read = await ReadJsonAsync<SystemOneDecisionRequest>(context, SystemOneValidationError, cancellationToken, body =>
        {
            provider = ResolveSystemOneProvider(body, runtime);
            if (provider == LlmTestServerDecisionProvider.TypeSafe)
            {
                typeSafeValidation = (context.Request.ContentLength ?? context.Request.Body.Length) > TypeSafeRequestValidation.MaxRequestBytes
                    ? new TypeSafeValidationResult("TypeSafe request body exceeds 32 MiB.", StatusCodes.Status413PayloadTooLarge)
                    : TypeSafeRequestValidation.Validate(body,
                    context.Request.Headers[TypeSafeRequestValidation.ImagesHeader] == TypeSafeRequestValidation.Enabled);
                return new LlmTestServerRequestValidationResult(typeSafeValidation.Error);
            }
            return SystemOneDecisionMapper.Validate(body, provider);
        }).ConfigureAwait(false);
        }
        if (typeSafeValidation.Error is not null)
        {
            return typeSafeValidation.StatusCode == StatusCodes.Status422UnprocessableEntity
            ? SystemOneValidationError(typeSafeValidation.Error)
            : Results.Json(new { detail = typeSafeValidation.Error }, statusCode: typeSafeValidation.StatusCode);
        }
        if (read.Error is not null) { return read.Error; }
        if (read.Value is null) { return SystemOneValidationError("Missing decision body."); }
        var result = await runtime.DecideAsync(SystemOneDecisionMapper.ToRequest(read.Value, provider), ReadAccessToken(context), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            if (provider == LlmTestServerDecisionProvider.Kev && result.StatusCode == StatusCodes.Status401Unauthorized)
            { context.Response.Headers.WWWAuthenticate = "Bearer"; }
            return Results.Json(new { detail = result.ErrorMessage }, statusCode: result.StatusCode);
        }
        if (result.Answers.Values.Any(answer => answer.Refused))
        { return Results.Json(new { detail = "SystemOne does not support refusal answer fixtures." }, statusCode: StatusCodes.Status409Conflict); }
        if (provider == LlmTestServerDecisionProvider.Kev)
        { context.Response.Headers["server-timing"] = "app;dur=" + Math.Round(result.Metadata.LatencyMilliseconds, 1).ToString("F1", CultureInfo.InvariantCulture); }
        return Results.Json(SystemOneDecisionMapper.ToResponse(read.Value, result, provider == LlmTestServerDecisionProvider.Kev, provider), SystemOneNativeJson.Options);
    }

    private static LlmTestServerDecisionProvider ResolveSystemOneProvider(JsonElement body, ILlmTestServerRuntime runtime)
    {
        var modelId = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String
            ? model.GetString() : SystemOneDecisionMapper.KevDefaultModel;
        var configured = runtime.GetModels().FirstOrDefault(item => item.Id == modelId);
        return configured?.DecisionProvider == LlmTestServerDecisionProvider.Kev ? LlmTestServerDecisionProvider.Kev : LlmTestServerDecisionProvider.TypeSafe;
    }

    private static void SetSystemOneRequestId(HttpContext context)
    {
        var supplied = context.Request.Headers[_typeSafeRequestIdHeader].ToString();
        context.Response.Headers[_typeSafeRequestIdHeader] = string.IsNullOrEmpty(supplied) ? Guid.NewGuid().ToString("N") : supplied;
    }

    private static IResult SystemOneValidationError(string message)
    {
        return Results.Json(new { detail = new[] { new { loc = new object[] { "body" }, msg = message, type = "value_error" } } }, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<IResult> CreateCloudflareDecisionAsync(HttpContext context, ILlmTestServerRuntime runtime, CancellationToken cancellationToken)
    {
        if (!await CloudflareDecisionRequestBody.FitsLimitAsync(context.Request, cancellationToken).ConfigureAwait(false))
        { return CloudflareDecisionFailure(StatusCodes.Status413PayloadTooLarge, "Workers AI request exceeds the native body limit.", "3006"); }
        var validation = new CloudflareDecisionValidationResult();
        var read = await ReadJsonAsync<CloudflareDecisionRequest>(context,
            message => Results.Json(CloudflareDecisionErrors.Create(validation.StatusCode, message, validation.ErrorCode.ToString(CultureInfo.InvariantCulture)), statusCode: validation.StatusCode),
            cancellationToken, body =>
            {
                validation = CloudflareDecisionValidation.ValidateNative(body);
                return new LlmTestServerRequestValidationResult(validation.Error);
            }).ConfigureAwait(false);
        if (read.Error is not null) { return read.Error; }
        if (read.Value is null) { return CloudflareDecisionFailure(StatusCodes.Status400BadRequest, "Missing decision body."); }
        var native = CloudflareDecisionMapper.ToSystemOne(read.Value);
        var expected = context.Request.Path.Value!.EndsWith(_clefFlashModel, StringComparison.Ordinal) ? _clefFlashModel : _clefModel;
        if (native.Model != expected) { return CloudflareDecisionFailure(StatusCodes.Status400BadRequest, "Body model must match the Workers AI route."); }
        var result = await runtime.DecideAsync(CloudflareDecisionMapper.ToRequest(read.Value) with { Provider = LlmTestServerDecisionProvider.Cloudflare }, ReadAccessToken(context), cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess) { return CloudflareDecisionFailure(result.StatusCode, result.ErrorMessage!, result.ErrorCode); }
        if (result.Answers.Values.Any(answer => answer.Refused)) { return CloudflareDecisionFailure(StatusCodes.Status409Conflict, "SystemOne does not support refusal answer fixtures."); }
        return Results.Json(new CloudflareDecisionResponse
        {
            Success = true,
            Result = SystemOneDecisionMapper.ToResponse(native, result, true, LlmTestServerDecisionProvider.Cloudflare)
        });
    }

    private static IResult CloudflareDecisionFailure(int status, string message, string? code = null)
    {
        return Results.Json(CloudflareDecisionErrors.Create(status, message, code), statusCode: status);
    }
}
