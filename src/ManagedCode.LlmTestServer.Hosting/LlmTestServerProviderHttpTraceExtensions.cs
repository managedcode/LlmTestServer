using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ManagedCode.LlmTestServer.Hosting;

public static class LlmTestServerProviderHttpTraceExtensions
{
    private const string _requestIdItemKey = "ManagedCode.LlmTestServer.Hosting.ProviderHttpTrace.RequestId";
    private const int _absoluteMaxPreviewBytes = 4 * 1024 * 1024;
    private const int _minimumMaxRetainedBytes = 512;
    private const int _absoluteMaxRetainedBytes = 256 * 1024 * 1024;

    public static IServiceCollection AddLlmTestServerProviderHttpTracing(
        this IServiceCollection services,
        Action<LlmTestServerProviderHttpTraceOptions>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services
            .AddOptions<LlmTestServerProviderHttpTraceOptions>()
            .Validate(value => value.MaxEntries > 0, "MaxEntries must be greater than zero.")
            .Validate(
                value => value.MaxPreviewBytes is > 0 and <= _absoluteMaxPreviewBytes,
                $"MaxPreviewBytes must be between 1 and {_absoluteMaxPreviewBytes}."
            )
            .Validate(
                value =>
                    value.MaxRetainedBytes
                        is >= _minimumMaxRetainedBytes and <= _absoluteMaxRetainedBytes,
                $"MaxRetainedBytes must be between {_minimumMaxRetainedBytes} and {_absoluteMaxRetainedBytes}."
            )
            .Validate(
                value => value.MaxPreviewBytes <= value.MaxRetainedBytes,
                "MaxPreviewBytes must not exceed MaxRetainedBytes."
            )
            .Validate(
                value => !string.IsNullOrWhiteSpace(value.RequestIdHeaderName),
                "RequestIdHeaderName must not be empty."
            );

        if (configure is not null)
        {
            options.Configure(configure);
        }

        services.TryAddSingleton<ILlmTestServerProviderHttpTraceStore, LlmTestServerProviderHttpTraceStore>();
        services.TryAddTransient<LlmTestServerProviderHttpTraceFilter>();
        return services;
    }

    public static RouteGroupBuilder AddLlmTestServerProviderHttpTracing(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group.AddEndpointFilterFactory(
            static (_, next) => async invocationContext =>
            {
                LlmTestServerProviderHttpTraceFilter filter;
                try
                {
                    filter = invocationContext
                        .HttpContext
                        .RequestServices
                        .GetRequiredService<LlmTestServerProviderHttpTraceFilter>();
                }
                catch (Exception exception)
                {
                    TryLogFilterResolutionFailure(invocationContext.HttpContext, exception);
                    return await next(invocationContext).ConfigureAwait(false);
                }

                return await filter
                    .InvokeAsync(invocationContext, next)
                    .ConfigureAwait(false);
            }
        );
        return group;
    }

    public static string? GetLlmTestServerProviderHttpTraceId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(_requestIdItemKey, out var value) ? value as string : null;
    }

    public static RouteHandlerBuilder WithLlmTestServerProviderOperation(
        this RouteHandlerBuilder builder,
        string operationId
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        return builder.WithMetadata(new LlmTestServerProviderOperationMetadata(operationId));
    }

    internal static void SetLlmTestServerProviderHttpTraceId(HttpContext context, string requestId)
    {
        context.Items[_requestIdItemKey] = requestId;
    }

    private static void TryLogFilterResolutionFailure(
        HttpContext context,
        Exception exception
    )
    {
        try
        {
            context
                .RequestServices
                .GetService<ILoggerFactory>()
                ?.CreateLogger(typeof(LlmTestServerProviderHttpTraceFilter).FullName!)
                .LogWarning(
                    exception,
                    "LLM Test Server provider HTTP tracing could not be resolved; provider behavior continues unchanged."
                );
        }
        catch (Exception)
        {
        }
    }
}

public sealed record LlmTestServerProviderOperationMetadata(string OperationId);

public sealed record LlmTestServerProviderFallbackMetadata;
