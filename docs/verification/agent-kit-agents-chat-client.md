# System Verification Design

This document describes the system-level verification strategy for the AgentKitAgentsChatClient
system.

## Verification Approach

The AgentKitAgentsChatClient system is verified through system-level integration tests that exercise
the package as a consumer would: they build an agent from a scripted chat client and a supplied tool
list, and they run an AgentKit Core session over a recording chat client, asserting on the observable
outcome. The behavioral guarantee the package exists for — that the image-promoting decorator is
installed on every agent it builds — is **asserted, not assumed**.

The decorator is proven in two parts. One scenario builds an agent through `Create` and asks that
agent for the decorator, so a construction path that stopped wrapping would fail; a second wraps a
scripted `IChatClient` exactly as the factory wraps one and shows a tool-returned image reaching that
client on a following user message, so the installed decorator is shown to do the job it is installed
for. The two together establish that an agent built by the factory talks to its provider through a
functioning image-promoting decorator.

The session adapter is proven end to end rather than inferred from its units. One scenario runs a
Core session over the adapter and asserts the occupancy it reports is the provider's own figure for
the prompt it was last sent, against the window the application stated. A second holds a short
conversation against a provider that reports a filling window on every turn, and asserts the whole
arrangement: the rotation happens on nothing but what the provider reported, the consolidation goes
out through a **separate** chat client, the record that client returns is found in the conversation
the provider is later sent, and the replacement session reports occupying nothing because it has not
been sent anything yet. That last assertion also pins the pipeline the session factory builds: a
replacement sharing its predecessor's prompt-size recorder would report the figure that provoked the
rotation, and the scenario would fail.

**What is out of automated scope, stated honestly.** A live end-to-end run against a real
`IChatClient` provider is not automated: the asymmetry the decorator exists for happens at a
provider's wire format, outside this process, and a real provider would add network access,
credentials, and a model's non-deterministic description of an image without observing the rewrite
this system performs. The same holds for the session adapter, where a live model would contribute
only its own tokenizer's arithmetic to figures this system deliberately does not compute. The
evidence that the promotion solves the real problem is the exploratory work recorded in the Core
*ImagePromotingChatClient Unit Design*, not a live-model test.

