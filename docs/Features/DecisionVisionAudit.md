# Native decision vision compatibility audit — 2026-10-20

Official references: [Cloudflare Clef](https://developers.cloudflare.com/workers-ai/models/clef/),
[TypeSafe OpenAPI](https://api.typesafe.ai/openapi.json), [images](https://docs.typesafe.ai/images),
[box](https://docs.typesafe.ai/primitives/box), [models](https://docs.typesafe.ai/models).

## Plan

1. Audit native DTOs, validation, HTTP routes, clients and existing tests against the official contracts.
2. Batch confirmed discrepancies: TypeSafe minimum side is 280px; box confidence may be null;
   physical JSON-body budgets must include surrounding whitespace; preserve Kev isolation.
3. Add a typed Cloudflare decision client for both Clef variants and both embedded image forms,
   preserving native responses/errors and caller-owned HTTP lifetime. Keep remote URLs rejected
   at the provider boundary; URL download belongs to the consuming platform.
4. Cover these contracts through deterministic HTTP/client regressions and rerun provider
   contract/evidence cases plus the full format/Release-build/test/coverage gate and NuGet audit.
5. Self-review the complete batch, increment the patch once, commit/push scoped changes, follow
   the release and verify all packages in NuGet. Update the consumer once and run regressions.

## Compatibility boundaries

Clef uses images beside state/questions, as data URLs or content_type/base64 objects, with
four-image, 4 MiB per-file, 16-megapixel, 8 MiB total and 13 MiB request budgets. Jev uses
reference-keyed PNG/JPEG assets, JSON or multipart, the explicit images header, 280–1568px
sides, 16 MiB per file and a 32 MiB physical body budget. Box needs Jev 1.14 or preview.
Long-context opt-in remains an explicit per-call header.

This deterministic TCK validates transport/file metadata and preserves scripted answers/usage.
It does not emulate provider inference, tokenization or image re-encoding; processed/expanded
image-memory and context-limit failures use explicit scripted provider errors. Do not infer
post-processing budgets from compressed input sizes. TypeSafe's GET models API is outside
the currently claimed decision-create compatibility surface.

## Combined local verification

The complete batch targets one patch release, **0.1.18**. All **669 tests passed**
without failures or skips, including the provider contract/evidence matrix and
Aspire-hosted integration. Production line coverage is **91.35%**, above the 90%
gate. Formatting verification, the Release build and diff checks passed; all 22
projects have no known direct or transitive NuGet vulnerabilities. Nullable Box
confidence is retained by both scenario configuration/runtime validation and
`SystemOneNativeJson.Options`, without adding confidence to Noul responses.

Release delivery still requires a successful GitHub workflow and verified NuGet
availability before any consuming package reference is changed. The TCK never
contacts production Cloudflare/TypeSafe inference APIs during these tests.
