using System.Globalization;
using System.Text.Json;

namespace ManagedCode.LlmTestServer.OpenAI;

/// <summary>Creates typed native OpenAI choice identities for neutral decision fixtures.</summary>
public static class OpenAiDecisionFixtureIds
{
    /// <summary>Returns the fixture question ID for its zero-based native array position.</summary>
    public static string ForQuestionIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Returns the canonical JSON string literal, including quotes.</summary>
    public static string ForChoiceValue(string value)
    {
        return JsonSerializer.Serialize(value);
    }

    /// <summary>Returns the canonical JSON boolean literal.</summary>
    public static string ForChoiceValue(bool value)
    {
        return JsonSerializer.Serialize(value);
    }
}
