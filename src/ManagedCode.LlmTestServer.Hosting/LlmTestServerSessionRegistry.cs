using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ManagedCode.LlmTestServer.Configuration;
using ManagedCode.LlmTestServer.Runtime;
using ManagedCode.LlmTestServer.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace ManagedCode.LlmTestServer.Hosting;

internal sealed class LlmTestServerSessionRegistry(
    LlmTestServerConfiguration template,
    LlmTestServerProviderHttpTraceStore defaultTraceStore,
    IOptions<LlmTestServerProviderHttpTraceOptions> traceOptions,
    IOptions<LlmTestServerSessionOptions> options)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, State> _sessions = new(StringComparer.Ordinal);
    private readonly LlmTestServerSessionOptions _options = options.Value;
    private long _reservedBytes;

    internal State Default { get; } = new(new LlmTestServerRuntime(template), defaultTraceStore, [], 0);

    internal State? Resolve(HttpContext? context)
    {
        if (context is null || !context.Request.Headers.ContainsKey(LlmTestServerSessionHeaders.Id)
            && !context.Request.Headers.ContainsKey(LlmTestServerSessionHeaders.AccessKey)) { return Default; }
        var ids = context.Request.Headers[LlmTestServerSessionHeaders.Id];
        var keys = context.Request.Headers[LlmTestServerSessionHeaders.AccessKey];
        if (ids.Count != 1 || keys.Count != 1 || ids[0] is not { Length: <= 128 } id || keys[0] is not { Length: <= 128 } key) { return null; }
        lock (_gate)
        {
            return _sessions.TryGetValue(id, out var state)
                && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)), state.KeyHash)
                ? state : null;
        }
    }

    internal LlmTestServerSession? Create(LlmTestServerConfiguration? configuration)
    {
        configuration ??= template;
        var runtime = new LlmTestServerRuntime(configuration);
        var reservation = Reservation(configuration);
        lock (_gate)
        {
            if (_sessions.Count >= _options.MaxSessions || reservation > _options.MaxReservedStateBytes - _reservedBytes) { return null; }
            var id = "session_" + Guid.NewGuid().ToString("N");
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _sessions.Add(id, new State(runtime, new LlmTestServerProviderHttpTraceStore(traceOptions), SHA256.HashData(Encoding.UTF8.GetBytes(key)), reservation));
            _reservedBytes += reservation;
            return new LlmTestServerSession { Id = id, AccessKey = key, ReservedStateBytes = reservation };
        }
    }

    internal Task? ConfigureAsync(State state, LlmTestServerConfiguration configuration)
    {
        var replacement = new LlmTestServerRuntime(configuration);
        var reservation = Reservation(configuration);
        lock (_gate)
        {
            if (!_sessions.ContainsValue(state)) { return null; }
            if (reservation > _options.MaxReservedStateBytes - _reservedBytes + state.ReservedBytes)
            { throw new InvalidOperationException("Session state capacity exceeded."); }
            var retired = state.Runtime.RetireAsync();
            state.Runtime = replacement;
            _reservedBytes += reservation - state.ReservedBytes;
            state.ReservedBytes = reservation;
            return retired;
        }
    }

    internal Task? DeleteAsync(string id, State state)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(id, out var found) || !ReferenceEquals(found, state)) { return null; }
            _sessions.Remove(id);
            _reservedBytes -= state.ReservedBytes;
            state.TraceStore.Reset();
            return state.Runtime.RetireAsync();
        }
    }

    private long Reservation(LlmTestServerConfiguration configuration)
    {
        var configurationBytes = JsonSerializer.SerializeToUtf8Bytes(configuration).LongLength;
        if (configurationBytes > _options.MaxConfigurationBytes) { throw new ArgumentException("Session configuration is too large.", nameof(configuration)); }
        try
        {
            return checked(16L * 1024 * 1024 + configurationBytes * 2 + traceOptions.Value.MaxRetainedBytes
                + configuration.MaxStoredChatResponseBytes + configuration.MaxVideoBytes
                + configuration.MaxPromptCacheEntries * 256L + configuration.MaxVideoJobs * 512L
                + configuration.MaxStoredChatResponses * 512L);
        }
        catch (OverflowException exception) { throw new ArgumentException("Session capacity is too large.", nameof(configuration), exception); }
    }

    internal sealed class State(LlmTestServerRuntime runtime, LlmTestServerProviderHttpTraceStore traceStore, byte[] keyHash, long reservedBytes)
    {
        internal LlmTestServerRuntime Runtime { get; set; } = runtime;
        internal LlmTestServerProviderHttpTraceStore TraceStore { get; } = traceStore;
        internal byte[] KeyHash { get; } = keyHash;
        internal long ReservedBytes { get; set; } = reservedBytes;
    }
}

internal sealed class LlmTestServerSessionRequestScope(IHttpContextAccessor accessor, LlmTestServerSessionRegistry registry)
{
    internal LlmTestServerSessionRegistry.State State { get; } = registry.Resolve(accessor.HttpContext) ?? registry.Default;
}