System tests reside in `AgentKitAgentsChatClientTests.cs` within the
`DemaConsulting.AgentKit.Agents.ChatClient.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **External services**: None. No provider is contacted and **no network access is used**; the
  OllamaSharp wire scenario replaces the network with a capturing HTTP handler. The
  image-promotion scenario uses a scripted `IChatClient` that contacts nothing, and the session
  scenarios use a recording `IChatClient` answering from a script
- **File system**: None; the image is a short byte array held in the test class
- **Isolation**: Each test constructs its own scripted client, tools, agent or session; no state is
  shared

## System-Level Test Scenarios

### Agent Construction: An Agent Carries Its Supplied Name

**Test**: `AgentKitAgentsChatClient_Create_BuildsAgentCarryingSuppliedName`

Verifies that the factory turns a chat client and a tool list into a usable agent. Builds an agent
from a scripted client, one tool, instructions, and a name, and asserts a non-null agent carrying the
supplied name is returned.

### Image Promotion: The Decorator Is Installed on an Agent the Factory Builds

**Test**: `AgentKitAgentsChatClient_Create_InstallsImagePromotingDecoratorOnTheAgent`

Verifies that an agent built through `Create` actually talks through the decorator, rather than that
the wrap seam would produce one if called. Builds an agent from a scripted client and one tool, then
asks the constructed agent's service chain for an `ImagePromotingChatClient` and asserts one is
found. This is the scenario that pins the construction path: a `Create` that stopped wrapping would
fail here while the promotion scenario below, which exercises the seam directly, would still pass.

### Image Promotion: A Tool-Returned Image Reaches the Provider on a User Message

**Test**: `AgentKitAgentsChatClient_ImagePromotion_ToolReturnedImage_ReachesProviderOnUserMessage`

Verifies that the installed decorator does the job it is installed for. Wraps a scripted client
exactly as the factory does, sends a conversation whose last message is a tool result holding an
image as the function-invocation loop would after the tool ran, and asserts the client received an
extra user message carrying the very same image instance. Paired with the scenario above, it
confirms an agent built by the factory delivers a tool-returned image to a provider that would
otherwise drop it.

### Image Promotion: A Host-Built Agent Promotes Through the Builder Extension

**Test**: `AgentKitAgentsChatClient_ImagePromotion_BuilderExtension_PromotesBeneathFunctionInvocation`

Verifies the path for a host that builds its own agent. A `ChatClientAgent` is constructed over
`client.AsBuilder().UseImagePromotion().Build()` with an image tool, and a recording provider is
scripted to call the tool and then answer. Asserts the run returns the answer and the provider's second
request carried only the placeholder in the tool result and the same image instance on a user message
— the agent's own function-invocation loop sits above the decorator, as the extension requires.

### Image Promotion: Over OllamaSharp the Tool Message Carries No Image Bytes

**Test**: `AgentKitAgentsChatClient_ImagePromotion_OverOllamaSharp_ToolMessageCarriesNoImageBytes`

Verifies the promotion at the HTTP wire, where the defect it fixes lives. The real OllamaSharp chat
client runs over `CapturingHttpHandler`, which records each request body and answers first with a
call to an image tool returning a 300 KB PNG and then with text; a `ChatClientAgent` over
`AsBuilder().UseImagePromotion()` drives it. Asserts the second `/api/chat` body's tool message is
under 300 characters, contains no `base64` and names `image/png`, and that the next message is a user
message whose `images` array holds one entry. Without the placeholder this body carried the image as
base64 text in the tool message.

### Context Provider: Tools Reach the Provider and Run

**Test**: `AgentKitAgentsChatClient_ContextProvider_ToolsReachTheProviderAndRun`

A tool-less `ChatClientAgent` with a `ToolPackContextProvider` over a pack. The recording provider
calls one of the pack's tools; asserts the tools were offered, the call ran, its result reached the
provider, and the run returned the answer.

### Context Provider: Conversations Are Isolated

**Test**: `AgentKitAgentsChatClient_ContextProvider_ConversationsAreIsolated`

Two sessions of one agent with retention configured. The first retains `output-1`; reading it in the
second is answered "never retained".

### Context Provider: A Deserialized Session Pages an Earlier Output

**Test**: `AgentKitAgentsChatClient_ContextProvider_DeserializedSession_PagesAnEarlierOutput`

A session retains a report and is serialized and deserialized through the agent; a search of
`output-1` in the reloaded session finds `250:report row 250`.

### Chat Reducer: The In-Memory History Provider Reduces Through AgentKit's Reducer

**Test**: `AgentKitAgentsChatClient_ChatReducer_InMemoryHistoryProvider_ReducesStoredHistory`

An agent whose `InMemoryChatHistoryProvider` carries an `AgentKitChatReducer` over a
`ChatClientSummarizer`, triggered before retrieval, runs four turns with long answers. Asserts the
summarizing client was called and the stored history holds exactly one record carrying the code
established in the first turn.

### Session Adapter: A Core Session Runs Over a Chat Client and Reports the Provider's Occupancy

**Test**: `AgentKitAgentsChatClient_Session_AnswersAndReportsTheProvidersOccupancy`

Verifies the plainest thing the session adapter must do. Creates a `CompactingAgentSession` over a
`ChatClientProviderSessionFactory` holding a recording client and a stated window, takes one turn,
and asserts the answer came back and the session's account of the window is the provider's own
reported figure against that window. The figure is one no estimate would arrive at, so an
implementation that computed rather than reported would fail here.

### Session Adapter: A Rotation Runs on the Provider's Usage and Seeds the Replacement

**Test**: `AgentKitAgentsChatClient_Session_RotatesOnTheProvidersUsage_AndSeedsTheReplacement`

Verifies the arrangement the session adapter exists for, end to end. A provider reporting a
conversation well past the rotation threshold of its window on every turn drives a short conversation;
a separate recording client performs the consolidations and returns a marked record. The scenario
asserts a rotation occurred, the consolidation went out through the summarizing client rather than
the conversational one, the marked record reached the provider as seeded history on a later turn, and
the replacement session reports occupying nothing of the window because it has not yet been sent
anything. Nothing but the provider's reported usage drives any of it.

### Large Tool Outputs: The Preview and the Page Travel Unchanged

**Test**: `AgentKitAgentsChatClient_ToolOutput_PreviewReachesTheProviderAndTheReaderPagesIt`

Composes a five-thousand-line tool with retention of large outputs and seeds it into a session over a
recording client that calls the tool, then calls `tool_output_read` for the last line, then answers.
Asserts the follow-up requests carried the bounded preview and then the exact page as the function
results, and that the turn's transcript records exactly those two results — never the oversized
output — so retention behaves through this adapter's function-invocation loop as it does anywhere.

### Model Selection: Every Request of a Turn Carries the Selection

**Test**: `AgentKitAgentsChatClient_ModelSelection_EveryRequestOfATurnCarriesTheSelection`

Verifies `AgentKitAgentsChatClient-ModelSelection-AppliedToEveryRequest`. A vision model is bound with
a low effort and handed to `ChatClientAgentFactory.Create` over a recording client that calls a tool
before it answers. The turn makes two requests, and both carry the selected model identifier and
`ReasoningEffort.Low` — including the one sent after the tool ran. The selection's host capabilities
are `Vision`, so the host's declaration follows the model.

## Acceptance Criteria

A system-level test run passes when all thirteen scenarios above pass without error or exception beyond
those explicitly asserted. Any agent that fails to build, any wrong name, any agent built without the
image-promoting decorator in its client chain, any tool-returned image that fails to reach the
provider on a user message, any content instance copied rather than carried, any session occupancy
that is not the provider's own figure against the stated window, any conversation that fails to
rotate when the provider reports a filling window, any consolidation sent through the conversational
client, any replacement session that fails to carry the consolidated record, any retained output's preview or page
altered on its way to the provider or the transcript, any image bytes in an OllamaSharp tool message, any
contributed tool not offered or run, any reference shared between conversations or lost across a session
reload, any stored history the reducer hook leaves unreduced, or any request of a turn that reaches
the provider without the model selection constitutes a failure.

## Supplementary Live Verification

The traced scenarios above stop at the seam. The opt-in live Ollama suite crosses it: it runs the
package's context provider, image-promotion extension and AgentKit's chat reducer in host-built
Agent Framework agents over OllamaSharp against a **real** Ollama server and model, and asserts what
was actually sent on the wire. It re-measures, on every run, the behaviors the AITool-first design
depends on — that tools contributed per run reach the model and that a retained output pages after
the session is serialized and restored, that a guarded foreign tool's result is bounded, that a
promoted image reaches the model with a small tool message, and that the reducer consolidates a
history the agent owns without losing the fact the conversation turns on. Two scenarios carry the
visual tools across the same seam: a model reads the diagram help, draws, reads a shaped failure,
fixes the source and sees the picture, and a model views an SVG figure and a PDF page while a
figure reaching outside the sandbox is refused.

**It is supplementary evidence and is not linked to ReqStream.** The suite needs a reachable Ollama
server, which the pipeline that builds the trace matrix does not have, so a requirement traced to a
live test would have no result there and would fail enforcement. The requirement evidence stays
with the hermetic tests above and in the unit chapters.

### Live Suite Location and Gating

- **Project**: `test/DemaConsulting.AgentKit.Agents.ChatClient.LiveTests/`, an xUnit v3 project
  targeting .NET 10 that references the ChatClient package, AgentKit's core and tools packages, and
  OllamaSharp directly. It is **not** part of `AgentKit.slnx`, so `build.ps1` and a `dotnet test` of
  the solution never build or run it. The merge gate compiles it, so an internal change that breaks
  it fails the pull request, but never runs it.
- **Gate**: every test calls `Assert.SkipUnless` on the environment variable
  `AGENTKIT_OLLAMA_LIVE_MODEL`, which also names the model every scenario uses. Unset, the fixture
  contacts nothing and every test reports itself skipped, naming the variable.
- **Server and window**: `AGENTKIT_OLLAMA_LIVE_HOST` names the server (default
  `http://localhost:11434`) and `AGENTKIT_OLLAMA_LIVE_NUM_CTX` the context window every request
  asks for (default 16384). Every request is sent with thinking disabled.
