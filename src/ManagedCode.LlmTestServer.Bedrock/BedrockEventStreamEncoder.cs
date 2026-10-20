using System.Text.Json;
using Amazon.Runtime.EventStreams;

namespace ManagedCode.LlmTestServer.Bedrock;

/// <summary>Encodes a Bedrock event using the official AWS event-stream framing implementation.</summary>
public static class BedrockEventStreamEncoder
{
    public static byte[] Encode(object streamEvent, JsonSerializerOptions? options = null)
    {
        var envelope = JsonSerializer.SerializeToElement(streamEvent, options);
        var properties = envelope.EnumerateObject();
        if (!properties.MoveNext())
        {
            throw new ArgumentException("An event must contain one named payload.", nameof(streamEvent));
        }

        var payloadProperty = properties.Current;
        if (properties.MoveNext())
        {
            throw new ArgumentException("An event must contain only one named payload.", nameof(streamEvent));
        }

        var message = new EventStreamMessage(
            [
                CreateHeader(":message-type", "event"),
                CreateHeader(":event-type", payloadProperty.Name),
                CreateHeader(":content-type", "application/json"),
            ],
            JsonSerializer.SerializeToUtf8Bytes(payloadProperty.Value, options)
        );
        return message.ToByteArray();
    }

    private static EventStreamHeader CreateHeader(string name, string value)
    {
        var header = new EventStreamHeader(name);
        header.SetString(value);
        return header;
    }
}
