using System.Text.Json;
using ManagedCode.LlmTestServer.SystemOne;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace ManagedCode.LlmTestServer.Hosting;

internal static class TypeSafeMultipartRequestReader
{
    private const string _requestPart = "request";
    private const int _bufferBytes = 64 * 1024;
    private const int _maxImageBytes = 16 * 1024 * 1024;
    private static readonly HashSet<string> _imageContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/png", "image/jpeg", "image/jpg", "application/octet-stream" };

    internal static async Task<(SystemOneDecisionRequest? Request, TypeSafeValidationResult? Error)> ReadAsync(
        HttpRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var media) || !media.Boundary.HasValue)
            { return Failure("Multipart requests need a boundary."); }
            var body = await ReadBoundedAsync(request.Body, TypeSafeRequestValidation.MaxRequestBytes, cancellationToken).ConfigureAwait(false);
            if (body is null) { return Failure("TypeSafe multipart body exceeds 32 MiB.", StatusCodes.Status413PayloadTooLarge); }
            using var stream = new MemoryStream(body, writable: false);
            var reader = new MultipartReader(HeaderUtilities.RemoveQuotes(media.Boundary).Value!, stream);
            var first = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false);
            if (first is null || PartName(first) != _requestPart || !IsJson(first.ContentType))
            { return Failure("The first multipart part must be named request with application/json content."); }
            var json = await JsonSerializer.DeserializeAsync<JsonElement>(first.Body, cancellationToken: cancellationToken).ConfigureAwait(false);
            var shape = SystemOneDecisionMapper.Validate(json, Decisions.LlmTestServerDecisionProvider.TypeSafe);
            if (!shape.IsValid) { return Failure(shape.Error!, StatusCodes.Status422UnprocessableEntity); }
            if (json.TryGetProperty("assets", out var declared) && declared.ValueKind == JsonValueKind.Object
                && declared.EnumerateObject().Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != declared.EnumerateObject().Count())
            { return Failure("Image references must be unique."); }
            var payload = json.Deserialize<SystemOneDecisionRequest>();
            if (payload?.Assets is null) { return Failure("Multipart requests require an assets map."); }
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            MultipartSection? section;
            while ((section = await reader.ReadNextSectionAsync(cancellationToken).ConfigureAwait(false)) is not null)
            {
                var name = PartName(section);
                if (string.IsNullOrEmpty(name) || name == _requestPart || files.ContainsKey(name) || !IsImage(section.ContentType))
                { return Failure("Image part names must be unique and use a supported image content type."); }
                var bytes = await ReadBoundedAsync(section.Body, _maxImageBytes, cancellationToken).ConfigureAwait(false);
                if (bytes is null) { return Failure("TypeSafe multipart image exceeds 16 MiB.", StatusCodes.Status413PayloadTooLarge); }
                files.Add(name, bytes);
            }
            return Normalize(payload, files);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or IOException or ArgumentException)
        { return Failure("Malformed TypeSafe multipart request."); }
    }

    private static (SystemOneDecisionRequest? Request, TypeSafeValidationResult? Error) Normalize(
        SystemOneDecisionRequest request, Dictionary<string, byte[]> files)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var assets = new Dictionary<string, SystemOneImageAsset>(StringComparer.Ordinal);
        foreach (var (reference, asset) in request.Assets!)
        {
            if (asset is null) { return Failure("Image assets cannot be null."); }
            if (asset.Part is not { } part) { assets.Add(reference, asset); continue; }
            if (string.IsNullOrEmpty(part) || !used.Add(part) || !files.TryGetValue(part, out var bytes))
            { return Failure("Each image part must belong to exactly one asset and must exist."); }
            var format = asset.Format ?? DetectFormat(bytes);
            assets.Add(reference, asset with { Part = null, Format = format, DataBase64 = Convert.ToBase64String(bytes) });
        }
        return used.Count != files.Count ? Failure("Every multipart image must be named by an asset.")
            : (request with { Assets = assets }, null);
    }

    private static string? DetectFormat(ReadOnlySpan<byte> bytes)
    {
        const byte pngStart = 0x89;
        const byte jpegStart = 0xff;
        const byte jpegMarker = 0xd8;
        return bytes.Length >= 2 && bytes[0] == pngStart ? "png"
            : bytes.Length >= 2 && bytes[0] == jpegStart && bytes[1] == jpegMarker ? "jpg" : null;
    }

    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, int limit, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[_bufferBytes];
        int count;
        while ((count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + count > limit) { return null; }
            await buffer.WriteAsync(chunk.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
        return buffer.ToArray();
    }

    private static string? PartName(MultipartSection section)
    {
        return ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            && disposition.DispositionType == "form-data" ? HeaderUtilities.RemoveQuotes(disposition.Name).Value : null;
    }

    private static bool IsJson(string? contentType)
    {
        return MediaTypeHeaderValue.TryParse(contentType, out var value)
            && string.Equals(value.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImage(string? contentType)
    {
        return contentType is null
            || MediaTypeHeaderValue.TryParse(contentType, out var value) && _imageContentTypes.Contains(value.MediaType.Value!);
    }

    private static (SystemOneDecisionRequest?, TypeSafeValidationResult?) Failure(string error, int status = StatusCodes.Status400BadRequest)
    {
        return (null, new(error, status));
    }
}
