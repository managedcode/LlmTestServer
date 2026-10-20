# Tests

Tests use TUnit and Microsoft.Testing.Platform.

Keep only integration tests. Start the server and call its APIs through HTTP or real provider SDKs, with supported parameters, streaming, failures, and full tool/conversation flows. Do not retain unit tests, fake clients, direct runtime/builder tests, source/document scans, or reflection-based test guards.

Keep HTTP endpoint integration tests on `Microsoft.AspNetCore.TestHost`, and Aspire integration tests on `Aspire.Hosting.Testing` with the AppHost model built directly in test code or a test fixture. Inspect state through the server's public control APIs instead of resolving internal runtime services as a substitute for observable behavior.
