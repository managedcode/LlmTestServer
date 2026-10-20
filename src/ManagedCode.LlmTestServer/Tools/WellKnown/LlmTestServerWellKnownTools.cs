namespace ManagedCode.LlmTestServer.Tools.WellKnown;

/// <summary>Provider-neutral function-tool presets. Every result is explicitly scripted by the caller.</summary>
public static class LlmTestServerWellKnownTools
{
    public const string ErrorSchemaJson = """{"type":"object","properties":{"error":{"type":"object","properties":{"code":{"type":"string","minLength":1},"message":{"type":"string","minLength":1},"retryable":{"type":"boolean"}},"required":["code","message","retryable"],"additionalProperties":false}},"required":["error"],"additionalProperties":false}""";

    public static LlmTestServerWellKnownTool<LlmTestServerCalculatorArguments, LlmTestServerCalculatorResult> Calculator { get; } = Create<LlmTestServerCalculatorArguments, LlmTestServerCalculatorResult>(
        LlmTestServerWellKnownToolNames.Calculator,
        "Scripted expression and numeric result; no expression is evaluated.",
        """{"type":"object","properties":{"expression":{"type":"string","minLength":1}},"required":["expression"],"additionalProperties":false}""",
        """{"type":"object","properties":{"value":{"type":"number"}},"required":["value"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerCurrentTimeArguments, LlmTestServerCurrentTimeResult> CurrentTime { get; } = Create<LlmTestServerCurrentTimeArguments, LlmTestServerCurrentTimeResult>(
        LlmTestServerWellKnownToolNames.CurrentTime,
        "Scripted timestamp supplied by the fixture; no system clock is consulted.",
        """{"type":"object","properties":{"timeZone":{"type":"string","minLength":1}},"required":["timeZone"],"additionalProperties":false}""",
        """{"type":"object","properties":{"currentTime":{"type":"string","format":"date-time"},"timeZone":{"type":"string","minLength":1}},"required":["currentTime","timeZone"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerWeatherArguments, LlmTestServerWeatherResult> Weather { get; } = Create<LlmTestServerWeatherArguments, LlmTestServerWeatherResult>(
        LlmTestServerWellKnownToolNames.Weather,
        "Scripted weather data for a location; no weather service is called.",
        """{"type":"object","properties":{"location":{"type":"string","minLength":1},"unit":{"type":"string","enum":["celsius","fahrenheit"]}},"required":["location","unit"],"additionalProperties":false}""",
        """{"type":"object","properties":{"location":{"type":"string","minLength":1},"temperature":{"type":"number"},"unit":{"type":"string","enum":["celsius","fahrenheit"]},"condition":{"type":"string","minLength":1}},"required":["location","temperature","unit","condition"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerWebSearchArguments, LlmTestServerWebSearchResult> WebSearch { get; } = Create<LlmTestServerWebSearchArguments, LlmTestServerWebSearchResult>(
        LlmTestServerWellKnownToolNames.WebSearch,
        "Scripted search hits from test data; no network search is performed.",
        """{"type":"object","properties":{"query":{"type":"string","minLength":1},"limit":{"type":"integer","minimum":1,"maximum":100}},"required":["query","limit"],"additionalProperties":false}""",
        """{"type":"object","properties":{"results":{"type":"array","items":{"type":"object","properties":{"title":{"type":"string","minLength":1},"url":{"type":"string","minLength":1},"snippet":{"type":"string","minLength":0}},"required":["title","url","snippet"],"additionalProperties":false}}},"required":["results"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerFetchUrlArguments, LlmTestServerFetchUrlResult> FetchUrl { get; } = Create<LlmTestServerFetchUrlArguments, LlmTestServerFetchUrlResult>(
        LlmTestServerWellKnownToolNames.FetchUrl,
        "Scripted URL content; no URL is fetched.",
        """{"type":"object","properties":{"url":{"type":"string","minLength":1}},"required":["url"],"additionalProperties":false}""",
        """{"type":"object","properties":{"url":{"type":"string","minLength":1},"statusCode":{"type":"integer","minimum":100,"maximum":599},"contentType":{"type":"string","minLength":1},"content":{"type":"string","minLength":0}},"required":["url","statusCode","contentType","content"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerFileSearchArguments, LlmTestServerFileSearchResult> FileSearch { get; } = Create<LlmTestServerFileSearchArguments, LlmTestServerFileSearchResult>(
        LlmTestServerWellKnownToolNames.FileSearch,
        "Scripted file search hits from test data; no file system is searched.",
        """{"type":"object","properties":{"query":{"type":"string","minLength":1},"limit":{"type":"integer","minimum":1,"maximum":100}},"required":["query","limit"],"additionalProperties":false}""",
        """{"type":"object","properties":{"results":{"type":"array","items":{"type":"object","properties":{"fileId":{"type":"string","minLength":1},"path":{"type":"string","minLength":1},"snippet":{"type":"string","minLength":0}},"required":["fileId","path","snippet"],"additionalProperties":false}}},"required":["results"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerReadFileArguments, LlmTestServerReadFileResult> ReadFile { get; } = Create<LlmTestServerReadFileArguments, LlmTestServerReadFileResult>(
        LlmTestServerWellKnownToolNames.ReadFile,
        "Scripted file content; no file is opened.",
        """{"type":"object","properties":{"path":{"type":"string","minLength":1},"offset":{"type":"integer","minimum":0},"length":{"type":"integer","minimum":0}},"required":["path","offset"],"additionalProperties":false}""",
        """{"type":"object","properties":{"path":{"type":"string","minLength":1},"content":{"type":"string","minLength":0},"truncated":{"type":"boolean"}},"required":["path","content","truncated"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerWriteFileArguments, LlmTestServerWriteFileResult> WriteFile { get; } = Create<LlmTestServerWriteFileArguments, LlmTestServerWriteFileResult>(
        LlmTestServerWellKnownToolNames.WriteFile,
        "Scripted write receipt; no file is created or changed.",
        """{"type":"object","properties":{"path":{"type":"string","minLength":1},"content":{"type":"string","minLength":0},"append":{"type":"boolean"}},"required":["path","content","append"],"additionalProperties":false}""",
        """{"type":"object","properties":{"path":{"type":"string","minLength":1},"bytesWritten":{"type":"integer","minimum":0}},"required":["path","bytesWritten"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerListFilesArguments, LlmTestServerListFilesResult> ListFiles { get; } = Create<LlmTestServerListFilesArguments, LlmTestServerListFilesResult>(
        LlmTestServerWellKnownToolNames.ListFiles,
        "Scripted directory entries; no directory is enumerated.",
        """{"type":"object","properties":{"path":{"type":"string","minLength":1},"recursive":{"type":"boolean"}},"required":["path","recursive"],"additionalProperties":false}""",
        """{"type":"object","properties":{"entries":{"type":"array","items":{"type":"object","properties":{"path":{"type":"string","minLength":1},"isDirectory":{"type":"boolean"},"bytes":{"type":"integer","minimum":0}},"required":["path","isDirectory"],"additionalProperties":false}}},"required":["entries"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerRunCodeArguments, LlmTestServerRunCodeResult> RunCode { get; } = Create<LlmTestServerRunCodeArguments, LlmTestServerRunCodeResult>(
        LlmTestServerWellKnownToolNames.RunCode,
        "Scripted process output; code is never evaluated or executed.",
        """{"type":"object","properties":{"language":{"type":"string","minLength":1},"code":{"type":"string","minLength":1}},"required":["language","code"],"additionalProperties":false}""",
        """{"type":"object","properties":{"stdout":{"type":"string","minLength":0},"stderr":{"type":"string","minLength":0},"exitCode":{"type":"integer"}},"required":["stdout","stderr","exitCode"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerShellArguments, LlmTestServerShellResult> Shell { get; } = Create<LlmTestServerShellArguments, LlmTestServerShellResult>(
        LlmTestServerWellKnownToolNames.Shell,
        "Scripted command output; no shell or process is started.",
        """{"type":"object","properties":{"command":{"type":"string","minLength":1},"workingDirectory":{"type":"string","minLength":1}},"required":["command"],"additionalProperties":false}""",
        """{"type":"object","properties":{"stdout":{"type":"string","minLength":0},"stderr":{"type":"string","minLength":0},"exitCode":{"type":"integer"}},"required":["stdout","stderr","exitCode"],"additionalProperties":false}""");

    public static LlmTestServerWellKnownTool<LlmTestServerApplyPatchArguments, LlmTestServerApplyPatchResult> ApplyPatch { get; } = Create<LlmTestServerApplyPatchArguments, LlmTestServerApplyPatchResult>(
        LlmTestServerWellKnownToolNames.ApplyPatch,
        "Scripted patch receipt; no patch is applied.",
        """{"type":"object","properties":{"patch":{"type":"string","minLength":1}},"required":["patch"],"additionalProperties":false}""",
        """{"type":"object","properties":{"updatedPaths":{"type":"array","items":{"type":"string","minLength":1}}},"required":["updatedPaths"],"additionalProperties":false}""");

    public static IReadOnlyList<LlmTestServerWellKnownToolDescriptor> All { get; } = Array.AsReadOnly(new[]
    {
        Calculator.Descriptor,
        CurrentTime.Descriptor,
        Weather.Descriptor,
        WebSearch.Descriptor,
        FetchUrl.Descriptor,
        FileSearch.Descriptor,
        ReadFile.Descriptor,
        WriteFile.Descriptor,
        ListFiles.Descriptor,
        RunCode.Descriptor,
        Shell.Descriptor,
        ApplyPatch.Descriptor,
    });

    private static LlmTestServerWellKnownTool<TArguments, TResult> Create<TArguments, TResult>(string name, string description, string parametersJson, string resultSchemaJson)
        where TArguments : class
        where TResult : class
    {
        return new(new() { Name = name, Description = description, ParametersJson = parametersJson, ResultSchemaJson = resultSchemaJson, ErrorSchemaJson = ErrorSchemaJson });
    }
}
