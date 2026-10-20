using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.Scenarios;

internal static partial class LlmTestServerResponseItems
{
    private static void ValidateFilter(JsonElement filter)
    {
        var nodes = 0;
        ValidateFilterNode(filter, 1, ref nodes);
    }

    private static void ValidateFilterNode(JsonElement node, int depth, ref int nodes)
    {
        if (++nodes > 128 || depth > 8 || node.ValueKind != JsonValueKind.Object) { throw new ArgumentException("Filter depth or node count exceeds its bounded contract."); }
        if (!node.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String) { throw new ArgumentException("A filter type is required."); }
        var operation = type.GetString();
        var compound = operation is "and" or "or";
        var allowed = compound ? new[] { "type", "filters" } : ["type", "key", "value"];
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in node.EnumerateObject())
        {
            if (!allowed.Contains(property.Name, StringComparer.Ordinal) || !fields.Add(property.Name)) { throw new ArgumentException("Unknown or duplicate filter field."); }
        }
        if (fields.Count != allowed.Length) { throw new ArgumentException("Required filter fields are missing."); }
        if (compound)
        {
            var children = node.GetProperty("filters");
            if (children.ValueKind != JsonValueKind.Array || children.GetArrayLength() is < 1 or > 128) { throw new ArgumentException("A nonempty bounded filter group is required."); }
            foreach (var child in children.EnumerateArray()) { ValidateFilterNode(child, depth + 1, ref nodes); }
            return;
        }
        var key = node.GetProperty("key");
        if (key.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(key.GetString()) || key.GetString()!.Length > 64)
        { throw new ArgumentException("A bounded filter attribute key is required."); }
        var value = node.GetProperty("value");
        if (operation is "in" or "nin")
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 128) { throw new ArgumentException("Membership filters require a nonempty bounded array."); }
            var kind = value[0].ValueKind;
            if (kind is not (JsonValueKind.String or JsonValueKind.Number)) { throw new ArgumentException("Membership filters require strings or numbers."); }
            foreach (var entry in value.EnumerateArray())
            {
                Scalar(entry);
                if (entry.ValueKind != kind) { throw new ArgumentException("Membership filter values must have a common kind."); }
            }
            return;
        }
        if (operation is not ("eq" or "ne" or "gt" or "gte" or "lt" or "lte")) { throw new ArgumentException("Unsupported filter operation."); }
        Scalar(value);
        if (operation is not ("eq" or "ne") && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
        { throw new ArgumentException("Ordering filters require numbers or strings."); }
    }

    internal static bool MatchesFilter(JsonElement? filter, IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        if (filter is null || filter.Value.ValueKind == JsonValueKind.Null) { return true; }
        return MatchFilterNode(filter.Value, attributes);
    }

    private static bool MatchFilterNode(JsonElement filter, IReadOnlyDictionary<string, JsonElement>? attributes)
    {
        var operation = filter.GetProperty("type").GetString();
        if (operation is "and" or "or")
        {
            var children = filter.GetProperty("filters").EnumerateArray();
            return operation == "and" ? children.All(child => MatchFilterNode(child, attributes)) : children.Any(child => MatchFilterNode(child, attributes));
        }
        if (attributes is null || !attributes.TryGetValue(filter.GetProperty("key").GetString()!, out var actual)) { return false; }
        var expected = filter.GetProperty("value");
        if (operation is "in" or "nin")
        {
            if (actual.ValueKind != expected[0].ValueKind) { return false; }
            var contains = expected.EnumerateArray().Any(entry => Compare(actual, entry) == 0);
            return operation == "in" ? contains : !contains;
        }
        var comparison = Compare(actual, expected);
        if (comparison is null) { return false; }
        return operation switch
        {
            "eq" => comparison == 0,
            "ne" => comparison != 0,
            "gt" => comparison > 0,
            "gte" => comparison >= 0,
            "lt" => comparison < 0,
            "lte" => comparison <= 0,
            _ => false,
        };
    }

    private static int? Compare(JsonElement actual, JsonElement expected)
    {
        if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
        {
            return CompareNumbers(actual, expected);
        }
        if (actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
        { return string.Compare(actual.GetString(), expected.GetString(), StringComparison.Ordinal); }
        if (actual.ValueKind is JsonValueKind.True or JsonValueKind.False && expected.ValueKind is JsonValueKind.True or JsonValueKind.False)
        { return actual.GetBoolean().CompareTo(expected.GetBoolean()); }
        return null;
    }

    private static int CompareNumbers(JsonElement actual, JsonElement expected)
    {
        var left = NormalizeNumber(actual);
        var right = NormalizeNumber(expected);
        var sign = left.Sign.CompareTo(right.Sign);
        if (sign != 0) { return sign; }
        if (left.Sign == 0) { return 0; }
        var magnitude = left.Order.CompareTo(right.Order);
        if (magnitude == 0)
        {
            var length = Math.Max(left.Digits.Length, right.Digits.Length);
            for (var index = 0; index < length; index++)
            {
                var leftDigit = index < left.Digits.Length ? left.Digits[index] : '0';
                var rightDigit = index < right.Digits.Length ? right.Digits[index] : '0';
                magnitude = leftDigit.CompareTo(rightDigit);
                if (magnitude != 0) { break; }
            }
        }
        return left.Sign * magnitude;
    }

    private static (int Sign, string Digits, BigInteger Order) NormalizeNumber(JsonElement value)
    {
        var text = value.GetRawText();
        var start = text[0] == '-' ? 1 : 0;
        var exponentIndex = text.IndexOf('e');
        if (exponentIndex < 0) { exponentIndex = text.IndexOf('E'); }
        var end = exponentIndex < 0 ? text.Length : exponentIndex;
        var point = text.IndexOf('.', start, end - start);
        var fractionDigits = point < 0 ? 0 : end - point - 1;
        var digits = text[start..end].Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0');
        if (digits.Length == 0) { return (0, string.Empty, BigInteger.Zero); }
        // Compare decimal order without expanding powers, including very small finite JSON numbers.
        var exponent = exponentIndex < 0 ? BigInteger.Zero
            : BigInteger.Parse(text.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return (start == 0 ? 1 : -1, digits, exponent + digits.Length - fractionDigits);
    }

}
