using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ManagedCode.LlmTck.SystemOne;

/// <summary>Native SystemOne serialization, including required nullable Box confidence.</summary>
public static class SystemOneNativeJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(SystemOneDecisionAnswer)) { return; }
            var confidence = info.Properties.Single(property => property.Name == "confidence");
            confidence.ShouldSerialize = (answer, value) => ((SystemOneDecisionAnswer)answer).Type == SystemOneDecisionTypes.Box || value is not null;
        });
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = resolver };
        options.MakeReadOnly();
        return options;
    }
}
