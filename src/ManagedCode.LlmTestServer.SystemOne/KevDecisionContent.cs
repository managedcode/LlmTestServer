using System.Globalization;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.SystemOne;

public static class KevDecisionContent
{
    private const int _minimumFixedExponent = -4;
    private const int _maximumFixedExponent = 16;

    public static string Render(JsonElement value, int indent = 0)
    {
        var pad = new string(' ', indent * 2);
        return value.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => value.GetString()!,
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            JsonValueKind.Number => RenderNumber(value),
            JsonValueKind.Array => string.Join('\n', value.EnumerateArray().Select(item => $"{pad}- {Render(item, indent + 1).TrimStart()}")),
            JsonValueKind.Object => string.Join('\n', value.EnumerateObject().Select(item => item.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Object
                ? $"{pad}{item.Name}:\n{Render(item.Value, indent + 1)}" : $"{pad}{item.Name}: {Render(item.Value)}")),
            _ => throw new ArgumentException("Kev content must contain a JSON value.", nameof(value)),
        };
    }

    private static string RenderNumber(JsonElement value)
    {
        var raw = value.GetRawText();
        if (raw.IndexOfAny(['.', 'e', 'E']) < 0) { return raw == "-0" ? "0" : raw; }
        var number = value.GetDouble();
        if (double.IsInfinity(number)) { return number > 0 ? "inf" : "-inf"; }
        var negative = double.IsNegative(number);
        if (number == 0) { return negative ? "-0.0" : "0.0"; }
        var text = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
        var parts = text.Split('E'); var mantissa = parts[0];
        var exponent = parts.Length == 1 ? 0 : int.Parse(parts[1], CultureInfo.InvariantCulture);
        var point = mantissa.IndexOf('.'); if (point < 0) { point = mantissa.Length; }
        var digits = mantissa.Replace(".", "", StringComparison.Ordinal);
        var leading = digits.Length - digits.TrimStart('0').Length;
        digits = digits[leading..].TrimEnd('0');
        exponent += point - leading - 1;
        var sign = negative ? "-" : "";
        if (exponent is < _minimumFixedExponent or >= _maximumFixedExponent)
        {
            var tail = digits.Length > 1 ? "." + digits[1..] : "";
            return sign + digits[0] + tail + "e" + (exponent >= 0 ? "+" : "-") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        }
        point = exponent + 1;
        if (point <= 0) { return sign + "0." + new string('0', -point) + digits; }
        if (point >= digits.Length) { return sign + digits + new string('0', point - digits.Length) + ".0"; }
        return sign + digits[..point] + "." + digits[point..];
    }
}
