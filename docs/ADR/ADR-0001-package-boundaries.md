# ADR-0001: Package Boundaries

## Status

Accepted

## Context

LLM Test Server needs to support several provider protocols while keeping deterministic test behavior consistent. Mixing provider DTOs, runtime state, host wiring, and client abstractions in one package would make future providers harder to add and harder to test.

## Decision

Use separate packages:

- Core runtime: `ManagedCode.LlmTestServer`
- Provider wire mapping: `ManagedCode.LlmTestServer.OpenAI`
- ASP.NET Core hosting: `ManagedCode.LlmTestServer.Hosting`
- Typed clients and `Microsoft.Extensions.AI`: `ManagedCode.LlmTestServer.Client`
- Aspire AppHost extensions: `ManagedCode.LlmTestServer.Aspire`

## Consequences

Provider adapters can evolve independently. Tests can cover package boundaries directly. Consumers can reference only the surfaces they need.
