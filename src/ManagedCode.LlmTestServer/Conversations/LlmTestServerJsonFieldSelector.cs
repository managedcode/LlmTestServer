using System.Globalization;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.Conversations;

internal static class LlmTestServerJsonFieldSelector
{
    internal static string[] Parse(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length > 256) { throw new ArgumentException("JSON field paths are limited to 256 characters.", nameof(path)); }
        if (path == "$" || path.Length == 0) { return []; }
        if (path[0] == '/')
        {
            var parts = path[1..].Split('/');
            foreach (var part in parts)
            {
                for (var index = 0; index < part.Length; index++)
                {
                    if (part[index] == '~' && (++index == part.Length || part[index] is not ('0' or '1')))
                    { throw new ArgumentException("Invalid JSON pointer escape.", nameof(path)); }
                }
            }
            return parts.Select(part => part.Replace("~1", "/", StringComparison.Ordinal).Replace("~0", "~", StringComparison.Ordinal)).ToArray();
        }
        if (path[0] != '$') { throw new ArgumentException("Use a JSON pointer or a JSON path starting with $.", nameof(path)); }
        var result = new List<string>();
        for (var index = 1; index < path.Length;)
        {
            if (path[index] == '.')
            {
                var start = ++index;
                while (index < path.Length && (char.IsAsciiLetterOrDigit(path[index]) || path[index] == '_')) { index++; }
                if (start == index) { throw new ArgumentException("Invalid JSON property selector.", nameof(path)); }
                result.Add(path[start..index]);
            }
            else if (path[index] == '[')
            {
                var start = ++index;
                while (index < path.Length && char.IsAsciiDigit(path[index])) { index++; }
                if (start == index || index >= path.Length || path[index++] != ']') { throw new ArgumentException("Invalid JSON array selector.", nameof(path)); }
                result.Add(path[start..(index - 1)]);
            }
            else { throw new ArgumentException("Unsupported JSON field selector.", nameof(path)); }
        }
        return [.. result];
    }

    internal static bool Matches(string json, string path, string expected)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            using var value = JsonDocument.Parse(expected);
            var current = document.RootElement;
            foreach (var part in Parse(path))
            {
                if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var property)) { current = property; }
                else if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < current.GetArrayLength()) { current = current[index]; }
                else { return false; }
            }
            return JsonElement.DeepEquals(current, value.RootElement);
        }
        catch (JsonException) { return false; }
    }
}
