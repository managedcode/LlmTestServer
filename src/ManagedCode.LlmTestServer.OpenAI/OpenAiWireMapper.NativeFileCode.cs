using System.Text.Json;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.OpenAI;

public static partial class OpenAiWireMapper
{
    private static LlmTestServerNativeToolContract? NativeContract(string? type)
    {
        return type switch
        {
            "web_search" => LlmTestServerNativeToolContract.OpenAiResponsesWebSearch,
            "file_search" => LlmTestServerNativeToolContract.OpenAiResponsesFileSearch,
            "code_interpreter" => LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter,
            _ => null,
        };
    }

    private static LlmTestServerNativeToolInclude? NativeInclude(string property)
    {
        return property switch
        {
            _webSearchSources => LlmTestServerNativeToolInclude.WebSearchSources,
            _fileSearchResults => LlmTestServerNativeToolInclude.FileSearchResults,
            _codeInterpreterOutputs => LlmTestServerNativeToolInclude.CodeInterpreterOutputs,
            _ => null,
        };
    }

    private static bool Includes(IReadOnlyCollection<string>? properties, string property)
    {
        return properties?.Contains(property, StringComparer.Ordinal) == true;
    }

    private static bool IsNativeItem(LlmTestServerResponseItem item)
    {
        return item is LlmTestServerResponseWebSearchItem or LlmTestServerResponseFileSearchItem
            or LlmTestServerResponseCodeInterpreterItem;
    }

