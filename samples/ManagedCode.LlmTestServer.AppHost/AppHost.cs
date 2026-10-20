using ManagedCode.LlmTestServer.Aspire;

var builder = DistributedApplication.CreateBuilder(args);

builder
    .AddLlmTestServer()
    .WithApiKey("test-key");

builder.Build().Run();
