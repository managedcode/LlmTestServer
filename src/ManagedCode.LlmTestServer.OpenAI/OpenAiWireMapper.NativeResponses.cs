using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.OpenAI;

public static partial class OpenAiWireMapper
{
    private const string _webSearchSources = "web_search_call.action.sources";

    private static JsonElement FunctionResponseChoice(JsonElement choice)
    {
        if (choice.ValueKind != JsonValueKind.Object) { return choice; }
        return choice.GetProperty("type").GetString() == "function"
            ? JsonSerializer.SerializeToElement(new { function = choice })
            : JsonSerializer.SerializeToElement("required");
    }

    private static LlmTestServerChatRequest ApplyNativeResponseRequest(OpenAiResponseRequest source,
        LlmTestServerChatRequest request, bool allowNativeTools)
    {
        var namedNative = source.ToolChoice.ValueKind == JsonValueKind.Object
            && source.ToolChoice.GetProperty("type").GetString() == "web_search";
        return request with
        {
            NativeToolContract = allowNativeTools ? LlmTestServerNativeToolContract.OpenAiResponsesWebSearch : null,
            NativeTools = source.Tools.Where(tool => tool.Type == "web_search").Select(tool =>
                new LlmTestServerNativeToolDeclaration
                {
                    Contract = LlmTestServerNativeToolContract.OpenAiResponsesWebSearch,
                    WebSearchOptions = new LlmTestServerWebSearchOptions
                    {
                        SearchContextSize = tool.SearchContextSize,
                        AllowedDomains = tool.Filters?.AllowedDomains is { } domains ? [.. domains] : [],
                        UserLocation = tool.UserLocation is { } location ? new LlmTestServerWebSearchLocation
                        {
                            Type = location.Type ?? "approximate",
                            Country = location.Country,
                            Region = location.Region,
                            City = location.City,
                            Timezone = location.Timezone
                        } : null
                    }
                }).ToList(),
            RequiredNativeTool = namedNative ? LlmTestServerNativeToolContract.OpenAiResponsesWebSearch : null,
            NativeIncludes = source.Include.Contains(_webSearchSources, StringComparer.Ordinal)
                ? [LlmTestServerNativeToolInclude.WebSearchSources] : [],
        };
    }

    private static List<OpenAiResponseOutputItem> CreateResponseOutput(LlmTestServerChatResult result,
        IReadOnlyCollection<string>? includedProperties)
    {
        var output = new List<OpenAiResponseOutputItem>();
        var includeSources = includedProperties?.Contains(_webSearchSources, StringComparer.Ordinal) == true;
        if (result.OutputItems.Count > 0)
        {
            for (var index = 0; index < result.OutputItems.Count; index++)
            {
                output.Add(MapResponseItem(result.OutputItems[index], index, result.WireMetadata, includeSources));
            }
            return output;
        }
        if (result.Content.Length > 0 || result.ToolCalls.Count == 0)
        {
            output.Add(CreateOutputMessage(result.Content, completed: true, result.WireMetadata));
        }
        output.AddRange(result.ToolCalls.Select(call => new OpenAiResponseFunctionCall
        { Id = "fc_" + call.Id, CallId = call.Id, Name = call.Name, Arguments = call.ArgumentsJson }));
        return output;
    }

    private static OpenAiResponseOutputItem MapResponseItem(LlmTestServerResponseItem item, int index,
        LlmTestServerResponseMetadata? metadata, bool includeSources)
    {
        return item switch
        {
            LlmTestServerResponseMessageItem message => new OpenAiResponseOutputMessage
            {
                Id = string.IsNullOrEmpty(message.Id)
                    ? "msg_" + (metadata ?? LlmTestServerResponseMetadata.Live()).IdFor(index == 0 ? "message" : $"message/{index}") : message.Id,
                Content = [new OpenAiResponseOutputText
                {
                    Text = message.Content,
                    Annotations = message.Citations.Select(citation => (object)new OpenAiUrlCitation
                    {
                        StartIndex = citation.StartIndex, EndIndex = citation.EndIndex,
                        Url = citation.Url, Title = citation.Title
                    }).ToList()
                }]
            },
            LlmTestServerResponseFunctionCallItem function => new OpenAiResponseFunctionCall
            {
                Id = string.IsNullOrEmpty(function.Id) ? "fc_" + function.Call.Id : function.Id,
                CallId = function.Call.Id,
                Name = function.Call.Name,
                Arguments = function.Call.ArgumentsJson
            },
            LlmTestServerResponseWebSearchItem web => new OpenAiResponseWebSearchCall
            {
                Id = web.Id,
                Action = new OpenAiWebSearchAction
                {
                    Type = "search",
                    Queries = [.. web.Queries],
                    Sources = includeSources ? web.Sources.Select(source => new OpenAiWebSearchSource { Url = source.Url }).ToList() : null
                }
            },
            _ => throw new InvalidOperationException("Unsupported Responses output item.")
        };
    }

