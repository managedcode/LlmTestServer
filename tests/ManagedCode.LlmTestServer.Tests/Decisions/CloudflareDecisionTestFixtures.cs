using System.Text.Json.Nodes;
using ManagedCode.LlmTestServer.Decisions;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

internal static class CloudflareDecisionTestFixtures
{
    internal const string _png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGNgYGAAAAAEAAH2FzhVAAAAAElFTkSuQmCC";
    private const string _jpeg = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFAEBAAAAAAAAAAAAAAAAAAAAAP/EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAMAwEAAhEDEQA/AJ/AB//Z";
    private const string _webp = "UklGRiQAAABXRUJQVlA4IBgAAAAwAQCdASoBAAEAAUAmJaQAA3AA/v02aAA=";

    internal static JsonObject Body(string model)
    {
        return new()
        {
            ["model"] = model,
            ["state"] = new JsonObject { ["evidence"] = "same explicit evidence" },
            ["questions"] = new JsonObject { ["binary"] = new JsonObject { ["type"] = "noul", ["instructions"] = "Does the evidence suffice?" } },
        };
    }

    internal static LlmTestServerDecisionScenario Scenario(string model)
    {
        return new()
        {
            Id = "native-cloudflare-contract-" + model,
            ModelId = model,
            Answers = new(StringComparer.Ordinal) { ["binary"] = new() { Kind = LlmTestServerDecisionKind.Predicate, Probability = 0.75 } },
            Usage = new() { InputTokens = 100, OutputTokens = 0, TotalTokens = 100 },
        };
    }

    internal static JsonNode Image(string contentType, bool embedded)
    {
        var base64 = contentType switch { "image/jpeg" => _jpeg, "image/webp" => _webp, _ => _png };
        return embedded ? new JsonObject { ["content_type"] = contentType, ["base64"] = base64 }
            : JsonValue.Create("data:" + contentType + ";base64," + base64);
    }
}
