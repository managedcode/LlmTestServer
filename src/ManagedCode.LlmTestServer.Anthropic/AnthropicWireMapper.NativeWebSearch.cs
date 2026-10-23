using System.Text.Json;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Anthropic;

public static partial class AnthropicWireMapper
{
    private static bool IsNativeWebTool(AnthropicTool tool)
    {
        return tool.Type == AnthropicNativeWebSearchContract.ToolType;
    }

    private static bool IsNamedNativeChoice(AnthropicMessagesRequest request)
    {
        return request.ToolChoice is { Type: "tool", Name: AnthropicNativeWebSearchContract.ToolName }
            && request.Tools.Any(IsNativeWebTool);
    }

    private static LlmTestServerNativeToolDeclaration ToNativeDeclaration(AnthropicTool tool)
    {
        return new()
        {
            Contract = LlmTestServerNativeToolContract.AnthropicMessagesWebSearch20250305,
            AnthropicWebSearchOptions = new()
            {
                MaxUses = tool.MaxUses,
                AllowedDomains = tool.AllowedDomains is { } allowed ? [.. allowed] : null,
                BlockedDomains = tool.BlockedDomains is { } blocked ? [.. blocked] : null,
                UserLocation = tool.UserLocation is { } location ? new()
                {
                    Type = location.Type,
                    City = location.City,
                    Region = location.Region,
                    Country = location.Country,
                    Timezone = location.Timezone,
                } : null,
            },
        };
    }

    private static List<AnthropicContentBlock> ToOrderedContent(LlmTestServerChatResult result)
    {
        return result.OutputItems.Select(item => item switch
        {
            LlmTestServerResponseMessageItem message => new AnthropicContentBlock
            {
                Text = message.Content,
                Citations = message.Citations.Count > 0 ? message.Citations.Select(ToNativeCitation).ToList() : null,
            },
            LlmTestServerResponseFunctionCallItem function => ToToolBlock(function.Call),
            LlmTestServerResponseServerToolUseItem call => new AnthropicContentBlock
            {
                Type = "server_tool_use",
                Caller = new(),
                Text = null,
                Id = call.Id,
                Name = AnthropicNativeWebSearchContract.ToolName,
                Input = JsonSerializer.SerializeToElement(new { query = ((LlmTestServerWebSearchInput)call.Input).Query }),
            },
            LlmTestServerResponseServerToolResultItem returned => new AnthropicContentBlock
            {
                Type = "web_search_tool_result",
                Caller = new(),
                Text = null,
                ToolUseId = returned.ToolUseId,
                Content = ToNativeResultContent(returned.Result),
            },
            _ => throw new InvalidOperationException("Unsupported Anthropic output item."),
        }).ToList();
    }

    private static JsonElement ToNativeResultContent(LlmTestServerServerToolResult result)
    {
        return result switch
        {
            LlmTestServerWebSearchResults found => JsonSerializer.SerializeToElement(found.Results.Select(item => new AnthropicWebSearchResult
            { Url = item.Url, Title = item.Title, EncryptedContent = item.EncryptedContent, PageAge = item.PageAge }).ToList()),
            LlmTestServerWebSearchErrorResult error => JsonSerializer.SerializeToElement(new AnthropicWebSearchToolResultError { ErrorCode = error.ErrorCode }),
            _ => throw new InvalidOperationException("Unsupported Anthropic server tool result."),
        };
    }

    private static AnthropicWebSearchCitation ToNativeCitation(LlmTestServerResponseCitation citation)
    {
        return citation is LlmTestServerAnthropicWebSearchCitation web ? new()
        {
            Url = web.Url,
            Title = web.Title,
            EncryptedIndex = web.EncryptedIndex,
            CitedText = web.CitedText,
        } : throw new InvalidOperationException("Unsupported Anthropic citation.");
    }

    private static AnthropicServerToolUsage? NativeServerToolUsage(LlmTestServerChatResult result)
    {
        if (!result.OutputItems.Any(item => item is LlmTestServerResponseServerToolUseItem)) { return null; }
        return new()
        {
            WebSearchRequests = result.OutputItems.OfType<LlmTestServerResponseServerToolResultItem>()
                .Count(item => item.Result is not LlmTestServerWebSearchErrorResult { ErrorCode: "max_uses_exceeded" }),
        };
    }

