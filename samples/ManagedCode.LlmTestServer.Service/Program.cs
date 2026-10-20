using ManagedCode.LlmTestServer.Hosting;
using ManagedCode.LlmTestServer.Models;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var aspirePort = builder.Configuration["PORT"];
if (!string.IsNullOrWhiteSpace(aspirePort)
    && string.IsNullOrWhiteSpace(builder.Configuration["ASPNETCORE_URLS"])
    && string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
{
    if (!int.TryParse(aspirePort, out var port))
    {
        throw new InvalidOperationException("PORT must be a valid integer when supplied.");
    }

    builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(port));
}

var requiredBearerToken = LlmTestServerHostingConfiguration.GetRequiredBearerToken(builder.Configuration);
builder.Services.AddLlmTestServer(options =>
{
    options.AddDefaultOpenAiModels();

    if (!string.IsNullOrWhiteSpace(requiredBearerToken))
    {
        options.RequireBearerToken(requiredBearerToken);
    }

    options.AddChatScenario(
        "default-blue-whale",
        scenario => scenario
            .ForModel(LlmTestServerKnownModelIds.Gpt41Mini)
            .WhenUserContains("color")
            .Responds("blue whale", "blue ", "whale")
            .Responds("blue whale", "blue ", "whale")
    );
});

var app = builder.Build();

app.MapDefaultEndpoints();
app.MapLlmTestServer();
app.Run();
