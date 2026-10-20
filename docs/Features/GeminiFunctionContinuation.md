# Gemini function-call continuation metadata

The qualified `gemini-3.8-flash` GenerateContent JSON and SSE routes preserve a fixture tool call's
opaque `ContinuationToken` as the function part's base64 `thoughtSignature`. Forward the original
signed candidate parts through the official Google SDK when sending function results.

Each current-turn model step requires a signature on its first function call. Parallel siblings
may omit it. Earlier steps in the same turn must retain their signatures; unsigned history before
the latest ordinary user turn remains valid. Missing or malformed signatures return HTTP 400
before scenario consumption. A changed signature for the last response retained in the local
conversation returns a fixture mismatch (409), allowing an explicit retry with the original parts.

Signatures count toward bounded history and diagnostic retention and participate in cache identity.
They do not inflate visible text-token usage. The local maximum is 16,384 base64 characters; it is
an explicit fixture retention policy, not a claimed Google limit.

This contract preserves function-call metadata only. Arbitrary text-part signatures, signature-only
streaming text parts and general signed multimodal history remain outside this slice. The server
does not authenticate Google's private cryptographic signatures. Exact local comparison covers
the last retained response; older current-turn signatures receive structural validation only.

Sources: [Google thought signatures](https://ai.google.dev/gemini-api/docs/generate-content/thought-signatures),
[Gemini 3.8 Flash](https://ai.google.dev/gemini-api/docs/models/gemini-3.8-flash),
[official .NET SDK](https://github.com/googleapis/dotnet-genai).

The official Google.GenAI 1.26.1 running-server suite covers JSON/SSE signed rounds, missing and
changed metadata with recovery, parallel/sequential calls, prior turns, malformed input and atomic
retention rejection. All 36 Google SDK integration cases pass. Raw HTTP supplements malformed
base64 cases; successful continuation and public SDK errors use the original SDK.
