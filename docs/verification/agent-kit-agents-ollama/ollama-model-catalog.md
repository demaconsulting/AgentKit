## OllamaModelCatalog Unit Verification Design

This document describes the unit-level verification strategy for the `OllamaModelCatalog` class.

### Verification Approach

`OllamaModelCatalog` is verified at two levels, matching the two things it does.

**The read is verified through the real client over HTTP.** Most scenarios start a WireMock.Net
server on loopback that replays the model listing (`GET /api/tags`) and each model's description
(`POST /api/show`) exactly as a live Ollama 0.34.2 server sent them, and drive a **real**
`OllamaApiClient` against it. Using the real client is the point: a hand-faked `IOllamaApiClient`
would assert this project's beliefs about OllamaSharp's deserialization back at it rather than test
them. The captured server holds five models — `gemma4:26b` and `qwen3.8:27b`, whose vision comes
from a projector, `gemma4:e4b`, whose newer engine embeds vision with no projector, and the
embedding models `nomic-embed-text` and `mxbai-embed-large` — so the mapping is exercised on what
Ollama actually sends for each kind of model. One scenario serves the listing but answers every
description with an error, to reach the per-model failure path.

**The mapping is verified on payloads, without a server.** `Map` is an internal pure function, so
the scenarios that need a shape no current server sends call it directly. The payloads named
`synthetic-pre-0-6-4` are the captured descriptions with the capability list removed, standing in
for a server too old to report capabilities; the remaining direct scenarios hand-build an
OllamaSharp description holding only the fields under test.

**Two scenarios reach across units.** The selection scenario calls `SelectAsync` on the catalog, so
the refusal comes from the Core validation on the trained length this unit reported, and asserts
that no chat request reached the server. The bare-name scenario exercises the shared tagging rule
from *OllamaContextWindow Unit Design* through the catalog's lookup.

**What remains outside automated scope.** The payloads are replayed rather than re-fetched, so a
future Ollama that changed the shape of its listing or description would not be detected until the
payloads were captured again. Whether a model honors an effort level is the model's own, and is not
asserted anywhere.

#### Supplementary Live Evidence

The opt-in live suite `OllamaModelCatalogLiveTests`, in the
`DemaConsulting.AgentKit.Agents.ChatClient.LiveTests` project, runs the catalog and a selection
against a real server when `AGENTKIT_OLLAMA_LIVE_MODEL` names a vision-capable tool-calling model.
It is supplementary evidence rather than a linked scenario, because it needs a server and models
the CI pipeline does not have. Run against `gemma4:26b`, `qwen3.8:27b` and `gemma4:e4b` with a
window of 8,192 tokens, it observed:

- all three listed as vision, tool-calling and thinking models, trained for 262,144, 262,144 and
  131,072 tokens respectively, and both embedding models listed as unusable;
- `think` sent as `false`, `"low"` and `"high"` for the efforts `none`, `low` and `high`, and
  `num_ctx` sent as 8,192, on the wire as AgentKit wrote it;
- the server loading the model at 8,192 tokens, as its running-models report showed;
- a window one token above the trained length refused by selection before any chat request was sent.

