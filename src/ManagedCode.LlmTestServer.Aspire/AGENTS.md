# ManagedCode.LlmTestServer.Aspire

This package owns Aspire AppHost extensions.

Extension methods should be declarative and should configure the package-owned `LlmTestServerResource` through Aspire primitives. Consumer examples must use `builder.AddLlmTestServer()` without service project paths, generated `Projects.*` metadata types, Docker, or a container runtime. Keep container hosting as an explicit opt-in API such as `builder.AddLlmTestServerContainer()`, not the default local AppHost path. Every behavior change needs an Aspire integration test that starts the AppHost.

The resource's `http` endpoint is the provider endpoint for Aspire consumers. Do not add a separate provider-endpoint setter or hardcoded loopback URL; callers must use `GetEndpoint("http")` or `CreateHttpClient(...)` from the running Aspire resource.

Do not add `LLM_TEST_SERVER_*_COMPATIBILITY` environment flags or fluent compatibility methods unless they drive real route/runtime behavior; Aspire compatibility proof belongs in provider-client tests, not config echo endpoints.
