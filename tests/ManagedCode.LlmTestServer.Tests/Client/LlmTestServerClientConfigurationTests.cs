using System.Net.Http.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Models;
using ManagedCode.LlmTestServer.Tests.TestSupport;
using Microsoft.Extensions.AI;

namespace ManagedCode.LlmTestServer.Tests.Client;

public sealed class LlmTestServerClientConfigurationTests
{
    [Test]
    public async Task ClientBuilder_ExposesOfficialModelConvenienceMethodsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseDefaultOpenAiModels().UseReasoningChatModel("gpt-5-nano", 11));
        var configuration = new LlmTestServerConfiguration
        {
            Models = (await http.GetFromJsonAsync<List<LlmTestServerModel>>("/admin-api/models"))!,
        };

        await Assert.That(configuration.Models.Any(model => model.Id == LlmTestServerKnownModelIds.Gpt41Mini))
            .IsTrue();
        await Assert.That(configuration.Models.Any(model => model.Id == LlmTestServerKnownModelIds.TextEmbedding3Small))
            .IsTrue();
        await Assert.That(configuration.Models.Any(model => model.Id == LlmTestServerKnownModelIds.GptImage1))
            .IsTrue();
        await Assert.That(configuration.Models.Any(model => model.Id == LlmTestServerKnownModelIds.Gpt4OMiniTts))
            .IsTrue();
        await Assert.That(configuration.Models.Single(model => model.Id == "gpt-5-nano").ReasoningTokens)
            .IsEqualTo(11);
    }

    [Test]
    public async Task ClientBuilder_ExposesKnownOpenAiModelConvenienceMethodsAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        var control = new LlmTestServerClient(http);
        await control.ConfigureAsync(config => config.UseKnownOpenAiModels(reasoningTokens: 19));
        var configuration = new LlmTestServerConfiguration
        {
            Models = (await http.GetFromJsonAsync<List<LlmTestServerModel>>("/admin-api/models"))!,
        };

        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.Gpt55, LlmTestServerModelKind.Chat))
            .IsTrue();
        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.Gpt54Nano, LlmTestServerModelKind.Chat))
            .IsTrue();
        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.TextEmbedding3Large, LlmTestServerModelKind.Embedding))
            .IsTrue();
        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.GptImage2, LlmTestServerModelKind.Image))
            .IsTrue();
        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.Tts1Hd, LlmTestServerModelKind.Audio))
            .IsTrue();
        await Assert.That(HasModel(configuration, LlmTestServerKnownModelIds.Sora2Pro, LlmTestServerModelKind.Video))
            .IsTrue();
        await Assert.That(configuration.Models.Single(model => model.Id == LlmTestServerKnownModelIds.Gpt55).ReasoningTokens)
            .IsEqualTo(19);
    }

    [Test]
    public async Task ConfigureAsync_WithFluentClientApi_LoadsDatasetAndFixturesAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var httpClient = host.GetTestClient();
        var control = LlmTestServerClient.Create(httpClient, "test-key");

        await control.ConfigureAsync(
            tck => tck
                .RequireBearerToken("test-key")
                .UseChatModel("dataset-chat")
                .UseEmbeddingModel("dataset-embedding")
                .UseImageModel("dataset-image")
                .UseAudioModel("dataset-audio")
                .UseVideoModel("dataset-video")
                .UseEmbeddingVector(0.9f, 0.8f)
                .UseImageDataUri("data:image/png;base64,Zm9v")
                .UseAudio([1, 2, 3, 4], "audio/test")
                .UseVideo([0, 0, 0, 24, 102, 116, 121, 112], "video/test")
                .UseTranscriptionText("dataset transcript")
                .UseTranslationText("dataset translation")
                .UseDataset(
                    "support-flow",
                    dataset => dataset
                        .AddChatScenario(
                            "dataset-blue",
                            scenario => scenario
                                .ForModel("dataset-chat")
                                .WhenUserContains("support")
                                .Responds("dataset answer", "dataset ", "answer")
                                .Responds("dataset answer", "dataset ", "answer")
                        )
                ),
            resetFirst: true
        );

        using var chatClient = control.CreateChatClient("dataset-chat");
        using var embeddingGenerator = control.CreateEmbeddingGenerator("dataset-embedding");
        using var imageGenerator = control.CreateImageGenerator("dataset-image");

        var chat = await chatClient.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "support request")]
        );
        var streamChunks = new List<string>();
        await foreach (
            var update in chatClient.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "support request")]
            )
        )
        {
            streamChunks.Add(update.Text);
        }

        var embeddings = await embeddingGenerator.GenerateAsync(["alpha"]);
        var image = await imageGenerator.GenerateAsync(
            new ImageGenerationRequest { Prompt = "fixture prompt" }
        );
        var audio = await control.GenerateAudioAsync("dataset-audio", "say this");
        var videoId = await control.GenerateVideoAsync("dataset-video", "make a fixture video");
        var transcription = await control.TranscribeAudioAsync(
            "dataset-audio",
            [82, 73, 70, 70],
            "fixture.wav"
        );
        var translation = await control.TranslateAudioAsync(
            "dataset-audio",
            [82, 73, 70, 70],
            "fixture.wav"
        );
        var assertions = await control.GetAssertionsAsync();

        await Assert.That(chat.Text).IsEqualTo("dataset answer");
        await Assert.That(string.Concat(streamChunks)).IsEqualTo("dataset answer");
        await Assert.That(embeddings).Count().IsEqualTo(1);
        await Assert.That(embeddings[0].Vector.ToArray()).IsEquivalentTo([0.9f, 0.8f]);
        await Assert.That(image.Contents).Count().IsEqualTo(1);
        await Assert.That(audio.Bytes).IsEquivalentTo((byte[])[1, 2, 3, 4]);
        await Assert.That(audio.MediaType).IsEqualTo("audio/test");
        await Assert.That(videoId).IsEqualTo("video_llm_tck");
        await Assert.That(transcription).IsEqualTo("dataset transcript");
        await Assert.That(translation).IsEqualTo("dataset translation");
        await Assert.That(assertions.Matched).IsGreaterThanOrEqualTo(8);
    }

    [Test]
    public async Task IndividualModelPresets_ServeEveryConfiguredModalityAsync()
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(config =>
        {
            config.UseGpt55(7).UseGpt55Pro(7).UseGpt54(7).UseGpt54Pro(7).UseGpt54Mini(7).UseGpt54Nano(7)
                .UseGpt5(7).UseGpt5Pro(7).UseGpt5Mini(7).UseGpt5Nano(7).UseGpt41().UseGpt41Mini().UseGpt4OMini()
                .UseTextEmbedding3Small().UseTextEmbedding3Large().UseTextEmbeddingAda002()
                .UseGptImage2().UseGptImage15().UseGptImage1().UseGptImage1Mini()
                .UseGpt4OMiniTts().UseTts1().UseTts1Hd().UseSora2().UseSora2Pro();
            string[] chatModels = [LlmTestServerKnownModelIds.Gpt55, LlmTestServerKnownModelIds.Gpt55Pro,
                LlmTestServerKnownModelIds.Gpt54, LlmTestServerKnownModelIds.Gpt54Pro, LlmTestServerKnownModelIds.Gpt54Mini,
                LlmTestServerKnownModelIds.Gpt54Nano, LlmTestServerKnownModelIds.Gpt5, LlmTestServerKnownModelIds.Gpt5Pro,
                LlmTestServerKnownModelIds.Gpt5Mini, LlmTestServerKnownModelIds.Gpt5Nano, LlmTestServerKnownModelIds.Gpt41,
                LlmTestServerKnownModelIds.Gpt41Mini, LlmTestServerKnownModelIds.Gpt4OMini];
            foreach (var id in chatModels)
            {
                config.UseChatScenario(id, scenario => scenario.ForModel(id).Responds("preset response"));
            }
        });
        var models = (await http.GetFromJsonAsync<List<LlmTestServerModel>>("/admin-api/models"))!;
        await Assert.That(models.Count).IsEqualTo(25);
        foreach (var model in models)
        {
            var route = model.Kind switch
            {
                LlmTestServerModelKind.Chat => "chat/completions",
                LlmTestServerModelKind.Embedding => "embeddings",
                LlmTestServerModelKind.Image => "images/generations",
                LlmTestServerModelKind.Audio => "audio/speech",
                LlmTestServerModelKind.Video => "videos",
                _ => throw new InvalidOperationException("Unexpected model kind returned by server.")
            };
            using var response = await http.PostAsJsonAsync("/openai/v1/" + route, new
            {
                model = model.Id,
                input = "preset input",
                prompt = "preset image",
                voice = "alloy",
                messages = new[] { new { role = "user", content = "preset input" } }
            });
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Model {model.Id} returned {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
            if (model.Kind == LlmTestServerModelKind.Chat)
            {
                var json = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                await Assert.That(json.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()).IsEqualTo("preset response");
                if (model.ReasoningTokens > 0)
                {
                    await Assert.That(json.GetProperty("usage").GetProperty("completion_tokens_details").GetProperty("reasoning_tokens").GetInt32()).IsEqualTo(7);
                }
            }
            else
            {
                await Assert.That((await response.Content.ReadAsByteArrayAsync()).Length).IsGreaterThan(0);
            }
        }
    }

    private static bool HasModel(
        LlmTestServerConfiguration configuration,
        string id,
        LlmTestServerModelKind kind
    )
    {
        return configuration.Models.Any(model => model.Id == id && model.Kind == kind);
    }
}
