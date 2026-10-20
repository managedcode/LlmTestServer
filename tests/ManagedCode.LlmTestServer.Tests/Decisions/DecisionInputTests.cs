using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.OpenAI;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class DecisionInputTests
{
    [Test]
    [Arguments("[true]")]
    [Arguments("[{}]")]
    [Arguments("[{\"role\":\"system\",\"content\":\"text\"}]")]
    [Arguments("[{\"role\":\"user\",\"type\":\"other\",\"content\":\"text\"}]")]
    [Arguments("[{\"role\":\"user\",\"content\":false}]")]
    [Arguments("[{\"role\":\"user\",\"content\":[{\"type\":\"input_audio\"}]}]")]
    [Arguments("[{\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":false}]}]")]
    [Arguments("[{\"role\":\"user\",\"content\":[{\"type\":\"input_image\",\"image_url\":\"https://example.com/image.png\"}]}]")]
    [Arguments("[{\"role\":\"user\",\"content\":[{\"type\":\"input_image\",\"image_url\":\"data:image/png;base64,!!!!\"}]}]")]
    [Arguments("[{\"role\":\"user\",\"content\":[{\"type\":\"input_image\",\"image_url\":\"data:image/png;base64,YQ==\",\"detail\":\"other\"}]}]")]
    public async Task OpenAiDecisionInput_RejectsUnsupportedMessageAndImageShapesAsync(string input)
    {
        var body = DecisionEndpointTests.Body("gpt-6-luna"); body["input"] = JsonNode.Parse(input);
        using var host = await LlmTestServerTestHost.StartAsync(); using var client = host.GetTestClient();
        using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments("low")]
    [Arguments("high")]
    [Arguments("auto")]
    [Arguments("original")]
    [Arguments(null)]
    public async Task OpenAiDecisionInput_AcceptsTextAndEmbeddedImagesAsync(string? detail)
    {
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["input"] = JsonNode.Parse("""[{"role":"user","type":"message","content":[{"type":"input_text","text":"evidence"},{"type":"input_image","image_url":"data:image/png;base64,YQ=="}]}]""");
        body["input"]![0]!["content"]![1]!["detail"] = detail;
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna").AddDecisionScenario(DecisionEndpointTests.Scenario("gpt-6-luna")));
        using var client = host.GetTestClient(); using var response = await client.PostAsJsonAsync(DecisionEndpointTests.PathFor("gpt-6-luna"), body);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task OpenAiDecisionInput_EnforcesAggregateImageLimitAsync()
    {
        var image = JsonNode.Parse("""{"type":"input_image","image_url":"data:image/png;base64,YQ=="}""")!;
        var input = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(Enumerable.Range(0, OpenAiDecisionInputValidation.MaxImages).Select(_ => image.DeepClone()).ToArray()) });
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder.AddOpenAiDecisionModel("gpt-6-luna")
            .AddDecisionScenario(DecisionEndpointTests.Scenario("gpt-6-luna")));
        using var http = host.GetTestClient();
        var body = DecisionEndpointTests.Body("gpt-6-luna");
        body["input"] = input;
        using var accepted = await http.PostAsJsonAsync("/openai/v1/decisions", body);
        accepted.EnsureSuccessStatusCode();
        input.Add(new JsonObject { ["role"] = "user", ["content"] = new JsonArray(image.DeepClone()) });
        using var rejected = await http.PostAsJsonAsync("/openai/v1/decisions", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
}