    public static IEnumerable<object> ToResponseStreamEvents(LlmTestServerChatResult result, string responseId,
        long created, OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None,
        IReadOnlyCollection<string>? includedProperties = null)
    {
        var response = ToResponse(result, responseId, created, cacheUsageShape, includedProperties);
        var sequence = 0;
        yield return new { type = "response.created", sequence_number = sequence++, response = response with { Status = "in_progress", Output = [] } };
        yield return new { type = "response.in_progress", sequence_number = sequence++, response = response with { Status = "in_progress", Output = [] } };
        for (var index = 0; index < response.Output.Count; index++)
        {
            var item = response.Output[index];
            OpenAiResponseOutputItem initial = item switch
            {
                OpenAiResponseFunctionCall function => function with { Status = "in_progress", Arguments = string.Empty },
                OpenAiResponseOutputMessage message => message with { Status = "in_progress", Content = [] },
                OpenAiResponseWebSearchCall web => web with { Status = "in_progress", Action = null },
                _ => throw new InvalidOperationException("Unsupported Responses output item.")
            };
            yield return new { type = "response.output_item.added", sequence_number = sequence++, output_index = index, item = initial };
            if (item is OpenAiResponseFunctionCall functionItem)
            {
                yield return new { type = "response.function_call_arguments.delta", sequence_number = sequence++, item_id = item.Id, output_index = index, delta = functionItem.Arguments };
                yield return new { type = "response.function_call_arguments.done", sequence_number = sequence++, item_id = item.Id, output_index = index, arguments = functionItem.Arguments };
            }
            else if (item is OpenAiResponseWebSearchCall)
            {
                yield return new { type = "response.web_search_call.in_progress", sequence_number = sequence++, item_id = item.Id, output_index = index };
                yield return new { type = "response.web_search_call.searching", sequence_number = sequence++, item_id = item.Id, output_index = index };
                yield return new { type = "response.web_search_call.completed", sequence_number = sequence++, item_id = item.Id, output_index = index };
            }
            else if (item is OpenAiResponseOutputMessage messageItem)
            {
                var part = messageItem.Content[0];
                yield return new { type = "response.content_part.added", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part = new OpenAiResponseOutputText() };
                IReadOnlyList<string> chunks = result.OutputItems.Count > index && result.OutputItems[index] is LlmTestServerResponseMessageItem messagePlan
                    ? messagePlan.StreamChunks.Count > 0 ? messagePlan.StreamChunks : [messagePlan.Content]
                    : result.StreamChunks.Count > 0 ? result.StreamChunks : [part.Text];
                foreach (var chunk in chunks)
                {
                    yield return new { type = "response.output_text.delta", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, delta = chunk, logprobs = Array.Empty<object>() };
                }
                for (var annotationIndex = 0; annotationIndex < part.Annotations.Count; annotationIndex++)
                {
                    yield return new { type = "response.output_text.annotation.added", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, annotation_index = annotationIndex, annotation = part.Annotations[annotationIndex] };
                }
                yield return new { type = "response.output_text.done", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, text = part.Text, logprobs = Array.Empty<object>() };
                yield return new { type = "response.content_part.done", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part };
            }
            yield return new { type = "response.output_item.done", sequence_number = sequence++, output_index = index, item };
        }
        yield return new { type = "response.completed", sequence_number = sequence, response };
    }

    private static LlmTestServerResponseWebSearchItem ReadNativeWebSearchItem(JsonElement item)
    {
        var action = item.GetProperty("action");
        return new()
        {
            Id = item.GetProperty("id").GetString()!,
            Queries = action.TryGetProperty("queries", out var queries)
                ? queries.EnumerateArray().Select(query => query.GetString()!).ToList() : [],
            Sources = action.TryGetProperty("sources", out var sources)
                ? sources.EnumerateArray().Select(source => new LlmTestServerWebSearchSource { Url = source.GetProperty("url").GetString()! }).ToList() : []
        };
    }

    private static List<LlmTestServerResponseItem> ReadNativeMessageItems(JsonElement item, JsonElement content)
    {
        var citations = new List<LlmTestServerUrlCitation>();
        var parts = content.ValueKind == JsonValueKind.Array ? content.EnumerateArray().ToArray() : [];
        foreach (var part in parts)
        {
            if (!part.TryGetProperty("annotations", out var annotations)) { continue; }
            foreach (var annotation in annotations.EnumerateArray())
            {
                if (annotation.GetProperty("type").GetString() != "url_citation") { continue; }
                citations.Add(new()
                {
                    StartIndex = annotation.GetProperty("start_index").GetInt32(),
                    EndIndex = annotation.GetProperty("end_index").GetInt32(),
                    Url = annotation.GetProperty("url").GetString()!,
                    Title = annotation.GetProperty("title").GetString()!
                });
            }
        }
        var outputId = item.TryGetProperty("id", out var id) ? id.GetString()! : string.Empty;
        if (citations.Count == 0 && string.IsNullOrEmpty(outputId)) { return []; }
        return [new LlmTestServerResponseMessageItem
        {
            Id = outputId,
            Content = ReadResponseInputContent(content), Citations = citations
        }];
    }

    private static List<LlmTestServerMessage> CoalesceNativeAssistantGroups(List<LlmTestServerMessage> messages)
    {
        var result = new List<LlmTestServerMessage>();
        for (var index = 0; index < messages.Count;)
        {
            var end = index;
            while (end < messages.Count && messages[end].Role == "assistant") { end++; }
            if (end == index) { result.Add(messages[index++]); continue; }
            var group = messages.GetRange(index, end - index);
            if (!group.Any(message => message.OutputItems.Any(item => item is LlmTestServerResponseWebSearchItem)))
            { result.AddRange(group); index = end; continue; }
            var items = new List<LlmTestServerResponseItem>();
            foreach (var message in group)
            {
                if (message.OutputItems.Count > 0) { items.AddRange(message.OutputItems); continue; }
                if (message.Content.Length > 0) { items.Add(new LlmTestServerResponseMessageItem { Content = message.Content }); }
                items.AddRange(message.ToolCalls.Select(call => new LlmTestServerResponseFunctionCallItem { Call = call }));
            }
            result.Add(new LlmTestServerMessage
            {
                Role = "assistant",
                Content = string.Concat(group.Select(message => message.Content)),
                ToolCalls = group.SelectMany(message => message.ToolCalls).ToList(),
                OutputItems = items
            });
            index = end;
        }
        return result;
    }
}
