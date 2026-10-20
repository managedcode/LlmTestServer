namespace ManagedCode.LlmTestServer.Runtime;

public sealed partial class LlmTestServerRuntime
{
    private readonly Dictionary<(string Provider, string Id), LlmTestServerVideoResult> _videos = [];
    private long _videoBytes;
    private readonly Dictionary<string, long> _videoSequence = new(StringComparer.Ordinal);

    public LlmTestServerVideoResult StoreVideo(string provider, LlmTestServerVideoResult video)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentNullException.ThrowIfNull(video);
        lock (_gate)
        {
            if (IsActiveRequestSuperseded())
            {
                return CreateSupersededVideoResult(video.ModelId);
            }

            if (!video.IsSuccess)
            {
                return video;
            }

            if (_videos.Count >= _configuration.MaxVideoJobs || video.Bytes.LongLength > _configuration.MaxVideoBytes - _videoBytes)
            {
                const string message = "Video store capacity exceeded. Delete jobs or increase the configured video capacity.";
                AddEvent(LlmTestServerEventKind.ErrorReturned, null, video.ModelId, message);
                return video with { IsSuccess = false, StatusCode = 409, ErrorCode = "llm_tck_video_capacity_exceeded", ErrorMessage = message, Bytes = [] };
            }

            var sequence = _videoSequence.GetValueOrDefault(provider);
            string videoId;
            string generationId;
            do
            {
                sequence++;
                videoId = sequence == 1 ? video.VideoId : $"{video.VideoId}_{sequence}";
                generationId = sequence == 1 ? video.GenerationId : $"{video.GenerationId}_{sequence}";
            }
            while (_videos.ContainsKey((provider, videoId)) || _videos.Any(pair => pair.Key.Provider == provider && pair.Value.GenerationId == generationId));

            var stored = video with { VideoId = videoId, GenerationId = generationId, Bytes = [.. video.Bytes] };
            _videoSequence[provider] = sequence;
            _videos.Add((provider, stored.VideoId), stored);
            _videoBytes += stored.Bytes.LongLength;
            return stored with { Bytes = [.. stored.Bytes] };
        }
    }

    public IReadOnlyList<LlmTestServerVideoResult> ListVideos(string provider)
    {
        lock (_gate)
        {
            return _videos.Where(pair => pair.Key.Provider == provider)
                .Select(pair => pair.Value with { Bytes = [.. pair.Value.Bytes] }).ToList();
        }
    }

    public LlmTestServerVideoResult? FindVideo(string provider, string id, bool generation = false)
    {
        lock (_gate)
        {
            var video = generation
                ? _videos.FirstOrDefault(pair => pair.Key.Provider == provider && pair.Value.GenerationId == id).Value
                : _videos.GetValueOrDefault((provider, id));
            return video is null ? null : video with { Bytes = [.. video.Bytes] };
        }
    }

    public bool DeleteVideo(string provider, string id)
    {
        lock (_gate)
        {
            if (!_videos.Remove((provider, id), out var video))
            {
                return false;
            }

            _videoBytes -= video.Bytes.LongLength;
            return true;
        }
    }
}