    public static IEnumerable<JsonElement> ToOrderedContentStreamEvents(LlmTestServerChatResult result)
    {
        var content = ToOrderedContent(result);
        for (var index = 0; index < content.Count; index++)
        {
            var block = content[index];
            if (block.Type is "server_tool_use" or "tool_use")
            {
                yield return JsonSerializer.SerializeToElement(new { type = "content_block_start", index, content_block = block with { Input = JsonSerializer.SerializeToElement(new { }) } });
                yield return JsonSerializer.SerializeToElement(new { type = "content_block_delta", index, delta = new { type = "input_json_delta", partial_json = block.Input!.Value.GetRawText() } });
            }
            else if (block.Type == "text")
            {
                yield return JsonSerializer.SerializeToElement(new { type = "content_block_start", index, content_block = block with { Text = string.Empty, Citations = block.Citations is null ? null : [] } });
                var plan = (LlmTestServerResponseMessageItem)result.OutputItems[index];
                IReadOnlyList<string> chunks = plan.StreamChunks.Count > 0 ? plan.StreamChunks : [plan.Content];
                foreach (var chunk in chunks)
                {
                    yield return JsonSerializer.SerializeToElement(new { type = "content_block_delta", index, delta = new { type = "text_delta", text = chunk } });
                }
                foreach (var citation in block.Citations ?? [])
                {
                    yield return JsonSerializer.SerializeToElement(new { type = "content_block_delta", index, delta = new { type = "citations_delta", citation } });
                }
            }
            else
            {
                yield return JsonSerializer.SerializeToElement(new { type = "content_block_start", index, content_block = block });
            }
            yield return JsonSerializer.SerializeToElement(new { type = "content_block_stop", index });
        }
    }

    private static bool HasNativeHistory(IEnumerable<JsonElement> blocks)
    {
        return blocks.Any(block => block.ValueKind == JsonValueKind.Object
            && (block.TryGetProperty("type", out var type) && type.GetString() is "server_tool_use" or "web_search_tool_result"
                || block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array && citations.GetArrayLength() > 0));
    }

    private static LlmTestServerMessage ReadNativeAssistant(IEnumerable<JsonElement> blocks)
    {
        var items = new List<LlmTestServerResponseItem>();
        var calls = new List<LlmTestServerToolCall>();
        foreach (var block in blocks)
        {
            switch (block.GetProperty("type").GetString())
            {
                case "text":
                    items.Add(new LlmTestServerResponseMessageItem
                    {
                        Content = block.GetProperty("text").GetString()!,
                        Citations = block.TryGetProperty("citations", out var citations) && citations.ValueKind == JsonValueKind.Array
                            ? citations.EnumerateArray().Select(ReadNativeCitation).ToList() : [],
                    });
                    break;
                case "tool_use":
                    var function = new LlmTestServerToolCall
                    { Id = block.GetProperty("id").GetString()!, Name = block.GetProperty("name").GetString()!, ArgumentsJson = block.GetProperty("input").GetRawText() };
                    calls.Add(function);
                    items.Add(new LlmTestServerResponseFunctionCallItem { Id = function.Id, Call = function });
                    break;
                case "server_tool_use":
                    items.Add(new LlmTestServerResponseServerToolUseItem
                    {
                        Id = block.GetProperty("id").GetString()!,
                        Input = new LlmTestServerWebSearchInput { Query = block.GetProperty("input").GetProperty("query").GetString()! },
                    });
                    break;
                case "web_search_tool_result":
                    items.Add(new LlmTestServerResponseServerToolResultItem
                    { ToolUseId = block.GetProperty("tool_use_id").GetString()!, Result = ReadNativeResult(block.GetProperty("content")) });
                    break;
            }
        }
        return new()
        {
            Role = "assistant",
            OutputItems = items,
            ToolCalls = calls,
            Content = string.Concat(items.OfType<LlmTestServerResponseMessageItem>().Select(item => item.Content)),
        };
    }

    private static LlmTestServerResponseCitation ReadNativeCitation(JsonElement citation)
    {
        return new LlmTestServerAnthropicWebSearchCitation
        {
            Url = citation.GetProperty("url").GetString()!,
            EncryptedIndex = citation.GetProperty("encrypted_index").GetString()!,
            CitedText = citation.GetProperty("cited_text").GetString()!,
            Title = citation.TryGetProperty("title", out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null,
        };
    }

    private static LlmTestServerServerToolResult ReadNativeResult(JsonElement content)
    {
        return content.ValueKind == JsonValueKind.Object
            ? new LlmTestServerWebSearchErrorResult { ErrorCode = content.GetProperty("error_code").GetString()! }
            : new LlmTestServerWebSearchResults
            {
                Results = content.EnumerateArray().Select(result => new LlmTestServerAnthropicWebSearchResult
                {
                    Url = result.GetProperty("url").GetString()!,
                    Title = result.GetProperty("title").GetString()!,
                    EncryptedContent = result.GetProperty("encrypted_content").GetString()!,
                    PageAge = result.TryGetProperty("page_age", out var pageAge) && pageAge.ValueKind == JsonValueKind.String ? pageAge.GetString() : null,
                }).ToList(),
            };
    }
}
