## TokenEstimator

### Purpose

`TokenEstimator` provides deterministic fallback token estimates. It is used when a provider reports
no usage, when fixed overhead must be estimated from application configuration, and when the engine
sizes a replacement seed before sending it.

### Data Model

Internal constants:

- **`CharactersPerToken`** (`int`) — Four characters per token.
- **`PerEntryOverheadTokens`** (`int`) — Four tokens of framing per transcript entry.
- **`PerToolOverheadTokens`** (`int`) — Eight tokens of framing per tool declaration.

The estimator is internal. It is not a provider tokenizer and is not used to override a provider's own
usage report.

### Key Methods

#### EstimateTokens(string? text)

**Purpose:** Estimate the token cost of text using the deterministic character ratio.

**Algorithm:** Null or empty text estimates as zero. Non-empty text is rounded up by the
four-characters-per-token ratio, so short present text is not free.

**Preconditions:** None; null is accepted.

**Postconditions:** The result is non-negative and deterministic.

#### EstimateEntryTokens(TranscriptEntry entry)

**Purpose:** Estimate one transcript entry including message framing.

**Algorithm:** Require an entry, estimate its text, and add the per-entry overhead.

**Preconditions:** `entry` is not null.

**Postconditions:** The result is at least the per-entry overhead.

#### EstimateToolDeclarationTokens(IReadOnlyList&lt;AIFunction&gt;? tools)

**Purpose:** Estimate fixed overhead for the tool declarations sent with every provider request.

**Algorithm:** Null or empty tools cost zero. For each tool, add estimates for the name, description,
JSON schema and per-tool overhead. Accumulate in a wide integer and reject a total that cannot be
represented as a token count.

**Preconditions:** The tool list contains no null entries.

**Postconditions:** The result is non-negative and is suitable for subtracting from a configured
window when no provider report is available.

### Error Handling

- **Null transcript entry** — `ArgumentNullException` propagates.
- **Null tool entry** — `ArgumentException` propagates.
- **Tool declaration total too large to represent** — `ArgumentException` propagates.

### Dependencies

- **TranscriptEntry** — Entry estimates are used by transcript and layout accounting.
- **Microsoft.Extensions.AI.Abstractions** — Supplies `AIFunction` tool declarations.

### Callers

`AgentSessionOptions` estimates fixed overhead, `ContextLayout` estimates seed size,
`SessionTranscript` and `TranscriptEntry` cache entry estimates, `InMemoryProviderSession` reports
in-memory usage, and `CompactingAgentSession` falls back to estimates when providers report nothing.
