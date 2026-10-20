using System.Buffers.Binary;
using System.Text.Json;
using MetadataExtractor;
using MetadataExtractor.Formats.FileType;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;

namespace ManagedCode.LlmTestServer.SystemOne;

internal static class TypeSafeImageValidation
{
    private const int _maxImageBytes = 16 * 1024 * 1024;
    private const int _minSide = 280;
    private const int _maxSide = 1568;
    private const int _minPixels = 78400;
    private const int _maxPixels = 2458624;

    internal static TypeSafeValidationResult Validate(JsonElement asset, out int byteCount)
    {
        byteCount = 0;
        if (asset.ValueKind != JsonValueKind.Object
            || (asset.TryGetProperty("type", out var type) && (type.ValueKind != JsonValueKind.String || type.GetString() != "image"))
            || asset.TryGetProperty("part", out _)
            || !asset.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String
            || format.GetString() is not ("png" or "jpg" or "jpeg")
            || !asset.TryGetProperty("data_base64", out var data) || data.ValueKind != JsonValueKind.String)
        { return new("JSON assets require image format and data_base64; part references require a multipart request."); }
        var base64 = data.GetString()!;
        if (base64.Any(char.IsWhiteSpace)) { return new("Image base64 must not contain whitespace."); }
        if (base64.Length > ((_maxImageBytes + 2) / 3) * 4) { return new("Image exceeds 16 MiB.", 413); }
        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return new("Image base64 is invalid."); }
        byteCount = bytes.Length;
        if (byteCount > _maxImageBytes) { return new("Image exceeds 16 MiB.", 413); }
        if (!string.Equals(Convert.ToBase64String(bytes), base64, StringComparison.Ordinal)) { return new("Image base64 must be canonical and padded."); }
        if (format.GetString() == "png" && !IsStillPng(bytes)) { return new("PNG assets must be complete still images, not animated PNGs."); }
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var metadata = ImageMetadataReader.ReadMetadata(stream);
            var actual = metadata.OfType<FileTypeDirectory>().Single().GetString(FileTypeDirectory.TagDetectedFileMimeType);
            var expected = format.GetString() == "png" ? "image/png" : "image/jpeg";
            if (actual != expected || metadata.Any(directory => directory.HasError)) { return new("Image bytes do not match the declared format."); }
            var png = metadata.OfType<PngDirectory>().FirstOrDefault(directory => directory.ContainsTag(PngDirectory.TagImageWidth));
            var jpeg = metadata.OfType<JpegDirectory>().FirstOrDefault();
            var width = png?.GetInt32(PngDirectory.TagImageWidth) ?? jpeg?.GetImageWidth() ?? 0;
            var height = png?.GetInt32(PngDirectory.TagImageHeight) ?? jpeg?.GetImageHeight() ?? 0;
            var pixels = (long)width * height;
            return width < _minSide || height < _minSide || pixels < _minPixels
                ? new("Image dimensions are below TypeSafe's minimum.")
                : width > _maxSide || height > _maxSide || pixels > _maxPixels
                    ? new("Image dimensions exceed TypeSafe's maximum.", 413) : new();
        }
        catch (Exception exception) when (exception is ImageProcessingException or MetadataException or IOException or InvalidOperationException)
        { return new("Image bytes are malformed or unsupported."); }
    }

    private static bool IsStillPng(ReadOnlySpan<byte> bytes)
    {
        const int signatureBytes = 8;
        const int chunkOverhead = 12;
        const int chunkTypeOffset = 4;
        if (bytes.Length < signatureBytes || bytes[0] != 0x89 || !bytes[1..signatureBytes].SequenceEqual("PNG\r\n\x1a\n"u8)) { return false; }
        var offset = signatureBytes;
        while (bytes.Length - offset >= chunkOverhead)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes[offset..]);
            if (length > bytes.Length - offset - chunkOverhead) { return false; }
            var type = bytes.Slice(offset + chunkTypeOffset, chunkTypeOffset);
            if (type.SequenceEqual("acTL"u8)) { return false; }
            offset += checked((int)length + chunkOverhead);
            if (type.SequenceEqual("IEND"u8)) { return length == 0 && offset == bytes.Length; }
        }
        return false;
    }
}
