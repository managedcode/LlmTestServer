using System.ClientModel;
using Aspire.Hosting.Testing;
using ManagedCode.LlmTestServer.Aspire;
using ManagedCode.LlmTestServer.Client;
using OpenAI;
using OpenAI.Chat;

public sealed class ChatDialogueTests
{
    [Test]
    public async Task ClientReceivesScriptedDialogue(CancellationToken cancellationToken)
    {
        // 1. Start the server. Aspire allocates the address and port.
        var builder = DistributedApplicationTestingBuilder.Create([]);
        builder.AddLlmTestServer("llm").WithApiKey("test-key");
        await using var app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("llm", cancellationToken);

        // 2. Script the replies in the order the client will receive them.
        using var http = app.CreateHttpClient("llm", "http");
        var control = new LlmTestServerClient(http, "test-key");
        await control.ConfigureAsync(config => config
            .RequireBearerToken("test-key")
            .UseReasoningChatModel("gpt-6.1-sol", reasoningTokens: 8)
            .UseChatScenario("delivery", scenario => scenario
                .ForModel("gpt-6.1-sol")
                .Responds("Your order arrives on Friday.")
                .Responds("Yes, delivery is free.")), cancellationToken);

        // 3. Call the server through the official OpenAI SDK.
        var chat = new ChatClient("gpt-6.1-sol", new ApiKeyCredential("test-key"),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "openai/v1"),
            });
        List<ChatMessage> messages = [new UserChatMessage("When will my order arrive?")];
        ChatCompletion first = await chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        // 4. Continue the dialogue, including the previous answer in the history.
        messages.Add(new AssistantChatMessage(first));
        messages.Add(new UserChatMessage("Is delivery free?"));
        ChatCompletion second = await chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        // 5. Verify the answers and that both requests matched the scenario.
        await Assert.That(first.Content[0].Text).IsEqualTo("Your order arrives on Friday.");
        await Assert.That(second.Content[0].Text).IsEqualTo("Yes, delivery is free.");
        var recorded = await control.GetAssertionsAsync(cancellationToken);
        await Assert.That(recorded.Matched).IsEqualTo(2);
    }
}