- **Log**: the optional environment variable `AGENTKIT_OLLAMA_LIVE_LOG_DIRECTORY` names the folder
  an `ollama-live-*.log` of the run is written to, recording every request's messages — role,
  length, image count and whether base64 image data was sent as text — and every answer.
- **Fixture**: a sandbox folder holds a `notes.txt` carrying the codeword `HERON-42`, a 400-line
  `big.log` whose `ERROR` lines exceed a 1,500-character result limit, a generated 316 KB PNG
  showing the number 7319, `composite.svg` — a titled SVG figure embedding that PNG — a generated
  one-page `report.pdf` that is blue on the left and yellow on the right, and `escape.svg`, a figure
  embedding `../outside/secret.png`; outside the sandbox, a secret and a PNG showing 5150, neither of
  which a confined tool may return. The tests run sequentially in one collection.
- **Model catalog**: the same project holds `OllamaModelCatalogLiveTests`, four tests prefixed
  `AgentKitAgentsOllamaLive_` that exercise AgentKitAgentsOllama's catalog and selection on the same
  fixture, server and variables. Their scenarios and recorded run are in *OllamaModelCatalog Unit
  Verification Design*.

### Running the Live Suite

From the repository root in PowerShell:

```pwsh
$env:AGENTKIT_OLLAMA_LIVE_MODEL = 'gemma4:e4b'
$env:AGENTKIT_OLLAMA_LIVE_HOST = 'http://localhost:11434'
dotnet test --project test/DemaConsulting.AgentKit.Agents.ChatClient.LiveTests -c Release
```

