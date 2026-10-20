# Gemini embeddings through the official SDK

`Google.GenAI` 1.26.1 uses `batchEmbedContents` for both its single-input and multiple-input
`Models.EmbedContentAsync` overloads. The server exposes the distinct documented operation
`POST /gemini/v1beta/models/{model}:batchEmbedContents`; the existing `embedContent` operation
remains its own API.

Each request contains 1–128 entries in `requests` (a local fixture bound). Each entry supplies
`model: "models/<path-model>"`, a non-empty text-only `content`, and optional
`outputDimensionality`. The current embedding fixture uses the documented stable
[`gemini-embedding-2`](https://ai.google.dev/gemini-api/docs/models/gemini-embedding-2) ID.
The SDK accepts model strings; it has no closed model enum to copy.

Reduced output dimensions are bounded to 128–3072 for this contract. The runtime validates all
requested dimensions against the configured fixture vector under the same admission lock before
recording success or evaluating scripted provider faults. A fixture with too few coordinates
returns 409 instead of silently returning a shorter vector. Successful batches preserve input
order and project each fixture vector to its requested prefix. The server does not pad vectors
or perform embedding inference.

Malformed or unsupported request shapes return 400. Model/path mismatches, missing or oversized
batches, non-text parts and unsupported `taskType`/`title` options are rejected explicitly.
Authentication, unknown model, configured rate limits, content filters and scripted errors use the
normal provider/runtime paths. One batch is one runtime operation; it does not partially record
successful entries before another entry fails.

The REST response contains ordered `embeddings[].values` and `usageMetadata.promptTokenCount`.
The pinned SDK does not expose this usage field, so SDK tests prove vectors and request behavior;
normalized REST token accounting is a separate observation, not provider billing parity.

Sources: [batch API](https://ai.google.dev/api/embeddings#method:-models.batchembedcontents),
[official SDK](https://github.com/googleapis/dotnet-genai),
[SDK model routing](https://github.com/googleapis/dotnet-genai/blob/c071e7d18f779bf166a66c0a7ff715ab24035273/Google.GenAI/Models.cs#L4635).
