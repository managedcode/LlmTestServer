#pragma warning disable OPENAI001 // Exercise the official Responses client against installed packages.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net.Http.Headers;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Scenarios;
using OpenAI.Responses;
using TUnit.Core;

public sealed class NativeFileCodePackageTests
{
    private const string _key = "file-code-package-key";

    [Test]
    public async Task InstalledPackages_ServeFileAndCodeJsonStreamAndStoredHistory(CancellationToken cancellationToken)
    {
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("file-code-package-server").WithApiKey(_key);
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("file-code-package-server", cancellationToken);
        using var http = app.CreateHttpClient("file-code-package-server", "http");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _key);
        var control = new LlmTestServerClient(http, _key);
        var sdk = new ResponsesClient(new ApiKeyCredential(_key), new ResponsesClientOptions
        { Endpoint = new Uri(http.BaseAddress!, "/openai/v1/"), Transport = new HttpClientPipelineTransport(http) });
        foreach (var code in new[] { false, true })
        {
            await control.ConfigureAsync(config =>
            {
                config.RequireBearerToken(_key);
                if (code) { config.UseGpt41(); } else { config.UseGpt4OMini(); }
                config.UseChatScenario("installed-file-code", scenario => scenario
                    .ForModel(code ? LlmTestServerKnownModelIds.Gpt41 : LlmTestServerKnownModelIds.Gpt4OMini)
                    .RespondsWithItems(Items(code))
                    .RespondsWithItems(Items(code)).Responds("installed continuation complete"));
            }, cancellationToken);
            var first = (await sdk.CreateResponseAsync(Options(code), cancellationToken: cancellationToken)).Value;
            AssertOutput(first, code);
            var streamOptions = Options(code);
            streamOptions.StreamingEnabled = true;
            var updates = new List<StreamingResponseUpdate>();
            await foreach (var update in sdk.CreateResponseStreamingAsync(streamOptions, cancellationToken: cancellationToken))
            { updates.Add(update); }
            Require(updates.Select(update => update.SequenceNumber).SequenceEqual(Enumerable.Range(0, updates.Count)),
                "Installed file/code streaming must preserve contiguous event sequence numbers.");
            AssertOutput(updates.OfType<StreamingResponseCompletedUpdate>().Single().Response, code);
            if (code)
            {
                Require(updates.OfType<StreamingResponseCodeInterpreterCallInProgressUpdate>().Count() == 1
                    && updates.OfType<StreamingResponseCodeInterpreterCallInterpretingUpdate>().Count() == 1
                    && updates.OfType<StreamingResponseCodeInterpreterCallCompletedUpdate>().Count() == 1,
                    "Installed code interpreter must emit native progress events.");
                Require(updates.OfType<StreamingResponseCodeInterpreterCallCodeDeltaUpdate>().Count() == 2
                    && updates.OfType<StreamingResponseCodeInterpreterCallCodeDoneUpdate>().Count() == 1,
                    "Installed code interpreter must emit configured code deltas and the completed code.");
            }
            else
            {
                Require(updates.OfType<StreamingResponseFileSearchCallInProgressUpdate>().Count() == 1
                    && updates.OfType<StreamingResponseFileSearchCallSearchingUpdate>().Count() == 1
                    && updates.OfType<StreamingResponseFileSearchCallCompletedUpdate>().Count() == 1,
                    "Installed file search must emit native progress events.");
            }
            Require(updates.OfType<StreamingResponseOutputTextAnnotationAddedUpdate>().Count() == 1,
                "Installed native text must emit its citation exactly once.");
            var follow = Options(code);
            follow.ToolChoice = ResponseToolChoice.CreateAutoChoice();
            follow.PreviousResponseId = first.Id;
            var continued = (await sdk.CreateResponseAsync(follow, cancellationToken: cancellationToken)).Value;
            Require(((MessageResponseItem)continued.OutputItems.Single()).Content.Single().Text == "installed continuation complete",
                "Installed file/code response history must support previous_response_id.");
            var evidence = await control.GetRequestsAsync(cancellationToken: cancellationToken);
            Require(evidence.Requests.Count == 3 && evidence.Requests.All(trace => trace.IsSuccess == true),
                "Installed native requests must retain successful terminal evidence.");
            var streamed = evidence.Requests.Single(trace => trace.IsStreaming);
            Require(streamed.TerminalMarkerObserved == true && streamed.ResponseOutputItems.Count == 2
                && streamed.NativeTools.Count == 1 && streamed.ResponseToolCalls.Count == 0,
                "Native evidence must expose native declarations and outputs separately from client function calls.");
            var restored = evidence.Requests.Single(trace => trace.ResponsePreview?.Contains("installed continuation complete", StringComparison.Ordinal) == true);
            Require(restored.Messages.Any(message => message.Role == "assistant" && message.OutputItems.Any(item =>
                code ? item is LlmTestServerResponseCodeInterpreterItem : item is LlmTestServerResponseFileSearchItem)),
                "Installed public history must preserve typed native output items.");
        }
    }

    private static CreateResponseOptions Options(bool code)
    {
        var options = new CreateResponseOptions
        {
            Model = code ? LlmTestServerKnownModelIds.Gpt41 : LlmTestServerKnownModelIds.Gpt4OMini,
            InputItems = { ResponseItem.CreateUserMessageItem("installed native fixture") },
            ToolChoice = ResponseToolChoice.CreateRequiredChoice(),
        };
        if (code)
        {
            options.Tools.Add(ResponseTool.CreateCodeInterpreterTool(new CodeInterpreterToolContainer(
                CodeInterpreterToolContainerConfiguration.CreateAutomaticContainerConfiguration(["file_input"]))));
            options.IncludedProperties.Add(IncludedResponseProperty.CodeInterpreterCallOutputs);
        }
        else
        {
            options.Tools.Add(ResponseTool.CreateFileSearchTool(["vs_installed"], 1, null, null));
            options.IncludedProperties.Add(IncludedResponseProperty.FileSearchCallResults);
        }
        return options;
    }

    private static LlmTestServerResponseItem[] Items(bool code)
    {
        return code ?
        [
            new LlmTestServerResponseCodeInterpreterItem
            {
                Id = "ci_installed", ContainerId = "cntr_installed", InputFileIds = ["file_input"], GeneratedFileIds = ["file_generated"],
                Code = "print(42)", CodeChunks = ["print(", "42)"],
                Outputs = [new LlmTestServerCodeInterpreterLogsOutput { Logs = "42\n" },
                    new LlmTestServerCodeInterpreterImageOutput { Url = "https://example.com/installed-chart.png" }],
            },
            new LlmTestServerResponseMessageItem
            {
                Id = "msg_installed_code", Content = "Chart fixture.",
                Citations = [new LlmTestServerContainerFileCitation
                { ContainerId = "cntr_installed", FileId = "file_generated", Filename = "chart.png", StartIndex = 0, EndIndex = 0 }],
            },
        ] :
        [
            new LlmTestServerResponseFileSearchItem
            {
                Id = "fs_installed", VectorStoreIds = ["vs_installed"], Queries = ["installed fixture"],
                Results = [new() { FileId = "file_installed", Filename = "fixture.txt", Text = "Installed fixture.", Score = 0.9 }],
            },
            new LlmTestServerResponseMessageItem
            {
                Id = "msg_installed_file", Content = "File fixture.",
                Citations = [new LlmTestServerFileCitation { FileId = "file_installed", Filename = "fixture.txt", Index = 4 }],
            },
        ];
    }

    private static void AssertOutput(ResponseResult response, bool code)
    {
        Require(response.OutputItems.Count == 2 && !response.OutputItems.OfType<FunctionCallResponseItem>().Any(),
            "File/code output must remain native output items.");
        var text = (MessageResponseItem)response.OutputItems[1];
        if (code)
        {
            var execution = (CodeInterpreterCallResponseItem)response.OutputItems[0];
            Require(execution.Id == "ci_installed" && execution.ContainerId == "cntr_installed" && execution.Code == "print(42)"
                && execution.Outputs.OfType<CodeInterpreterCallLogsOutput>().Single().Logs == "42\n"
                && execution.Outputs.OfType<CodeInterpreterCallImageOutput>().Single().ImageUri.AbsoluteUri == "https://example.com/installed-chart.png",
                "Official installed SDK must parse code, logs and image output.");
            var citation = (ContainerFileCitationMessageAnnotation)text.Content.Single().OutputTextAnnotations.Single();
            Require(citation.ContainerId == "cntr_installed" && citation.FileId == "file_generated"
                && citation.StartIndex == 0 && citation.EndIndex == 0,
                "Official installed SDK must preserve zero-length container file citations.");
        }
        else
        {
            var search = (FileSearchCallResponseItem)response.OutputItems[0];
            var result = search.Results.Single();
            Require(search.Id == "fs_installed" && search.Queries.Single() == "installed fixture"
                && result.FileId == "file_installed" && result.Filename == "fixture.txt" && result.Text == "Installed fixture.",
                "Official installed SDK must parse included file search results.");
            var citation = (FileCitationMessageAnnotation)text.Content.Single().OutputTextAnnotations.Single();
            Require(citation.FileId == "file_installed" && citation.Filename == "fixture.txt" && citation.Index == 4,
                "Official installed SDK must preserve file citation insertion indexes.");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) { throw new InvalidOperationException(message); }
    }
}
#pragma warning restore OPENAI001
