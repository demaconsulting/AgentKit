## ModelSettingsChatClient Unit Verification Design

This document describes the unit-level verification strategy for the `ModelSettingsChatClient`
class.

### Verification Approach

`ModelSettingsChatClient` is verified through unit tests that wrap a hand-written
`OptionsRecordingChatClient`, which records the options each request arrived with and answers with
a fixed reply. What the provider would have been asked for is therefore observed directly, on the
options object the decorator forwarded, rather than inferred from a response.

Selections are obtained the only way a caller can obtain one — by binding settings to the shared
vision model from `ModelTestData` — so each scenario exercises a selection that validation would
really produce. That model offers an effort with no provider-neutral equivalent (`max`) and two
tiers, which is what lets the construction refusals be reached.

**Both request paths are exercised.** The streamed path is drained in full, so the request is made,
and asserted against the same options as the whole-response path; a host that streams would
otherwise lose the selection with nothing failing.

**What remains outside automated scope.** The decorator writes only provider-neutral options, so
how a particular provider client maps them to its wire format is not verified here. That a chosen
effort reaches Ollama as `think` was observed against a live server by the opt-in Ollama model
catalog live suite; see *OllamaModelCatalog Unit Verification Design*.

Unit tests reside in `ModelSettingsChatClientTests.cs`, with the shared descriptions in
`ModelTestData.cs`, within the `DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, file system access or network access required
- **Mocking**: A hand-written recording chat client; no mocking framework
- **Isolation**: Each test constructs its own inner client, selection and decorator; no state is
  shared

### Acceptance Criteria

A unit test run passes when all five scenarios below pass without error or exception beyond those
explicitly asserted. Any request, streamed or not, that does not carry the selected model, the
chosen neutral effort and the chosen output limit; any value the caller set that is overwritten;
any write to the caller's options object; any reasoning option sent when no effort was chosen; or
any selection carrying a context tier or an effort with no neutral equivalent that is accepted at
construction each constitute a failure.

### Test Scenarios

#### AgentKitCore-ModelSettingsChatClient-AppliesToEveryRequest: A Request Carries the Selection

**Test**: `ModelSettingsChatClient_GetResponseAsync_AppliesTheSelection`

Normal operation. A request made with no options reaches the inner client carrying the model
`vision-large`, the neutral `High` effort and an output limit of 1,000.

#### AgentKitCore-ModelSettingsChatClient-AppliesToEveryRequest: A Streamed Request Carries the Selection

**Test**: `ModelSettingsChatClient_GetStreamingResponseAsync_AppliesTheSelection`

The same through the streaming path: the drained request carries the model and the neutral `Low`
effort.

#### AgentKitCore-ModelSettingsChatClient-PreservesTheCallersRequest: The Caller's Values Win and Are Not Mutated

**Test**: `ModelSettingsChatClient_GetResponseAsync_CallerValuesWinAndAreNotMutated`

A caller that set its own model, output limit and effort sees those values reach the provider in
place of the selection's, and the options forwarded are a different object from the caller's.

#### AgentKitCore-ModelSettingsChatClient-PreservesTheCallersRequest: No Effort Chosen Sends No Reasoning

**Test**: `ModelSettingsChatClient_GetResponseAsync_NoEffortChosen_SendsNoReasoning`

Boundary condition. With no effort and no output limit chosen, the forwarded options carry neither,
so the model's own defaults stay in force, and the caller's options object still has no model set.

#### AgentKitCore-ModelSettingsChatClient-RefusesWhatItCannotApply: An Inapplicable Selection Is Refused

**Test**: `ModelSettingsChatClient_Construct_InapplicableSelection_Throws`

Error path, both branches. A selection naming a context tier and one naming the `max` effort are
each refused with `ArgumentException` at construction, with messages naming the missing context
tiers and the effort with no provider-neutral equivalent.
