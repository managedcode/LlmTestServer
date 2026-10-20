using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.OpenAI;

public sealed partial class NativeWebSearchIntegrationTests
{
    [Test]
    [Arguments("oversizedGroup")]
    [Arguments("duplicateOutputId")]
    [Arguments("duplicateUncitedOutputId")]
    [Arguments("oversizedCallId")]
    [Arguments("tooManyQueries")]
    [Arguments("tooManySources")]
    [Arguments("tooManyCitations")]
    [Arguments("zeroLengthCitation")]
    public async Task InvalidExplicitNativeHistory_Returns400AndPreservesNextFixtureAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        await new LlmTestServerClient(http).ConfigureAsync(Configuration(2));
        var first = await CompleteAsync(http);
        var output = JsonNode.Parse(first.GetProperty("output").GetRawText())!.AsArray();
        var input = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "start" }, output[0]!.DeepClone(), output[1]!.DeepClone());
        var search = input[1]!;
        var message = input[2]!;
        var content = message["content"]![0]!;
        switch (defect)
        {
            case "oversizedGroup":
                for (var index = 2; index < 129; index++)
                {
                    var extra = message.DeepClone(); extra["id"] = $"msg_bounded_{index}"; input.Add(extra);
                }
                break;
            case "duplicateOutputId": message["id"] = search["id"]!.GetValue<string>(); break;
            case "duplicateUncitedOutputId": message["id"] = search["id"]!.GetValue<string>(); content["annotations"] = new JsonArray(); break;
            case "oversizedCallId": search["id"] = new string('x', 129); break;
            case "tooManyQueries": search["action"]!["queries"] = new JsonArray(Enumerable.Range(0, 33).Select(index => JsonValue.Create($"query {index}")).ToArray()); break;
            case "tooManySources": search["action"]!["sources"] = new JsonArray(Enumerable.Range(0, 129).Select(_ => (JsonNode)new JsonObject { ["type"] = "url", ["url"] = _sourceUrl }).ToArray()); break;
            case "tooManyCitations": content["annotations"] = new JsonArray(Enumerable.Range(0, 129).Select(_ => content["annotations"]![0]!.DeepClone()).ToArray()); break;
            case "zeroLengthCitation": content["annotations"]![0]!["end_index"] = 0; break;
        }
        input.Add(new JsonObject { ["role"] = "user", ["content"] = "follow" });
        var body = Request(); body["input"] = input;
        using var rejected = await http.PostAsJsonAsync("/openai/v1/responses", body);
        await Assert.That(rejected.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertWireAsync(await CompleteAsync(http));
    }
}
