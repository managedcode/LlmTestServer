using System.Security.Cryptography;
using System.Text;
using ManagedCode.LlmTestServer.Scenarios;

namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private readonly Dictionary<string, StoredChatResponse> _chatResponses = new(StringComparer.Ordinal);
    private long _chatResponseBytes;

    private LlmTestServerChatResult? RestoreChatResponse(ref LlmTestServerChatRequest request, string? token)
    {
        if (!request.StoreResponse && request.PreviousResponseId is null) { return null; }
        if (string.IsNullOrWhiteSpace(request.HistoryNamespace))
        {
            return LlmTestServerChatResult.Failure(request.ModelId, 400, "invalid_history_namespace", "Response history requires an exact namespace.");
        }
        if (request.PreviousResponseId is null) { return null; }
        if (!_chatResponses.TryGetValue(request.PreviousResponseId, out var previous)
            || previous.Namespace != request.HistoryNamespace
            || previous.TokenHash != TokenHash(token))
        {
            return LlmTestServerChatResult.Failure(request.ModelId, 404, "response_not_found", "The previous response is unavailable in this scope.");
        }
        request = request with
        {
            Messages = [.. request.Messages.Where(IsInstruction), .. SnapshotHistory(previous.Messages), .. request.Messages.Where(message => !IsInstruction(message))]
        };
        return null;
    }

    private LlmTestServerChatResult RetainChatResponse(LlmTestServerChatRequest request, LlmTestServerChatResult result, string? token)
    {
        if (!request.StoreResponse) { return result; }
        var messages = SnapshotHistory(request.Messages);
        messages.Add(new LlmTestServerMessage { Role = "assistant", Content = result.Content, OutputDisposition = LlmTestServerOutputDisposition.ForHistory(result.OutputDisposition), ToolCalls = [.. result.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(result.OutputItems) });
        var bytes = HistoryBytes(messages) + Encoding.UTF8.GetByteCount(request.HistoryNamespace!);
        if (_chatResponses.Count >= _configuration.MaxStoredChatResponses || bytes > _configuration.MaxStoredChatResponseBytes - _chatResponseBytes)
        {
            return LlmTestServerChatResult.Failure(request.ModelId, 409, "llm_test_server_response_capacity_exceeded", "Response history capacity exceeded. Reset the runtime or increase its configured capacity.");
        }
        var id = "resp_" + (result.WireMetadata ?? GetResponseMetadata()).IdFor("response");
        _chatResponses.Add(id, new StoredChatResponse(request.HistoryNamespace!, TokenHash(token), messages));
        _chatResponseBytes += bytes;
        return result with { ResponseId = id };
    }

    private static List<LlmTestServerMessage> SnapshotHistory(IEnumerable<LlmTestServerMessage> messages)
    {
        return messages.Where(message => !IsInstruction(message)).Select(message => message with { OutputDisposition = LlmTestServerOutputDisposition.ForHistory(message.OutputDisposition), ToolCalls = [.. message.ToolCalls], OutputItems = LlmTestServerResponseItems.Snapshot(message.OutputItems) }).ToList();
    }

    private static bool IsInstruction(LlmTestServerMessage message)
    {
        return message.Role is "system" or "developer";
    }

    private static string TokenHash(string? token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token ?? string.Empty)));
    }

    private static long HistoryBytes(IEnumerable<LlmTestServerMessage> messages)
    {
        return 256L + messages.Sum(message =>
        256L + Encoding.UTF8.GetByteCount(message.Role) + Encoding.UTF8.GetByteCount(message.Content)
        + Encoding.UTF8.GetByteCount(message.ToolCallId ?? string.Empty)
        + OutputDispositionBytes(message.OutputDisposition)
        + LlmTestServerResponseItems.Bytes(message.OutputItems)
        + message.ToolCalls.Sum(call => 128L + Encoding.UTF8.GetByteCount(call.Id)
            + Encoding.UTF8.GetByteCount(call.Name) + Encoding.UTF8.GetByteCount(call.ArgumentsJson) + Encoding.UTF8.GetByteCount(call.ContinuationToken ?? string.Empty)));
    }

    private sealed record StoredChatResponse(string Namespace, string TokenHash, List<LlmTestServerMessage> Messages);
}
