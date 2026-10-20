namespace ManagedCode.LlmTestServer.Runtime;

/// <summary>Explicit transcription fixture usage: duration or complete audio/text/output token counts.</summary>
public sealed record LlmTestServerTranscriptionUsage
{
    public double? DurationSeconds { get; init; } = 1;

    public long? InputAudioTokens { get; init; }

    public long? InputTextTokens { get; init; }

    public long? OutputTokens { get; init; }

    public static LlmTestServerTranscriptionUsage ForDuration(double seconds)
    {
        var usage = new LlmTestServerTranscriptionUsage { DurationSeconds = seconds };
        usage.Validate();
        return usage;
    }

    public static LlmTestServerTranscriptionUsage ForTokens(long audio, long text, long output)
    {
        var usage = new LlmTestServerTranscriptionUsage
        {
            DurationSeconds = null,
            InputAudioTokens = audio,
            InputTextTokens = text,
            OutputTokens = output,
        };
        usage.Validate();
        return usage;
    }

    internal void Validate()
    {
        if (DurationSeconds is { } seconds)
        {
            if (!double.IsFinite(seconds) || seconds <= 0 || InputAudioTokens is not null
                || InputTextTokens is not null || OutputTokens is not null)
            {
                throw new ArgumentException("Duration usage requires only a finite positive duration.");
            }
            return;
        }

        if (InputAudioTokens is not >= 0 || InputTextTokens is not >= 0 || OutputTokens is not >= 0
            || (decimal)InputAudioTokens.Value + InputTextTokens.Value + OutputTokens.Value is <= 0 or > long.MaxValue)
        {
            throw new ArgumentException("Token usage requires complete nonnegative audio, text and output counts with a positive bounded total.");
        }
    }
}
