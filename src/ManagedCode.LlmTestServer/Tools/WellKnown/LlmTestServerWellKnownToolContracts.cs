namespace ManagedCode.LlmTestServer.Tools.WellKnown;

public sealed record LlmTestServerCalculatorArguments
{
    public required string Expression { get; init; }
}

public sealed record LlmTestServerCalculatorResult
{
    public decimal Value { get; init; }
}

public sealed record LlmTestServerCurrentTimeArguments
{
    public string TimeZone { get; init; } = "UTC";
}

public sealed record LlmTestServerCurrentTimeResult
{
    public required DateTimeOffset CurrentTime { get; init; }
    public string TimeZone { get; init; } = "UTC";
}

public sealed record LlmTestServerWeatherArguments
{
    public required string Location { get; init; }
    public string Unit { get; init; } = "celsius";
}

public sealed record LlmTestServerWeatherResult
{
    public required string Location { get; init; }
    public decimal Temperature { get; init; }
    public string Unit { get; init; } = "celsius";
    public required string Condition { get; init; }
}

public sealed record LlmTestServerWebSearchArguments
{
    public required string Query { get; init; }
    public int Limit { get; init; } = 5;
}

public sealed record LlmTestServerWebSearchHit
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public required string Snippet { get; init; }
}

public sealed record LlmTestServerWebSearchResult
{
    public List<LlmTestServerWebSearchHit> Results { get; init; } = [];
}

public sealed record LlmTestServerFetchUrlArguments
{
    public required string Url { get; init; }
}

public sealed record LlmTestServerFetchUrlResult
{
    public required string Url { get; init; }
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "text/plain";
    public string Content { get; init; } = string.Empty;
}

public sealed record LlmTestServerFileSearchArguments
{
    public required string Query { get; init; }
    public int Limit { get; init; } = 5;
}

public sealed record LlmTestServerFileSearchHit
{
    public required string FileId { get; init; }
    public required string Path { get; init; }
    public required string Snippet { get; init; }
}

public sealed record LlmTestServerFileSearchResult
{
    public List<LlmTestServerFileSearchHit> Results { get; init; } = [];
}

public sealed record LlmTestServerReadFileArguments
{
    public required string Path { get; init; }
    public int Offset { get; init; }
    public int? Length { get; init; }
}

public sealed record LlmTestServerReadFileResult
{
    public required string Path { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool Truncated { get; init; }
}

public sealed record LlmTestServerWriteFileArguments
{
    public required string Path { get; init; }
    public string Content { get; init; } = string.Empty;
    public bool Append { get; init; }
}

public sealed record LlmTestServerWriteFileResult
{
    public required string Path { get; init; }
    public int BytesWritten { get; init; }
}

public sealed record LlmTestServerListFilesArguments
{
    public required string Path { get; init; }
    public bool Recursive { get; init; }
}

public sealed record LlmTestServerFileEntry
{
    public required string Path { get; init; }
    public bool IsDirectory { get; init; }
    public long? Bytes { get; init; }
}

public sealed record LlmTestServerListFilesResult
{
    public List<LlmTestServerFileEntry> Entries { get; init; } = [];
}

public sealed record LlmTestServerRunCodeArguments
{
    public required string Language { get; init; }
    public required string Code { get; init; }
}

public sealed record LlmTestServerRunCodeResult
{
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}

public sealed record LlmTestServerShellArguments
{
    public required string Command { get; init; }
    public string? WorkingDirectory { get; init; }
}

public sealed record LlmTestServerShellResult
{
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public int ExitCode { get; init; }
}

public sealed record LlmTestServerApplyPatchArguments
{
    public required string Patch { get; init; }
}

public sealed record LlmTestServerApplyPatchResult
{
    public List<string> UpdatedPaths { get; init; } = [];
}

public sealed record LlmTestServerToolFixtureError
{
    public required string Code { get; init; }
    public required string Message { get; init; }
    public bool Retryable { get; init; }
}

/// <summary>An explicitly selected tool error payload; successful results retain their own typed root shape.</summary>
public sealed record LlmTestServerToolFixtureErrorResult
{
    public required LlmTestServerToolFixtureError Error { get; init; }
}
