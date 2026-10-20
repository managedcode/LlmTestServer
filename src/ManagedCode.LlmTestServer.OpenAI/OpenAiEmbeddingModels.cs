using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedCode.LlmTestServer.Models;

namespace ManagedCode.LlmTestServer.OpenAI;

public sealed record OpenAiEmbeddingRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.TextEmbedding3Small;

    [JsonPropertyName("input")]
    public JsonElement Input { get; init; }

    [JsonPropertyName("encoding_format")]
    public string? EncodingFormat { get; init; }

    [JsonIgnore]
    public bool UsesBase64Encoding =>
        string.Equals(EncodingFormat, "base64", StringComparison.OrdinalIgnoreCase);
}

public sealed record OpenAiEmbeddingResponse
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "list";

    [JsonPropertyName("model")]
    public string Model { get; init; } = LlmTestServerKnownModelIds.TextEmbedding3Small;

    [JsonPropertyName("data")]
    public List<OpenAiEmbeddingData> Data { get; init; } = [];

    [JsonPropertyName("usage")]
    public OpenAiUsage Usage { get; init; } = new();
}

public sealed record OpenAiEmbeddingData
{
    [JsonPropertyName("object")]
    public string ObjectType { get; init; } = "embedding";

    [JsonPropertyName("index")]
    public int Index { get; init; }

    [JsonPropertyName("embedding")]
    public JsonElement Embedding { get; init; } = JsonSerializer.SerializeToElement(Array.Empty<float>());

    [JsonIgnore]
    public float[] Vector => ReadVector(Embedding);

    public static OpenAiEmbeddingData FromVector(
        int index,
        IReadOnlyList<float> vector,
        bool encodeAsBase64
    )
    {
        return new()
        {
            Index = index,
            Embedding = encodeAsBase64
                ? JsonSerializer.SerializeToElement(ToBase64(vector))
                : JsonSerializer.SerializeToElement(vector),
        };
    }

    private static float[] ReadVector(JsonElement embedding)
    {
        return embedding.ValueKind switch
        {
            JsonValueKind.Array => embedding
                .EnumerateArray()
                .Select(value => value.GetSingle())
                .ToArray(),
            JsonValueKind.String => FromBase64(embedding.GetString() ?? string.Empty),
            _ => [],
        };
    }

    private static string ToBase64(IReadOnlyList<float> vector)
    {
        var bytes = new byte[vector.Count * sizeof(float)];
        for (var index = 0; index < vector.Count; index++)
        {
            var valueBytes = BitConverter.GetBytes(vector[index]);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(valueBytes);
            }

            valueBytes.CopyTo(bytes, index * sizeof(float));
        }

        return Convert.ToBase64String(bytes);
    }

    private static float[] FromBase64(string value)
    {
        var bytes = Convert.FromBase64String(value);
        var vector = new float[bytes.Length / sizeof(float)];
        for (var index = 0; index < vector.Length; index++)
        {
            var slice = bytes.AsSpan(index * sizeof(float), sizeof(float));
            if (BitConverter.IsLittleEndian)
            {
                vector[index] = BitConverter.ToSingle(slice);
                continue;
            }

            var valueBytes = slice.ToArray();
            Array.Reverse(valueBytes);
            vector[index] = BitConverter.ToSingle(valueBytes);
        }

        return vector;
    }
}
