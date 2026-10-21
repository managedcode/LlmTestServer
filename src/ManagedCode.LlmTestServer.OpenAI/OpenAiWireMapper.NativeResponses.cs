using System.Text.Json;
using ManagedCode.LlmTestServer.Determinism;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.OpenAI;

public static partial class OpenAiWireMapper
{
    private const string _webSearchSources = "web_search_call.action.sources";
    private const string _fileSearchResults = "file_search_call.results";
    private const string _codeInterpreterOutputs = "code_interpreter_call.outputs";

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
        return request with
        {
            NativeToolSurface = allowNativeTools ? LlmTestServerNativeToolSurface.OpenAiResponses : null,
            NativeTools = source.Tools.Where(tool => NativeContract(tool.Type) is not null)
                .Select(CreateNativeDeclaration).ToList(),
            RequiredNativeTool = source.ToolChoice.ValueKind == JsonValueKind.Object
                ? NativeContract(source.ToolChoice.GetProperty("type").GetString()) : null,
            NativeIncludes = source.Include.Select(NativeInclude).Where(include => include is not null)
                .Select(include => include!.Value).ToList(),
        };
    }

    private static List<OpenAiResponseOutputItem> CreateResponseOutput(LlmTestServerChatResult result,
        IReadOnlyCollection<string>? includedProperties)
    {
        var output = new List<OpenAiResponseOutputItem>();
        if (result.OutputItems.Count > 0)
        {
            for (var index = 0; index < result.OutputItems.Count; index++)
            {
                output.Add(MapResponseItem(result.OutputItems[index], index, result.WireMetadata, includedProperties));
            }
            return output;
        }
        if (result.Content.Length > 0 || result.ToolCalls.Count == 0)
        {
            var message = CreateOutputMessage(result.Content, completed: true, result.WireMetadata);
            if (result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Refused)
            { message = message with { Content = [new OpenAiResponseRefusal { Refusal = result.Content }] }; }
            if (result.OutputDisposition?.Kind == LlmTestServerOutputDispositionKind.Filtered)
            { message = message with { Status = "incomplete" }; }
            output.Add(message);
        }
        output.AddRange(result.ToolCalls.Select(call => new OpenAiResponseFunctionCall
        { Id = "fc_" + call.Id, CallId = call.Id, Name = call.Name, Arguments = call.ArgumentsJson }));
        return output;
    }

    private static OpenAiResponseOutputItem MapResponseItem(LlmTestServerResponseItem item, int index,
        LlmTestServerResponseMetadata? metadata, IReadOnlyCollection<string>? includedProperties)
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
                    Annotations = message.Citations.Select(MapCitation).ToList()
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
                    Sources = Includes(includedProperties, _webSearchSources) ? web.Sources.Select(source => new OpenAiWebSearchSource { Url = source.Url }).ToList() : null
                }
            },
            LlmTestServerResponseFileSearchItem file => MapFileSearchItem(file, includedProperties),
            LlmTestServerResponseCodeInterpreterItem code => MapCodeInterpreterItem(code, includedProperties),
            _ => throw new InvalidOperationException("Unsupported Responses output item.")
        };
    }

    public static IEnumerable<object> ToResponseStreamEvents(LlmTestServerChatResult result, string responseId,
        long created, OpenAiCacheUsageShape cacheUsageShape = OpenAiCacheUsageShape.None,
        IReadOnlyCollection<string>? includedProperties = null, bool azureToolUsage = false)
    {
        var response = ToResponse(result, responseId, created, cacheUsageShape, includedProperties, azureToolUsage);
        var sequence = 0;
        yield return new { type = "response.created", sequence_number = sequence++, response = response with { Status = "in_progress", IncompleteDetails = null, Output = [], ToolUsage = null } };
        yield return new { type = "response.in_progress", sequence_number = sequence++, response = response with { Status = "in_progress", IncompleteDetails = null, Output = [], ToolUsage = null } };
        for (var index = 0; index < response.Output.Count; index++)
        {
            var item = response.Output[index];
            OpenAiResponseOutputItem initial = item switch
            {
                OpenAiResponseFunctionCall function => function with { Status = "in_progress", Arguments = string.Empty },
                OpenAiResponseOutputMessage message => message with { Status = "in_progress", Content = [] },
                OpenAiResponseWebSearchCall web => web with { Status = "in_progress", Action = null },
                OpenAiResponseFileSearchCall file => file with { Status = "in_progress", Queries = [], Results = null },
                OpenAiResponseCodeInterpreterCall code => code with { Status = "in_progress", Code = code.Code is null ? null : string.Empty, Outputs = null },
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
            else if (item is OpenAiResponseFileSearchCall)
            {
                yield return new { type = "response.file_search_call.in_progress", sequence_number = sequence++, item_id = item.Id, output_index = index };
                yield return new { type = "response.file_search_call.searching", sequence_number = sequence++, item_id = item.Id, output_index = index };
                yield return new { type = "response.file_search_call.completed", sequence_number = sequence++, item_id = item.Id, output_index = index };
            }
            else if (item is OpenAiResponseCodeInterpreterCall codeItem)
            {
                yield return new { type = "response.code_interpreter_call.in_progress", sequence_number = sequence++, item_id = item.Id, output_index = index };
                if (codeItem.Code is not null)
                {
                    IReadOnlyList<string> codeChunks = result.OutputItems.Count > index
                        && result.OutputItems[index] is LlmTestServerResponseCodeInterpreterItem { CodeChunks.Count: > 0 } codePlan
                        ? codePlan.CodeChunks : [codeItem.Code];
                    foreach (var chunk in codeChunks)
                    {
                        yield return new { type = "response.code_interpreter_call_code.delta", sequence_number = sequence++, item_id = item.Id, output_index = index, delta = chunk };
                    }
                    yield return new { type = "response.code_interpreter_call_code.done", sequence_number = sequence++, item_id = item.Id, output_index = index, code = codeItem.Code };
                }
                yield return new { type = "response.code_interpreter_call.interpreting", sequence_number = sequence++, item_id = item.Id, output_index = index };
                yield return new { type = "response.code_interpreter_call.completed", sequence_number = sequence++, item_id = item.Id, output_index = index };
            }
            else if (item is OpenAiResponseOutputMessage messageItem)
            {
                if (messageItem.Content[0] is OpenAiResponseRefusal refusal)
                {
                    yield return new { type = "response.content_part.added", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part = (OpenAiResponseContentPart)new OpenAiResponseRefusal() };
                    foreach (var chunk in result.StreamChunks.Count > 0 ? result.StreamChunks : [refusal.Refusal])
                    {
                        yield return new { type = "response.refusal.delta", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, delta = chunk };
                    }
                    yield return new { type = "response.refusal.done", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, refusal = refusal.Refusal };
                    yield return new { type = "response.content_part.done", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part = (OpenAiResponseContentPart)refusal };
                }
                else
                {
                    var part = (OpenAiResponseOutputText)messageItem.Content[0];
                    yield return new { type = "response.content_part.added", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part = (OpenAiResponseContentPart)new OpenAiResponseOutputText() };
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
                    yield return new { type = "response.content_part.done", sequence_number = sequence++, item_id = item.Id, output_index = index, content_index = 0, part = (OpenAiResponseContentPart)part };
                }
            }
            yield return new { type = "response.output_item.done", sequence_number = sequence++, output_index = index, item };
        }
        yield return new { type = response.Status == "incomplete" ? "response.incomplete" : "response.completed", sequence_number = sequence, response };
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
        if (HasRefusalPart(content)) { return []; }
        var citations = new List<LlmTestServerResponseCitation>();
        var parts = content.ValueKind == JsonValueKind.Array ? content.EnumerateArray().ToArray() : [];
        foreach (var part in parts)
        {
            if (part.ValueKind != JsonValueKind.Object || !part.TryGetProperty("annotations", out var annotations)) { continue; }
            foreach (var annotation in annotations.EnumerateArray())
            {
                citations.Add(ReadCitation(annotation));
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
            if (!group.Any(message => message.OutputItems.Any(item => IsNativeItem(item)
                || item is LlmTestServerResponseMessageItem { Citations.Count: > 0 })))
            {
                result.AddRange(group.Select(message => message with { OutputItems = [] }));
                index = end;
                continue;
            }
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
                OutputDisposition = group.Select(message => message.OutputDisposition).FirstOrDefault(disposition => disposition is not null),
                Content = string.Concat(group.Select(message => message.Content)),
                ToolCalls = group.SelectMany(message => message.ToolCalls).ToList(),
                OutputItems = items
            });
            index = end;
        }
        return result;
    }
}
