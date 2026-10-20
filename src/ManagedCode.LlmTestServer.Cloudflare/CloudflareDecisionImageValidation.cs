using System.Text;
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;

namespace ManagedCode.LlmTestServer.Cloudflare;

public static class CloudflareDecisionImageValidation
{
    public const int MaxImages = 4;
    public const int MaxImageBytes = 4 * 1024 * 1024;
    public const int MaxTotalImageBytes = 8 * 1024 * 1024;
    public const int MaxRequestBytes = 13 * 1024 * 1024;
    public const int MaxImagePixels = 16_000_000;
    private const int _maxBase64Characters = ((MaxImageBytes + 2) / 3) * 4;
    private const string _dataPrefix = "data:";
    private const string _base64Suffix = ";base64";
    private const string _png = "image/png";
    private const string _jpeg = "image/jpeg";
    private const string _webp = "image/webp";

    public static CloudflareDecisionValidationResult Validate(JsonElement body)
    {
        if (Encoding.UTF8.GetByteCount(body.GetRawText()) > MaxRequestBytes) { return TooLarge("Clef request body exceeds 13 MiB."); }
        if (!body.TryGetProperty("images", out var images)) { return new(); }
        if (images.ValueKind != JsonValueKind.Array) { return Invalid("Clef images must be an array."); }
        if (images.GetArrayLength() > MaxImages) { return Invalid("Clef accepts at most four images."); }
        long totalBytes = 0;
        foreach (var value in images.EnumerateArray())
        {
            CloudflareDecisionImage? image;
            try { image = value.Deserialize<CloudflareDecisionImage>(); }
            catch (JsonException) { return Invalid("Clef images require a data URL or content_type/base64 object."); }
            if (image is null || !TryGetContent(image, out var contentType, out var base64))
            { return Invalid("Clef accepts embedded PNG, JPEG or WebP images; remote URLs are not accepted."); }
            var result = ValidateContent(contentType, base64, out var byteCount);
            if (!result.IsValid) { return result; }
            totalBytes += byteCount;
            if (totalBytes > MaxTotalImageBytes) { return TooLarge("Clef decoded images exceed 8 MiB in total."); }
        }
        return new();
    }

    private static bool TryGetContent(CloudflareDecisionImage image, out string contentType, out string base64)
    {
        contentType = string.Empty; base64 = string.Empty;
        if (image.Embedded is { } embedded)
        { contentType = embedded.ContentType; base64 = embedded.Base64; return IsAllowed(contentType) && base64 is not null; }
        var value = image.DataUrl;
        if (value is null || !value.StartsWith(_dataPrefix, StringComparison.OrdinalIgnoreCase)) { return false; }
        var comma = value.IndexOf(',');
        if (comma < _dataPrefix.Length) { return false; }
        var header = value[_dataPrefix.Length..comma];
        if (!header.EndsWith(_base64Suffix, StringComparison.OrdinalIgnoreCase)) { return false; }
        contentType = header[..^_base64Suffix.Length]; base64 = value[(comma + 1)..];
        return IsAllowed(contentType);
    }

    private static CloudflareDecisionValidationResult ValidateContent(string contentType, string base64, out int byteCount)
    {
        byteCount = 0;
        if (base64.Count(character => !char.IsWhiteSpace(character)) > _maxBase64Characters)
        { return TooLarge("A Clef decoded image exceeds 4 MiB."); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return Invalid("Clef image data is not valid base64."); }
        byteCount = bytes.Length;
        if (byteCount > MaxImageBytes) { return TooLarge("A Clef decoded image exceeds 4 MiB."); }
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var metadata = ImageMetadataReader.ReadMetadata(stream);
            var actualType = metadata.OfType<FileTypeDirectory>().Single().GetString(FileTypeDirectory.TagDetectedFileMimeType);
            if (!string.Equals(contentType, actualType, StringComparison.OrdinalIgnoreCase) || metadata.Any(directory => directory.HasError))
            { return Invalid("Clef image bytes must match their declared PNG, JPEG or WebP content type."); }
            var dimensions = Dimensions(metadata);
            if (dimensions.Width <= 0 || dimensions.Height <= 0) { return Invalid("Clef image dimensions are invalid."); }
            return (long)dimensions.Width * dimensions.Height > MaxImagePixels
                ? TooLarge("A Clef image exceeds 16 megapixels.") : new();
        }
        catch (Exception exception) when (exception is ImageProcessingException or MetadataException or IOException or InvalidOperationException)
        { return Invalid("Clef image bytes are malformed or unsupported."); }
    }

    private static (int Width, int Height) Dimensions(IReadOnlyList<MetadataExtractor.Directory> metadata)
    {
        if (metadata.OfType<PngDirectory>().FirstOrDefault(directory => directory.ContainsTag(PngDirectory.TagImageWidth)) is { } png)
        { return (png.GetInt32(PngDirectory.TagImageWidth), png.GetInt32(PngDirectory.TagImageHeight)); }
        if (metadata.OfType<JpegDirectory>().FirstOrDefault() is { } jpeg)
        { return (jpeg.GetImageWidth(), jpeg.GetImageHeight()); }
        if (metadata.OfType<WebPDirectory>().FirstOrDefault() is { } webp)
        { return (webp.GetInt32(WebPDirectory.TagImageWidth), webp.GetInt32(WebPDirectory.TagImageHeight)); }
        return default;
    }

    private static bool IsAllowed(string? contentType)
    {
        return contentType is not null
        && (_png.Equals(contentType, StringComparison.OrdinalIgnoreCase) || _jpeg.Equals(contentType, StringComparison.OrdinalIgnoreCase)
            || _webp.Equals(contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static CloudflareDecisionValidationResult Invalid(string error)
    {
        return new(error);
    }

    private static CloudflareDecisionValidationResult TooLarge(string error)
    {
        return new(error, 413, CloudflareDecisionErrors.RequestTooLarge);
    }
}