Unit tests reside in `OllamaModelCatalogTests.cs`, with the captured payloads under `Fixtures`,
within the `DemaConsulting.AgentKit.Agents.Ollama.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None; no Ollama server is contacted and **no outbound network access is
  made**. The HTTP scenarios start a WireMock.Net server on loopback and stop it with the test that
  started it
- **Files**: The payloads are embedded resources: `tags-five-models.json` and the five `show-*.json`
  descriptions, captured verbatim from a live Ollama 0.34.2 server except that each model's license
  and model-file text are elided for size, and the three `synthetic-pre-0-6-4-*.json` payloads
- **Mocking**: WireMock.Net replays the captured payloads so the real `OllamaApiClient` executes its
  own request shaping and deserialization; the direct scenarios use hand-built OllamaSharp values
- **Isolation**: Each test starts its own server and constructs its own client and catalog; no state
  is shared

### Acceptance Criteria

A unit test run passes when all fifteen scenarios below pass without error or exception beyond
those explicitly asserted. Any capability reported that the server did not report, or omitted that
it did; any vision model hidden because it has no projector; any model reported usable without both
completion and tools, or unusable without its reason; any older server's model reported usable or
with vision not inferred from its metadata; any trained length misreported, any ceiling invented
where the metadata carries none, or any window above the trained length accepted or sent to the
server; any effort set other than the documented one, or any default effort published; any listing
that fails because one model could not be described; any bare name that does not find its
`latest` tag; or any listing in an order other than the server's each constitute a failure.

### Test Scenarios

#### AgentKitAgentsOllama-OllamaModelCatalog-ReportsTheServersCapabilities: Captured Vision Models

**Test**: `OllamaModelCatalog_ListAsync_CapturedServer_DescribesTheVisionModels`

Normal operation over the captured server. A vision-and-tools listing returns exactly the three
vision models, in the server's order, each usable, carrying the provider name, reporting vision, tool
calling and reasoning, and publishing no default effort.

#### AgentKitAgentsOllama-OllamaModelCatalog-ReportsTheServersCapabilities: Vision Without a Projector

**Test**: `OllamaModelCatalog_GetAsync_VisionWithoutProjector_IsVision`

The model whose engine embeds vision with no projector is reported as vision-capable, because the
server's own report is used, and carries its listed family as an additional property.

#### AgentKitAgentsOllama-OllamaModelCatalog-UsableOnlyWithCompletionAndTools: Embedding Model Unusable

**Test**: `OllamaModelCatalog_ListAsync_EmbeddingModel_IsUnusable`

The default listing omits the embedding models; a lookup still finds `nomic-embed-text:latest`,
unusable with the reason "embedding-only model" and its trained length of 2,048 tokens.

#### AgentKitAgentsOllama-OllamaModelCatalog-UsableOnlyWithCompletionAndTools: Completion Without Tools

**Test**: `OllamaModelCatalog_Map_CompletionWithoutTools_IsUnusable`

Error path. A model reporting completion and vision but not tools is unusable with "Model does not
support tool calling." and still reports vision.

#### AgentKitAgentsOllama-OllamaModelCatalog-InfersCapabilitiesFromAnOlderServer: From a Projector

**Test**: `OllamaModelCatalog_Map_PreCapabilityServerWithProjector_InfersVision`

A description with a projector and no capability list yields vision and nothing else, is unusable
with the message requiring Ollama 0.6.4 or later, and carries the inferred-capabilities notice.

#### AgentKitAgentsOllama-OllamaModelCatalog-InfersCapabilitiesFromAnOlderServer: From Vision Keys Only

**Test**: `OllamaModelCatalog_Map_PreCapabilityServer_InfersVisionFromVisionKeysOnly`

A description with no projector but architecture vision keys yields vision; a text-only description
with neither does not, so the inference does not mark every model as vision.

#### AgentKitAgentsOllama-OllamaModelCatalog-ReportsTrainedLengthAsCeiling: Trained Lengths Reported

**Test**: `OllamaModelCatalog_ListAsync_CapturedServer_DescribesTheVisionModels`

The same listing as above, asserted for its ceilings: the three vision models report trained lengths
of 262,144, 262,144 and 131,072 tokens, read from each architecture's context-length metadata.

#### AgentKitAgentsOllama-OllamaModelCatalog-ReportsTrainedLengthAsCeiling: No Length, No Ceiling

**Test**: `OllamaModelCatalog_Map_NoContextLength_ReportsNoCeiling`

Boundary condition. Metadata with an architecture but no context length yields no ceiling rather
than an invented one; the model is still usable and, not thinking, is offered no efforts.

#### AgentKitAgentsOllama-OllamaModelCatalog-ReportsTrainedLengthAsCeiling: Window Above It Refused

**Test**: `OllamaModelCatalog_SelectAsync_WindowAboveTrainedLength_IsRefused`

Boundary condition across units. A window of 131,073 tokens for a model trained for 131,072 is
refused at selection with the formatted maximum, and the server logged no chat request.

#### AgentKitAgentsOllama-OllamaModelCatalog-OffersEffortsAThinkingModelHonors: Four Efforts

**Test**: `OllamaModelCatalog_GetAsync_ThinkingModel_OffersFourEfforts`

Normal operation. A thinking model offers `none`, `low`, `medium` and `high`, mapped to the neutral
`None`, `Low`, `Medium` and `High`, and its adjustments are the effort, the window and the output
limit.

#### AgentKitAgentsOllama-OllamaModelCatalog-OffersEffortsAThinkingModelHonors: GPT-OSS Not Offered Off

**Test**: `OllamaModelCatalog_Map_GptOss_IsNotOfferedOff`

A thinking model of the `gptoss` family is offered `low`, `medium` and `high` only, because it cannot
stop thinking.

#### AgentKitAgentsOllama-OllamaModelCatalog-ToleratesAModelItCannotDescribe: Listed Unusable

**Test**: `OllamaModelCatalog_ListAsync_DescriptionFails_ModelIsUnusable`

Error path. A server that lists five models but answers every description with an error still
yields a listing of five, each unusable with "Ollama did not describe this model."

#### AgentKitAgentsOllama-OllamaModelCatalog-MatchesAnUntaggedModelName: Bare Name Finds Latest

**Test**: `OllamaModelCatalog_GetAsync_BareName_FindsTheLatestTag`

A lookup of `nomic-embed-text` finds the model the server lists as `nomic-embed-text:latest`.

#### AgentKitAgentsOllama-OllamaModelCatalog-KeepsTheServersOrder: Listed in the Server's Order

**Test**: `OllamaModelCatalog_ListAsync_KeepsTheServersOrder`

Normal operation over the captured server, whose listing is not in name order. A listing that
includes unusable models returns all five installed models exactly in the order `/api/tags` lists
them: `gemma4:26b`, `mxbai-embed-large:latest`, `nomic-embed-text:latest`, `qwen3.8:27b`,
`gemma4:e4b`.

#### AgentKitAgentsOllama-OllamaModelCatalog-KeepsTheServersOrder: Filtered Listing Keeps the Order

**Test**: `OllamaModelCatalog_ListAsync_CapturedServer_DescribesTheVisionModels`

The vision-and-tools listing above returns `gemma4:26b`, `qwen3.8:27b`, `gemma4:e4b` — the server's
order with the other models filtered out, not re-sorted.
