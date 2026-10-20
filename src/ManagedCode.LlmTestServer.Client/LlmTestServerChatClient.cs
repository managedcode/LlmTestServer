using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.OpenAI;
using Microsoft.Extensions.AI;
using ProviderRoutes = ManagedCode.LlmTestServer.Providers.LlmTestServerProviderRouteNamespaces;

namespace ManagedCode.LlmTestServer.Client;

public sealed class LlmTestServerChatClient(
    HttpClient httpClient,
    string defaultModelId = LlmTestServerKnownModelIds.Gpt41Mini,
    string? bearerToken = null
) : IChatClient
{
    private const string _refusalErrorCode = nameof(OpenAiChatMessage.Refusal);
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public ChatClientMetadata Metadata { get; } =
        new(nameof(LlmTestServerChatClient), httpClient.BaseAddress, defaultModelId);

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = CreateRequest(messages, options, stream: false);
        using var httpRequest = CreateJsonRequest(request);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var completion = await response
            .Content
            .ReadFromJsonAsync<OpenAiChatCompletionResponse>(_jsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var text = completion?.Choices.FirstOrDefault()?.Message.TextContent ?? string.Empty;
        var message = completion?.Choices.FirstOrDefault()?.Message.Refusal is null
            ? new ChatMessage(ChatRole.Assistant, text) : new ChatMessage(ChatRole.Assistant, new List<AIContent>());
        AddToolCalls(message.Contents, completion?.Choices.FirstOrDefault()?.Message.ToolCalls);
        if (completion?.Choices.FirstOrDefault()?.Message.Refusal is { } refusal)
        { message.Contents.Add(new ErrorContent(refusal) { ErrorCode = _refusalErrorCode }); }
        return new ChatResponse(message)
        {
            ModelId = completion?.Model,
            CreatedAt = completion is null ? null : DateTimeOffset.FromUnixTimeSeconds(completion.Created),
            ResponseId = completion?.Id,
            FinishReason = MapFinishReason(completion?.Choices.FirstOrDefault()?.FinishReason),
            Usage = completion is null ? null : LlmTestServerUsageMapper.Map(completion.Usage),
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(messages);

        var request = CreateRequest(messages, options, stream: true);
        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/chat/completions")
        )
        {
            Content = new StringContent(
                JsonSerializer.Serialize(request, _jsonOptions),
                Encoding.UTF8,
                "application/json"
            ),
        };
        ApplyBearerToken(httpRequest);

        using var response = await httpClient
            .SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response
            .Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        StringBuilder? refusal = null;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (!line.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payload = line["data: ".Length..];
            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
            {
                break;
            }

            var chunk = JsonSerializer.Deserialize<OpenAiChatCompletionChunk>(payload, _jsonOptions);
            var choice = chunk?.Choices.FirstOrDefault();
            var text = choice?.Delta.TextContent;
            if (choice?.Delta.Refusal is { } refusalDelta) { (refusal ??= new()).Append(refusalDelta); }
            if (chunk is not null && (!string.IsNullOrEmpty(text) || choice?.FinishReason is not null || choice?.Delta.ToolCalls is { Count: > 0 } || chunk.Usage is not null))
            {
                var update = new ChatResponseUpdate
                {
                    Role = ChatRole.Assistant,
                    ModelId = chunk.Model,
                    ResponseId = chunk.Id,
                    CreatedAt = DateTimeOffset.FromUnixTimeSeconds(chunk.Created),
                    FinishReason = MapFinishReason(choice?.FinishReason),
                };
                if (!string.IsNullOrEmpty(text)) { update.Contents.Add(new TextContent(text)); }
                AddToolCalls(update.Contents, choice?.Delta.ToolCalls);
                if (chunk.Usage is not null)
                {
                    update.Contents.Add(new UsageContent(LlmTestServerUsageMapper.Map(chunk.Usage)));
                }

                yield return update;
            }
        }
        if (refusal is not null)
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, [new ErrorContent(refusal.ToString()) { ErrorCode = _refusalErrorCode }]);
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private static ChatFinishReason? MapFinishReason(string? reason)
    {
        return reason is null ? null : new ChatFinishReason(reason);
    }

    private OpenAiChatCompletionRequest CreateRequest(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        bool stream
    )
    {

        return new()
        {
            Model = options?.ModelId ?? defaultModelId,
            Stream = stream,
            StreamOptions = stream ? new() { IncludeUsage = true } : null,
            ParallelToolCalls = options?.AllowMultipleToolCalls ?? true,
            Tools = options?.Tools?.Select(tool => tool is AIFunctionDeclaration function
                ? new OpenAiTool { Function = new() { Name = function.Name, Description = function.Description, Parameters = function.JsonSchema } }
                : throw new NotSupportedException("Only function tools are supported.")).ToList() ?? [],
            ToolChoice = options?.ToolMode switch
            {
                NoneChatToolMode => JsonSerializer.SerializeToElement("none"),
                RequiredChatToolMode { RequiredFunctionName: { } name } => JsonSerializer.SerializeToElement(new { type = "function", function = new { name } }),
                RequiredChatToolMode => JsonSerializer.SerializeToElement("required"),
                _ => JsonSerializer.SerializeToElement("auto"),
            },
            ResponseFormat = options?.ResponseFormat is ChatResponseFormatJson json
                ? new() { Type = json.Schema is null ? "json_object" : "json_schema", JsonSchema = json.Schema is { } schema ? new() { Name = json.SchemaName ?? "response", Schema = schema, Strict = true } : null } : null,
            Messages = messages.SelectMany(ToWireMessages).ToList(),
        };
    }

    private static IEnumerable<OpenAiChatMessage> ToWireMessages(ChatMessage message)
    {
        var calls = message.Contents.OfType<FunctionCallContent>().Select(call => new OpenAiToolCall
        {
            Id = call.CallId,
            Function = new() { Name = call.Name, Arguments = JsonSerializer.Serialize(call.Arguments ?? new Dictionary<string, object?>()) },
        }).ToList();
        var refusals = message.Contents.OfType<ErrorContent>().Where(error => error.ErrorCode == _refusalErrorCode).ToArray();
        if (calls.Count > 0 || message.Contents.OfType<TextContent>().Any() || refusals.Length > 0)
        {
            yield return OpenAiChatMessage.FromText(message.Role.Value, message.Text) with
            {
                Content = refusals.Length > 0 && message.Text.Length == 0 ? JsonSerializer.SerializeToElement((string?)null) : JsonSerializer.SerializeToElement(message.Text),
                Refusal = refusals.Length > 0 ? string.Concat(refusals.Select(error => error.Message)) : null,
                ToolCalls = calls.Count > 0 ? calls : null,
            };
        }

        foreach (var result in message.Contents.OfType<FunctionResultContent>())
        {
            yield return OpenAiChatMessage.FromText("tool", result.Result is string text ? text : JsonSerializer.Serialize(result.Result)) with { ToolCallId = result.CallId };
        }
    }

    private static void AddToolCalls(IList<AIContent> contents, List<OpenAiToolCall>? calls)
    {
        if (calls is null)
        {
            return;
        }

        foreach (var call in calls)
        {
            contents.Add(new FunctionCallContent(call.Id, call.Function.Name, JsonSerializer.Deserialize<Dictionary<string, object?>>(call.Function.Arguments)));
        }
    }

    private HttpRequestMessage CreateJsonRequest(OpenAiChatCompletionRequest request)
    {
        var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            ProviderRoutes.ForProvider(ProviderRoutes.OpenAI, "/v1/chat/completions")
        )
        {
            Content = JsonContent.Create(request, options: _jsonOptions),
        };
        ApplyBearerToken(httpRequest);
        return httpRequest;
    }

    private void ApplyBearerToken(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }
}