    private static LlmTestServerNativeToolDeclaration CreateNativeDeclaration(OpenAiResponseTool tool)
    {
        var contract = NativeContract(tool.Type)!.Value;
        return contract switch
        {
            LlmTestServerNativeToolContract.OpenAiResponsesWebSearch => new()
            {
                Contract = contract,
                WebSearchOptions = new()
                {
                    SearchContextSize = tool.SearchContextSize,
                    AllowedDomains = tool.WebSearchFilters?.AllowedDomains is { } domains ? [.. domains] : [],
                    UserLocation = tool.UserLocation is { } location ? new()
                    {
                        Type = location.Type,
                        Country = location.Country,
                        Region = location.Region,
                        City = location.City,
                        Timezone = location.Timezone,
                    } : null,
                },
            },
            LlmTestServerNativeToolContract.OpenAiResponsesFileSearch => new()
            {
                Contract = contract,
                FileSearchOptions = new()
                {
                    VectorStoreIds = tool.VectorStoreIds is { } stores ? [.. stores] : [],
                    MaxNumResults = tool.MaxNumResults,
                    Ranker = tool.RankingOptions?.Ranker,
                    ScoreThreshold = tool.RankingOptions?.ScoreThreshold,
                    Filters = tool.Filters is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } filters
                        ? filters.Clone() : null,
                },
            },
            LlmTestServerNativeToolContract.OpenAiResponsesCodeInterpreter => new()
            {
                Contract = contract,
                CodeInterpreterOptions = tool.Container.ValueKind == JsonValueKind.String
                    ? new() { ContainerId = tool.Container.GetString() }
                    : new()
                    {
                        AutomaticContainer = true,
                        FileIds = tool.Container.TryGetProperty("file_ids", out var files)
                            ? files.EnumerateArray().Select(file => file.GetString()!).ToList() : [],
                    },
            },
            _ => throw new InvalidOperationException("Unsupported native declaration."),
        };
    }

    private static JsonElement MapCitation(LlmTestServerResponseCitation citation)
    {
        return citation switch
        {
            LlmTestServerUrlCitation url => JsonSerializer.SerializeToElement(new OpenAiUrlCitation
            {
                StartIndex = url.StartIndex,
                EndIndex = url.EndIndex,
                Url = url.Url,
                Title = url.Title,
            }),
            LlmTestServerFileCitation file => JsonSerializer.SerializeToElement(new OpenAiFileCitation
            {
                FileId = file.FileId,
                Filename = file.Filename,
                Index = file.Index,
            }),
            LlmTestServerContainerFileCitation file => JsonSerializer.SerializeToElement(new OpenAiContainerFileCitation
            {
                ContainerId = file.ContainerId,
                FileId = file.FileId,
                Filename = file.Filename,
                StartIndex = file.StartIndex,
                EndIndex = file.EndIndex,
            }),
            _ => throw new InvalidOperationException("Unsupported Responses citation."),
        };
    }

    private static OpenAiResponseFileSearchCall MapFileSearchItem(LlmTestServerResponseFileSearchItem item,
        IReadOnlyCollection<string>? includedProperties)
    {
        return new()
        {
            Id = item.Id,
            Queries = [.. item.Queries],
            Results = Includes(includedProperties, _fileSearchResults) ? item.Results.Select(result => new OpenAiFileSearchResult
            {
                FileId = result.FileId,
                Filename = result.Filename,
                Text = result.Text,
                Score = result.Score,
                Attributes = result.Attributes?.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            }).ToList() : null,
        };
    }

    private static OpenAiResponseCodeInterpreterCall MapCodeInterpreterItem(LlmTestServerResponseCodeInterpreterItem item,
        IReadOnlyCollection<string>? includedProperties)
    {
        return new()
        {
            Id = item.Id,
            ContainerId = item.ContainerId,
            Code = item.Code,
            Outputs = Includes(includedProperties, _codeInterpreterOutputs)
                ? item.Outputs?.Select(MapCodeOutput).ToList() : null,
        };
    }

    private static OpenAiCodeInterpreterOutput MapCodeOutput(LlmTestServerCodeInterpreterOutput output)
    {
        return output switch
        {
            LlmTestServerCodeInterpreterLogsOutput logs => new OpenAiCodeInterpreterLogsOutput { Logs = logs.Logs },
            LlmTestServerCodeInterpreterImageOutput image => new OpenAiCodeInterpreterImageOutput { Url = image.Url },
            _ => throw new InvalidOperationException("Unsupported code interpreter output."),
        };
    }

    private static LlmTestServerResponseItem ReadNativeToolItem(JsonElement item)
    {
        return item.GetProperty("type").GetString() switch
        {
            "web_search_call" => ReadNativeWebSearchItem(item),
            "file_search_call" => ReadFileSearchItem(item),
            "code_interpreter_call" => ReadCodeInterpreterItem(item),
            _ => throw new InvalidOperationException("Unsupported native history item."),
        };
    }

    private static LlmTestServerResponseFileSearchItem ReadFileSearchItem(JsonElement item)
    {
        return new()
        {
            Id = item.GetProperty("id").GetString()!,
            Queries = item.GetProperty("queries").EnumerateArray().Select(query => query.GetString()!).ToList(),
            Results = item.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array
                ? results.EnumerateArray().Select(result => new LlmTestServerFileSearchResult
                {
                    FileId = OptionalString(result, "file_id"),
                    Filename = OptionalString(result, "filename"),
                    Text = OptionalString(result, "text"),
                    Score = result.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number ? score.GetDouble() : null,
                    Attributes = result.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object
                        ? attributes.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone(), StringComparer.Ordinal) : null,
                }).ToList() : [],
        };
    }

    private static LlmTestServerResponseCodeInterpreterItem ReadCodeInterpreterItem(JsonElement item)
    {
        return new()
        {
            Id = item.GetProperty("id").GetString()!,
            ContainerId = item.GetProperty("container_id").GetString()!,
            Code = OptionalString(item, "code"),
            Outputs = item.TryGetProperty("outputs", out var outputs) && outputs.ValueKind == JsonValueKind.Array
                ? outputs.EnumerateArray().Select(ReadCodeOutput).ToList() : null,
        };
    }

    private static LlmTestServerCodeInterpreterOutput ReadCodeOutput(JsonElement output)
    {
        return output.GetProperty("type").GetString() switch
        {
            "logs" => new LlmTestServerCodeInterpreterLogsOutput { Logs = output.GetProperty("logs").GetString()! },
            "image" => new LlmTestServerCodeInterpreterImageOutput { Url = output.GetProperty("url").GetString()! },
            _ => throw new InvalidOperationException("Unsupported code interpreter history output."),
        };
    }

    private static LlmTestServerResponseCitation ReadCitation(JsonElement citation)
    {
        return citation.GetProperty("type").GetString() switch
        {
            "url_citation" => new LlmTestServerUrlCitation
            {
                StartIndex = citation.GetProperty("start_index").GetInt32(),
                EndIndex = citation.GetProperty("end_index").GetInt32(),
                Url = citation.GetProperty("url").GetString()!,
                Title = citation.GetProperty("title").GetString()!,
            },
            "file_citation" => new LlmTestServerFileCitation
            {
                FileId = citation.GetProperty("file_id").GetString()!,
                Filename = citation.GetProperty("filename").GetString()!,
                Index = citation.GetProperty("index").GetInt32(),
            },
            "container_file_citation" => new LlmTestServerContainerFileCitation
            {
                ContainerId = citation.GetProperty("container_id").GetString()!,
                FileId = citation.GetProperty("file_id").GetString()!,
                Filename = citation.GetProperty("filename").GetString()!,
                StartIndex = citation.GetProperty("start_index").GetInt32(),
                EndIndex = citation.GetProperty("end_index").GetInt32(),
            },
            _ => throw new InvalidOperationException("Unsupported Responses history citation."),
        };
    }

    private static string? OptionalString(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
