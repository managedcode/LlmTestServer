# Samples

Samples must stay runnable and minimal. They are examples of the public API, not hidden test fixtures.

Use the existing `ManagedCode.LlmTestServer.Hosting` service-defaults methods for Aspire health and liveness endpoints. Do not add a separate ServiceDefaults sample project, ad hoc `app.MapGet("/health", ...)`, or placeholder root status endpoints to sample `Program.cs` files.

When changing the sample service or AppHost, run the Aspire integration test in `tests/ManagedCode.LlmTestServer.Tests`.
