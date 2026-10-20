#pragma warning disable OPENAI001 // Exercise official Responses SDK contracts through the running server.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Responses;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeFileCodeIntegrationTests
{
    private const string _storeId = "vs_fixture";
    private const string _fileId = "file_fixture";
    private const string _fileName = "fixture.txt";
    private const string _containerId = "cntr_fixture";
    private const string _inputFile = "file_input";
    private const string _generatedFile = "file_generated";
    private const string _imageUrl = "https://example.com/fixture.png";
    private const string _code = "print(42)\n";
    private const string _answer = "Paris fixture.";
    private static readonly JsonSerializerOptions _configurationJson = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    private static string Model(bool code)
    {
        return code ? LlmTestServerKnownModelIds.Gpt41 : LlmTestServerKnownModelIds.Gpt4OMini;
    }

    private static string ItemId(bool code)
    {
        return code ? "ci_fixture" : "fs_fixture";
    }

    private static Dictionary<string, JsonElement> Attributes()
    {
        return new()
        {
            ["category"] = JsonSerializer.SerializeToElement("fixture"),
            ["year"] = JsonSerializer.SerializeToElement(2026),
            ["approved"] = JsonSerializer.SerializeToElement(true),
        };
    }

    private static LlmTestServerResponseItem[] Items(bool code)
    {
        if (code)
        {
            return [new LlmTestServerResponseCodeInterpreterItem
            {
                Id = ItemId(true), ContainerId = _containerId, InputFileIds = [_inputFile], GeneratedFileIds = [_generatedFile],
                Code = _code, CodeChunks = ["print(", "42)", "\n"],
                Outputs = [new LlmTestServerCodeInterpreterLogsOutput { Logs = "42\n" }, new LlmTestServerCodeInterpreterImageOutput { Url = _imageUrl }],
            }, new LlmTestServerResponseMessageItem
            {
                Id = "msg_code_fixture", Content = _answer, StreamChunks = ["Paris ", "fixture."],
                Citations = [new LlmTestServerContainerFileCitation
                { ContainerId = _containerId, FileId = _generatedFile, Filename = "result.txt", StartIndex = 0, EndIndex = 0 }],
            }];
        }
        return [new LlmTestServerResponseFileSearchItem
        {
            Id = ItemId(false), VectorStoreIds = [_storeId], Queries = ["Paris fixture facts"],
            Results = [new LlmTestServerFileSearchResult
            { FileId = _fileId, Filename = _fileName, Text = "Scripted Paris source", Score = 0.9, Attributes = Attributes() }],
        }, new LlmTestServerResponseMessageItem
        {
            Id = "msg_file_fixture", Content = _answer, StreamChunks = ["Paris ", "fixture."],
            Citations = [new LlmTestServerFileCitation { FileId = _fileId, Filename = _fileName, Index = 5 }],
        }];
    }

    private static LlmTestServerConfiguration Configuration(bool code, int responses = 1)
    {
        return new LlmTestServerClientConfigurationBuilder().UseChatModel(Model(code)).UseChatScenario("native-file-code", scenario =>
        {
            scenario.ForModel(Model(code));
            for (var index = 0; index < responses; index++) { scenario.RespondsWithItems(Items(code)); }
        }).Build();
    }

    private static JsonObject Request(bool code, bool include = true, bool auto = false, bool stream = false, string input = "start")
    {
        JsonNode container = auto ? new JsonObject { ["type"] = "auto", ["file_ids"] = new JsonArray(_inputFile) } : JsonValue.Create(_containerId)!;
        var tool = code
            ? new JsonObject { ["type"] = "code_interpreter", ["container"] = container }
            : new JsonObject { ["type"] = "file_search", ["vector_store_ids"] = new JsonArray(_storeId) };
        var body = new JsonObject
        {
            ["model"] = Model(code),
            ["input"] = input,
            ["tools"] = new JsonArray(tool),
            ["stream"] = stream,
            ["tool_choice"] = new JsonObject { ["type"] = code ? "code_interpreter" : "file_search" },
        };
        if (include) { body["include"] = new JsonArray(code ? "code_interpreter_call.outputs" : "file_search_call.results"); }
        return body;
    }

    private static CreateResponseOptions Options(bool code, bool include = true, bool auto = false, string input = "start")
    {
        var options = new CreateResponseOptions
        {
            Model = Model(code),
            InputItems = { ResponseItem.CreateUserMessageItem(input) },
            ToolChoice = code ? ResponseToolChoice.CreateRequiredChoice() : ResponseToolChoice.CreateFileSearchChoice(),
        };
        if (code)
        {
            var container = auto
                ? new CodeInterpreterToolContainer(CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration([_inputFile]))
                : new CodeInterpreterToolContainer(_containerId);
            options.Tools.Add(ResponseTool.CreateCodeInterpreterTool(container));
        }
        else
        {
            options.Tools.Add(ResponseTool.CreateFileSearchTool([_storeId], 2,
                new FileSearchToolRankingOptions { Ranker = FileSearchToolRanker.Auto, ScoreThreshold = 0.5f },
                BinaryData.FromString("""{"type":"eq","key":"category","value":"fixture"}""")));
        }
        if (include) { options.IncludedProperties.Add(code ? IncludedResponseProperty.CodeInterpreterCallOutputs : IncludedResponseProperty.FileSearchCallResults); }
        return options;
    }

    private static ResponsesClient Sdk(HttpClient http)
    {
        return new(new ApiKeyCredential("provider-key"), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http) });
    }

    private static async Task<JsonElement> CompleteAsync(HttpClient http, bool code, JsonObject? body = null)
    {
        using var response = await http.PostAsJsonAsync("/openai/v1/responses", body ?? Request(code));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task AssertSdkAsync(ResponseResult response, bool code, bool include = true)
    {
        await Assert.That(response.OutputItems.Count).IsEqualTo(2);
        await Assert.That(response.OutputItems.OfType<FunctionCallResponseItem>().Count()).IsEqualTo(0);
        await Assert.That(response.OutputItems[0].Id).IsEqualTo(ItemId(code));
        if (code)
        {
            var call = (CodeInterpreterCallResponseItem)response.OutputItems[0];
            await Assert.That(call.ContainerId).IsEqualTo(_containerId);
            await Assert.That(call.Status).IsEqualTo(CodeInterpreterCallStatus.Completed);
            await Assert.That(call.Code).IsEqualTo(_code);
            await Assert.That(call.Outputs?.Count ?? 0).IsEqualTo(include ? 2 : 0);
            if (include)
            {
                await Assert.That(((CodeInterpreterCallLogsOutput)call.Outputs![0]).Logs).IsEqualTo("42\n");
                await Assert.That(((CodeInterpreterCallImageOutput)call.Outputs![1]).ImageUri.AbsoluteUri).IsEqualTo(_imageUrl);
            }
        }
        else
        {
            var call = (FileSearchCallResponseItem)response.OutputItems[0];
            await Assert.That(call.Status).IsEqualTo(FileSearchCallStatus.Completed);
            await Assert.That(call.Queries.Single()).IsEqualTo("Paris fixture facts");
            await Assert.That(call.Results?.Count ?? 0).IsEqualTo(include ? 1 : 0);
            if (include)
            {
                var result = call.Results!.Single();
                await Assert.That(result.FileId).IsEqualTo(_fileId);
                await Assert.That(result.Filename).IsEqualTo(_fileName);
                await Assert.That(result.Text).IsEqualTo("Scripted Paris source");
                await Assert.That(result.Score).IsEqualTo(0.9f);
                await Assert.That(JsonSerializer.Deserialize<string>(result.Attributes["category"].ToString())).IsEqualTo("fixture");
                await Assert.That(JsonSerializer.Deserialize<int>(result.Attributes["year"].ToString())).IsEqualTo(2026);
                await Assert.That(JsonSerializer.Deserialize<bool>(result.Attributes["approved"].ToString())).IsTrue();
            }
        }
        var message = (MessageResponseItem)response.OutputItems[1];
        await Assert.That(message.Content.Single().Text).IsEqualTo(_answer);
        var annotation = message.Content.Single().OutputTextAnnotations.Single();
        if (code)
        {
            var citation = (ContainerFileCitationMessageAnnotation)annotation;
            await Assert.That(citation.ContainerId).IsEqualTo(_containerId);
            await Assert.That(citation.FileId).IsEqualTo(_generatedFile);
            await Assert.That(citation.Filename).IsEqualTo("result.txt");
            await Assert.That(citation.StartIndex).IsEqualTo(0);
            await Assert.That(citation.EndIndex).IsEqualTo(0);
        }
        else
        {
            var citation = (FileCitationMessageAnnotation)annotation;
            await Assert.That(citation.FileId).IsEqualTo(_fileId);
            await Assert.That(citation.Filename).IsEqualTo(_fileName);
            await Assert.That(citation.Index).IsEqualTo(5);
        }
    }
}
#pragma warning restore OPENAI001
