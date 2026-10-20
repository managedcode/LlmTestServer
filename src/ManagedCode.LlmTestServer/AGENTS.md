# ManagedCode.LlmTestServer

Keep this package provider-neutral. It owns deterministic scenarios, model metadata, runtime assertions, and modality fixtures.

Do not add HTTP, Aspire, or provider wire DTO dependencies here. Add behavior through small immutable records and focused runtime tests.