### Live Scenarios

Every test name in the table carries the prefix `AgentKitAgentsChatClientLive_`, omitted there. The
catalog tests in the same project carry `AgentKitAgentsOllamaLive_` and are listed in *OllamaModelCatalog
Unit Verification Design*.

| # | Test | What it asserts |
| --- | --- | --- |
| L1 | `ContextProvider_ToolsRetentionAndRestore` | Provider tools read, retain, deny relatively, page after reload |
| L2 | `AsGuarded_ForeignToolIsBounded` | A factory tool is refused, then retained and searched |
| L3 | `ImagePromotion_SmallToolMessageAndCorrectAnswer` | Small tool message, image on the user message, right number |
| L3c | `ImagePromotion_Control_WithoutPromotionToolMessageCarriesBase64` | Without promotion the image is base64 text |
| L4 | `ChatReducer_ReducesAndKeepsTheFact` | The reducer consolidates and the first turn's fact survives |
| L5 | `DiagramRender_HelpDrawSeeAndFix` | Help served, injected error shaped with its topic, fixed picture seen |
| L6 | `ImageView_FigureAndPdfDrawnOutsideReferenceRefused` | Figure and PDF read; outside picture refused |

- **L1** runs a tool-less `ChatClientAgent` whose tools come from `ToolPackContextProvider` over the
  text-file and file packs. It asserts the codeword is read; that `text_file_search` over `big.log`
  sends a retained preview; that a read of `../outside/secret.txt` sends a denial phrased relative
  to the working directory, naming no host path and carrying no secret; and that after
  `SerializeSessionAsync` and `DeserializeSessionAsync` a `tool_output_read` of `output-1` sends the
  retained line.
- **L2** builds `report_rows` with `AIFunctionFactory` and bounds it with `AsGuarded`. Without
  retention the tool message is a `ResourceTooLarge` refusal within the limit; wrapped by a
  `ToolOutputRetention` with its readers, it is a retained preview, and the model finds the square
  of row 137.
