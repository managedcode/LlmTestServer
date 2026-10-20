using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace ManagedCode.LlmTestServer.Hosting;

internal static class LlmTestServerProviderHttpTracePayloads
{
    private const string _redactedValue = "[redacted]";

    public static LlmTestServerProviderHttpPayloadPreview CreateRequestPreview(
        ReadOnlyMemory<byte> body,
        string? contentType,
        long? byteCount,
        bool truncated
    )
    {
        contentType = NormalizeContentType(contentType);
        if (byteCount == 0)
        {
            return new(null, false, 0, null);
        }

        if (IsMultipart(contentType))
        {
            return new(
                CreateOmittedBodySummary("multipart", contentType, byteCount),
                false,
                byteCount,
                null
            );
        }

        if (!IsTextual(contentType))
        {
            return new(
                CreateOmittedBodySummary("binary", contentType, byteCount),
                false,
                byteCount,
                null
            );
        }

        if (body.IsEmpty)
        {
            return new(
                "[request body was not consumed by the endpoint]",
                truncated,
                byteCount,
                null
            );
        }

        var preview = CreateTextPreview(body, contentType, truncated, out var modelId);
        return new(preview, truncated, byteCount, modelId);
    }

    public static string? CreateResponsePreview(
        ReadOnlyMemory<byte> body,
        string? contentType,
        long byteCount,
        bool truncated
    )
    {
        contentType = NormalizeContentType(contentType);
        if (byteCount == 0)
        {
            return null;
        }

        return IsTextual(contentType)
            ? CreateTextPreview(body, contentType, truncated, out _)
            : CreateOmittedBodySummary("binary", contentType, byteCount);
    }

