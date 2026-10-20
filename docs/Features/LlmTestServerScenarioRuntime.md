# Feature: Scenario Runtime

## Summary

The scenario runtime lets tests emulate LLM provider responses without calling an external model. A test can script response text, stream chunks, auth failures, model IDs, and deterministic modality fixtures.

## Supported In This Slice

- Model list endpoint for chat, embedding, image, and audio models.
- Chat completion scenario matching by model and user-message content.
- Streaming chat completion using server-sent events.
- Deterministic embeddings for one or more input values.
- Deterministic image generation response with base64 PNG data.
- Deterministic audio speech response bytes.
- Global bearer-token enforcement.
- Optional bearer-token enforcement for control endpoints when a token is configured.
- Explicit unknown-model errors for requests that use missing models or the wrong modality.
- Provider-neutral fault simulation for configured `429 too_many_requests` and `400 content_filter` responses across all provider families.
- OpenAI-style bad request envelopes for malformed provider request bodies.
- Control endpoints for configure, reset, and assertion summary.
- Deterministic tiktoken-compatible usage for runtime events and provider response envelopes.
- `Microsoft.Extensions.AI` chat, embedding, and image clients.
- Aspire AppHost integration tested through `Aspire.Hosting.Testing`.

## Deferred

- Tool-call scenario scripting.
- Structured-output schema assertions.
- Provider packages for Anthropic, Gemini, and Groq.
- Request transcript export.
- Latency and timeout profiles per scenario.