- **L3** builds the agent's client with `UseImagePromotion` and asks for the number `image_read`
  returns; **L3c** is its control without promotion.
- **L4** runs four turns through `InMemoryChatHistoryProvider` with an `AgentKitChatReducer` over a
  500-character threshold and one verbatim turn, counting the consolidations its summarizer makes.
- **L5** composes `DiagramPack.For(Vision)` over a read-write sandbox into an agent built with
  `UseImagePromotion`, places the pack's `Instructions` in the agent's instructions, and asks in one
  turn for `diagram_help` on `mermaid/flowchart` and `diagram_render` of a flowchart whose second
  link is written `->`. It asserts the help card was sent, a `mermaid-error` report naming the
  `mermaid/diagnostics` topic was sent, a user message carried a promoted image, and the answer names
  `Publish`, the node the fix made drawable.
- **L6** composes `image_view` over the read-only sandbox in an agent built with `UseImagePromotion`
  and takes three turns on one session: the number in `composite.svg`, the colors and sides of
  `report.pdf`, and what the tool returned for `escape.svg` — a `file-access-denied` report sent with
  no part of the run's root path, and an answer without the outside picture's number.

### Live Acceptance Criteria

A live run passes when every test that is not skipped passes. A run in which every test was skipped
is not evidence of anything. A failure of L1 means tools contributed per run, retention or its
restore, or relative denials do not hold against a real model; of L2, that a foreign tool's output
reaches the model unbounded; of L3, that a promoted image does not reach the model or the tool
message carries its bytes; of L3c, that OllamaSharp no longer sends the image as text, so promotion
should be reassessed; of L4, that the reducer does not reduce or loses the conversation's fact; of
L5, that the draw, read the failure, fix and look loop does not work against a real model; of L6,
that a drawn figure or page does not reach the model, or that a picture outside the sandbox does.

### Recorded Run

The suite was run manually on 2026-11-21, on Windows 11 (build 26100), against Ollama 0.34.2 with a
16,384-token window and thinking disabled. All five tests passed with `gemma4:e4b` and again with
`gemma4:26b`. The figures were the same for both models:

- **L1**: the search over `big.log` was retained as `output-1` with a 909-character preview; the
  denial named `../outside/secret.txt (relative to the working directory)`; the serialized session
  was about 7,300 characters, and after the reload `tool_output_read` returned
  `code=E07 order=ORD-00490`.
- **L2**: the unbounded report was 14,453 characters against a 1,500-character limit; the refusal
  was 155 characters and the retained preview 1,025; both models answered 18769.
- **L3**: for the 316,104-byte image the promoted tool message was 306 characters with no base64,
  the following user message carried one image, and both models answered 7319. In the control the
  tool message was 421,738 characters of base64 text, no message carried an image, and neither model
  saw one.
- **L4**: the reducer consolidated after the third and the fourth turn, the stored history held
  five messages, and both models answered with the vault code `4417-BRAVO`.
L5 and L6 were added on 2026-11-22 and run against the same server, window and settings; both passed
with `gemma4:26b` and again with `gemma4:e4b`. The figures are from the `gemma4:26b` run:

- **L5**: the `mermaid/flowchart` card was sent (about 2,900 characters); the first render was
  reported as `mermaid-error/mermaid-syntax-error` at line 3, column 10, naming
  `mermaid/diagnostics`; the corrected render was captioned `Rendered mermaid as PNG, 768x123
  pixels.` and promoted to the following user message; the answer listed Draft, Review and Publish.
- **L6**: the figure was captioned `Rendered svg as PNG, 800x480 pixels.` and answered 7319; the PDF
  page was captioned `Rendered pdf as PNG, 768x385 pixels.` and answered blue on the left, yellow on
  the right; `escape.svg` was reported as `file-access-denied/image-access-denied` naming the
  reference as written, `../outside/secret.png`, and the outside number never appeared.