    public static LlmTestServerProviderHttpPayloadPreview CreateMultipartRequestPreview(
        IFormCollection form,
        string? contentType,
        long? byteCount,
        int maxPreviewBytes
    )
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPreviewBytes);

        var modelId = form.TryGetValue("model", out var models) && models.Count > 0
            ? models[0]
            : null;
        var builder = new BoundedUtf8TextBuilder(maxPreviewBytes);
        builder.Append("[multipart body summary; ");
        AppendNormalizedContentType(builder, contentType);
        builder.Append("; ");
        if (byteCount is null)
        {
            builder.Append("unknown size");
        }
        else
        {
            builder.Append(byteCount.Value).Append(" bytes");
        }

        builder.Append(']');

        foreach (var field in form)
        {
            builder.Append("\nfield ").Append(field.Key).Append(": ");
            if (builder.IsTruncated)
            {
                break;
            }

            if (IsSensitiveName(field.Key))
            {
                builder.Append(_redactedValue);
            }
            else
            {
                var valueIndex = 0;
                foreach (var value in field.Value)
                {
                    if (valueIndex > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(value ?? string.Empty);
                    valueIndex++;
                    if (builder.IsTruncated)
                    {
                        break;
                    }
                }
            }

            if (builder.IsTruncated)
            {
                break;
            }
        }

        if (!builder.IsTruncated)
        {
            foreach (var file in form.Files)
            {
                builder
                    .Append("\nfile ")
                    .Append(file.Name)
                    .Append(": name=")
                    .Append(file.FileName)
                    .Append(", content-type=")
                    .Append(file.ContentType)
                    .Append(", bytes=")
                    .Append(file.Length);
                if (builder.IsTruncated)
                {
                    break;
                }
            }
        }

        return new(builder.ToString(), builder.IsTruncated, byteCount, modelId);
    }

    public static bool IsStreamingResponse(
        string? contentType,
        string? route,
        int successfulFlushCount
    )
    {
        contentType = NormalizeContentType(contentType);
        return string.Equals(contentType, "text/event-stream", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                contentType,
                "application/vnd.amazon.eventstream",
                StringComparison.OrdinalIgnoreCase
            )
            || successfulFlushCount > 1
            || route?.Contains("stream", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool ShouldCaptureBody(string? contentType)
    {
        contentType = NormalizeContentType(contentType);
        return !IsMultipart(contentType) && IsTextual(contentType);
    }

    public static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        var separator = contentType.IndexOf(';', StringComparison.Ordinal);
        return (separator < 0 ? contentType : contentType[..separator]).Trim();
    }

    private static string CreateTextPreview(
        ReadOnlyMemory<byte> body,
        string? contentType,
        bool truncated,
        out string? modelId
    )
    {
        modelId = null;
        if (body.IsEmpty)
        {
            return string.Empty;
        }

        var text = Encoding.UTF8.GetString(body.Span);
        if (IsJson(contentType) || LooksLikeJson(text))
        {
            if (truncated)
            {
                return "[JSON preview omitted because the bounded payload is incomplete]";
            }

            return SanitizeJsonAndReadModel(text, out modelId);
        }

        if (string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
        {
            return SanitizeForm(text);
        }

        return text;
    }

    private static string SanitizeJsonAndReadModel(string json, out string? modelId)
    {
        modelId = null;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var model)
                && model.ValueKind == JsonValueKind.String)
            {
                modelId = model.GetString();
            }

            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                WriteSanitizedJson(document.RootElement, writer);
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
        }
        catch (JsonException)
        {
            return "[malformed JSON preview omitted because it could not be safely sanitized]";
        }
    }

    private static void WriteSanitizedJson(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    if (IsSensitiveName(property.Name))
                    {
                        writer.WriteStringValue(_redactedValue);
                    }
                    else
                    {
                        WriteSanitizedJson(property.Value, writer);
                    }
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteSanitizedJson(item, writer);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static string SanitizeForm(string form)
    {
        var parsed = QueryHelpers.ParseQuery(form);
        var builder = new StringBuilder();

        foreach (var item in parsed)
        {
            foreach (var value in item.Value)
            {
                if (builder.Length > 0)
                {
                    builder.Append('&');
                }

                builder
                    .Append(Uri.EscapeDataString(item.Key))
                    .Append('=')
                    .Append(
                        Uri.EscapeDataString(IsSensitiveName(item.Key) ? _redactedValue : value ?? string.Empty)
                    );
            }
        }

        return builder.ToString();
    }

    private static bool IsTextual(string? contentType)
    {
        return contentType is null
            || contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || IsJson(contentType)
            || contentType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase)
            || string.Equals(contentType, "application/x-ndjson", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJson(string? contentType)
    {
        return string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase)
            || contentType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsMultipart(string? contentType)
    {
        return contentType?.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSensitiveName(string name)
    {
        if (EqualsNormalized(name, "authorization") || EqualsNormalized(name, "bearer"))
        {
            return true;
        }

        // Singular security suffixes catch refresh_token, session_token, private_key,
        // github_token, client_secret, and similar provider-specific names. Ordinary usage
        // fields such as max_tokens end in the plural "tokens" and are intentionally retained.
        return EndsWithNormalized(name, "token")
            || EndsWithNormalized(name, "key")
            || EndsWithNormalized(name, "secret")
            || EndsWithNormalized(name, "password")
            || EndsWithNormalized(name, "credential")
            || EndsWithNormalized(name, "credentials");
    }

    private static bool EqualsNormalized(ReadOnlySpan<char> value, ReadOnlySpan<char> expected)
    {
        var expectedIndex = 0;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }

            if (
                expectedIndex >= expected.Length
                || char.ToLowerInvariant(character) != expected[expectedIndex]
            )
            {
                return false;
            }

            expectedIndex++;
        }

        return expectedIndex == expected.Length;
    }

    private static bool EndsWithNormalized(ReadOnlySpan<char> value, ReadOnlySpan<char> suffix)
    {
        var suffixIndex = suffix.Length - 1;
        for (var index = value.Length - 1; index >= 0; index--)
        {
            var character = value[index];
            if (!char.IsLetterOrDigit(character))
            {
                continue;
            }

            if (suffixIndex < 0)
            {
                return true;
            }

            if (char.ToLowerInvariant(character) != suffix[suffixIndex])
            {
                return false;
            }

            suffixIndex--;
        }

        return suffixIndex < 0;
    }

    private static bool LooksLikeJson(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        return !trimmed.IsEmpty && trimmed[0] is '{' or '[';
    }

    private static string CreateOmittedBodySummary(
        string kind,
        string? contentType,
        long? byteCount
    )
    {
        var mediaType = contentType ?? "unknown content type";
        var size = byteCount is null ? "unknown size" : $"{byteCount.Value} bytes";
        return $"[{kind} body omitted; {mediaType}; {size}]";
    }

    private static void AppendNormalizedContentType(
        BoundedUtf8TextBuilder builder,
        string? contentType
    )
    {
        var mediaType = contentType.AsSpan();
        var separator = mediaType.IndexOf(';');
        if (separator >= 0)
        {
            mediaType = mediaType[..separator];
        }

        mediaType = mediaType.Trim();
        if (mediaType.IsEmpty)
        {
            builder.Append("unknown content type");
        }
        else
        {
            builder.Append(mediaType);
        }
    }

    private sealed class BoundedUtf8TextBuilder(int maxBytes)
    {
        private const int _ellipsisByteCount = 3;
        private readonly StringBuilder _builder = new(Math.Min(maxBytes, 4096));
        private int _remainingBytes = maxBytes;

        public bool IsTruncated { get; private set; }

        public BoundedUtf8TextBuilder Append(string? value)
        {
            return Append(value.AsSpan());
        }

        public BoundedUtf8TextBuilder Append(char value)
        {
            Span<char> buffer = stackalloc char[1];
            buffer[0] = value;
            return Append(buffer);
        }

        public BoundedUtf8TextBuilder Append(long value)
        {
            Span<char> buffer = stackalloc char[32];
            if (
                !value.TryFormat(
                    buffer,
                    out var written,
                    default,
                    CultureInfo.InvariantCulture
                )
            )
            {
                throw new InvalidOperationException("The multipart byte count could not be formatted.");
            }

            return Append(buffer[..written]);
        }

        public override string ToString()
        {
            return _builder.ToString();
        }

        public BoundedUtf8TextBuilder Append(ReadOnlySpan<char> value)
        {
            if (IsTruncated || value.IsEmpty)
            {
                return this;
            }

            var byteCount = Encoding.UTF8.GetByteCount(value);
            if (byteCount <= _remainingBytes)
            {
                _builder.Append(value);
                _remainingBytes -= byteCount;
                return this;
            }

            IsTruncated = true;
            if (_remainingBytes == 0)
            {
                return this;
            }

            var prefixBudget = _remainingBytes >= _ellipsisByteCount
                ? _remainingBytes - _ellipsisByteCount
                : _remainingBytes;
            var prefixLength = FindUtf8PrefixLength(value, prefixBudget, out var prefixBytes);
            if (prefixLength > 0)
            {
                var prefix = value[..prefixLength];
                _builder.Append(prefix);
                _remainingBytes -= prefixBytes;
            }

            if (_remainingBytes >= _ellipsisByteCount)
            {
                _builder.Append('…');
                _remainingBytes -= _ellipsisByteCount;
            }

            return this;
        }

        private static int FindUtf8PrefixLength(
            ReadOnlySpan<char> value,
            int maxBytes,
            out int byteCount
        )
        {
            var index = 0;
            byteCount = 0;
            while (index < value.Length)
            {
                var character = value[index];
                var characterLength = 1;
                var characterBytes = character switch
                {
                    <= '\u007f' => 1,
                    <= '\u07ff' => 2,
                    _ when char.IsHighSurrogate(character)
                        && index + 1 < value.Length
                        && char.IsLowSurrogate(value[index + 1]) => 4,
                    _ => 3,
                };
                if (characterBytes == 4)
                {
                    characterLength = 2;
                }

                if (byteCount + characterBytes > maxBytes)
                {
                    break;
                }

                byteCount += characterBytes;
                index += characterLength;
            }

            return index;
        }
    }
}

internal readonly record struct LlmTestServerProviderHttpPayloadPreview(
    string? Preview,
    bool IsTruncated,
    long? ByteCount,
    string? ModelId
);
