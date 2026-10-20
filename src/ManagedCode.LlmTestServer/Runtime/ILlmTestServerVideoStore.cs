namespace ManagedCode.LlmTestServer.Runtime;

public interface ILlmTestServerVideoStore
{
    LlmTestServerVideoResult StoreVideo(string provider, LlmTestServerVideoResult video);
    IReadOnlyList<LlmTestServerVideoResult> ListVideos(string provider);
    LlmTestServerVideoResult? FindVideo(string provider, string id, bool generation = false);
    bool DeleteVideo(string provider, string id);
}
