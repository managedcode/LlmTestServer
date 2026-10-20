using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Client;
using ManagedCode.LlmTestServer.Decisions;
using ManagedCode.LlmTestServer.SystemOne;
using ManagedCode.LlmTestServer.Tests.TestSupport;

namespace ManagedCode.LlmTestServer.Tests.Decisions;

public sealed class Jev114MultipartTests
{
    private const string _path = "/systemone/v1/systemone";
    private const string _part = "photo-file";
    private const string _key = "jev-client-fixture-key";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Jev114_TypedClientSupportsJsonAndMultipartImagesAsync(bool multipart)
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder
            .AddDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe)
            .RequireBearerToken(_key).AddDecisionScenario(Jev114EndpointTests.Scenario(false)));
        using var http = host.GetTestClient();
        var client = new LlmTestServerClient(http, _key).CreateSystemOneClient();
        var request = Request(multipart);
        var response = multipart
            ? await client.EvaluateMultipartAsync(request, [new() { PartName = _part, Data = Image(), ContentType = "IMAGE/PNG" }], enableImages: true, enableLongContext: true)
            : await client.EvaluateAsync(request, enableImages: true, enableLongContext: true);
        await Assert.That(response.Answers["target"].Box!.Left).IsEqualTo(0.44);
        await Assert.That(response.Answers["rating"].Legend!["0"].GetProperty("example").GetString()).IsEqualTo("photo-reference");
        await Assert.That(response.Answers["target"].Confidence).IsEqualTo(0.92);
        await Assert.That(response.Usage.InputTokens).IsEqualTo(290);
    }

    [Test]
    [Arguments("first-part")]
    [Arguments("duplicate-part")]
    [Arguments("missing-part")]
    [Arguments("unused-part")]
    [Arguments("shared-part")]
    [Arguments("reserved-part")]
    [Arguments("unsupported-type")]
    [Arguments("missing-header")]
    [Arguments("malformed-json")]
    public async Task Jev114_MultipartRejectsNativePartViolationsAsync(string defect)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        if (defect != "missing-header") { http.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled); }
        var request = Request(true);
        if (defect == "shared-part") { request.Assets!["second-reference"] = request.Assets["photo-reference"]; }
        using var content = new MultipartFormDataContent();
        if (defect == "first-part") { content.Add(File(), _part, "photo.png"); }
        content.Add(defect == "malformed-json" ? new StringContent("{", Encoding.UTF8, "application/json") : JsonContent.Create(request), "request");
        if (defect != "missing-part") { content.Add(File(defect == "unsupported-type" ? "image/webp" : "image/png"), _part, "photo.png"); }
        if (defect is "duplicate-part" or "unused-part" or "reserved-part")
        { content.Add(File(), defect == "duplicate-part" ? _part : defect == "reserved-part" ? "request" : "unused", "other.png"); }
        using var response = await http.PostAsync(_path, content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Jev114_MultipartEnforcesPhysicalBodyAndImageByteLimitsAsync(bool wholeBody)
    {
        using var host = await LlmTestServerTestHost.StartAsync();
        using var http = host.GetTestClient();
        http.DefaultRequestHeaders.Add(TypeSafeRequestValidation.ImagesHeader, TypeSafeRequestValidation.Enabled);
        using var content = new MultipartFormDataContent();
        content.Add(JsonContent.Create(Request(true)), "request");
        content.Add(new ByteArrayContent(new byte[(wholeBody ? TypeSafeRequestValidation.MaxRequestBytes : 16 * 1024 * 1024) + 1]), _part, "photo.png");
        using var response = await http.PostAsync(_path, content);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.RequestEntityTooLarge);
    }

    [Test]
    public async Task Jev114_MultipartInfersFormatAndPrefersPartOverBase64Async()
    {
        using var host = await LlmTestServerTestHost.StartAsync(builder => builder
            .AddDecisionModel(TypeSafeRequestValidation.Jev114Model, LlmTestServerDecisionProvider.TypeSafe).AddDecisionScenario(Jev114EndpointTests.Scenario(false)));
        using var http = host.GetTestClient();
        var request = Request(true);
        request.Assets!["photo-reference"] = request.Assets["photo-reference"] with { Format = null, DataBase64 = "ignored-invalid-base64" };
        var response = await new LlmTestServerClient(http).CreateSystemOneClient().EvaluateMultipartAsync(request,
            [new() { PartName = _part, Data = Image() }], enableImages: true);
        await Assert.That(response.Answers["target"].Box!.Right).IsEqualTo(0.84);
    }

    private static SystemOneDecisionRequest Request(bool multipart)
    {
        var request = Jev114EndpointTests.Body(false).Deserialize<SystemOneDecisionRequest>()!;
        if (multipart) { request.Assets!["photo-reference"] = request.Assets["photo-reference"] with { Part = _part, DataBase64 = null }; }
        return request;
    }

    private static byte[] Image()
    {
        return Convert.FromBase64String(Jev114EndpointTests.Body(false)["assets"]!["photo-reference"]!["data_base64"]!.GetValue<string>());
    }

    private static ByteArrayContent File(string media = "image/png")
    {
        var file = new ByteArrayContent(Image()); file.Headers.ContentType = new MediaTypeHeaderValue(media); return file;
    }
}
