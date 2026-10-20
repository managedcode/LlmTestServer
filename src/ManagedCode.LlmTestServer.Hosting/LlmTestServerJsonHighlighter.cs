using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace ManagedCode.LlmTestServer.Hosting;

// Renders payload previews as indented JSON with token classes; anything that is not JSON stays plain text.
internal static partial class LlmTestServerJsonHighlighter
{
    private static readonly JsonSerializerOptions _prettyJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    public static MarkupString Highlight(string? payload, bool truncated)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return new MarkupString("No body captured.");
        }

        var suffix = truncated ? "\n\n[preview truncated]" : string.Empty;
        string pretty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            pretty = JsonSerializer.Serialize(document.RootElement, _prettyJsonOptions);
        }
        catch (JsonException)
        {
            return new MarkupString(Encode(payload + suffix));
        }

        var builder = new StringBuilder(pretty.Length * 2);
        var last = 0;
        foreach (Match match in JsonToken().Matches(pretty))
        {
            builder.Append(Encode(pretty[last..match.Index]));
            if (match.Groups["string"].Success)
            {
                var key = match.Groups["colon"].Success;
                Wrap(builder, key ? "j-key" : "j-str", match.Groups["string"].Value);
                if (key)
                {
                    builder.Append(Encode(match.Groups["colon"].Value));
                }
            }
            else
            {
                Wrap(builder, match.Groups["literal"].Success ? "j-lit" : "j-num", match.Value);
            }

            last = match.Index + match.Length;
        }

        return new MarkupString(builder.Append(Encode(pretty[last..])).Append(Encode(suffix)).ToString());
    }

    private static void Wrap(StringBuilder builder, string css, string value)
    {
        builder.Append("<span class=\"").Append(css).Append("\">").Append(Encode(value)).Append("</span>");
    }

    // Only markup-significant characters are escaped so Unicode text stays readable in the page source.
    private static string Encode(string value)
    {
        return value.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    [GeneratedRegex("""(?<string>"(?:\\.|[^"\\])*")(?<colon>\s*:)?|(?<literal>\b(?:true|false|null)\b)|(?<number>-?\b\d+(?:\.\d+)?(?:[eE][+-]?\d+)?\b)""")]
    private static partial Regex JsonToken();
}
