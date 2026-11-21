## ToolPackContextProvider

![AgentKit ChatClient Agents Structure](AgentKitAgentsChatClientView.svg)

The public `ToolPackContextProvider` class is a Microsoft Agent Framework `AIContextProvider` that
contributes AgentKit tools and optional instructions to every run of a `ChatClientAgent`, composed
once per conversation, with each conversation's large-output retention persisted in its session. Its
options type, `ToolPackContextProviderOptions`, is documented here as a supporting value type.

### Purpose

A host that builds its own `ChatClientAgent` can pass it a tool list once, but AgentKit's tools carry
state that belongs to one conversation — a retention store, a task list, a memory store — and must be
neither lost between turns nor shared between conversations. The agent framework's context-provider
hook contributes tools to each run; this unit composes them once per `AgentSession` and contributes
that composition on every run of the session.

Retention must also survive the framework persisting a conversation. The framework serializes an
`AgentSession`, including its `StateBag`, so this unit writes each conversation's
`ToolOutputRetention` snapshot there after every run and restores it when it first meets a reloaded
session. Without it, the live spike observed every reference answered "never retained" after
`SerializeSessionAsync`/`DeserializeSessionAsync`.

The hook is honored only by `ChatClientAgent`: the framework merges contributed tools into the request
only for a chat-client agent, and AgentKit's Copilot agent fixes a session's tools when the runtime
session is created. The provider therefore refuses to run for any other agent rather than be silently
ignored.

### Data Model

`ToolPackContextProviderOptions` (immutable, `init` properties):

| Member | Type | Description |
| --- | --- | --- |
| `Instructions` | `string?` | Instructions contributed with the tools; null for none. |
| `HostCapabilities` | `HostCapabilities` | Declared when composing packs; default `None`. |
| `ToolOutput` | `ToolOutputOptions?` | Retention options; null composes no retention. |
| `PersistRetainedOutputs` | `bool` | Persist retained text (default) or only the counter. |
| `StateKey` | `string` | State bag key; default `AgentKit.ToolPackContextProvider`. |

`ToolPackContextProvider`:

| Member | Type | Description |
| --- | --- | --- |
| `_compose` | `Func<IReadOnlyList<AIFunction>>` | Composes one conversation's tools before retention. |
| `_limits` | `ToolLimits` | The ceilings retention, and the delegate constructor's bounding, use. |
| `_options` | `ToolPackContextProviderOptions` | The validated options. |
| `_sessions` | `ConditionalWeakTable<AgentSession, SessionTools>` | Each live session's tools and retention. |

`SessionTools` is a private record of the contributed `IList<AITool>` and the conversation's
`ToolOutputRetention`, or null. The weak table holds an entry only as long as its session lives.

### Key Methods

#### ToolPackContextProvider(PathPolicy policy, IEnumerable&lt;IToolPack&gt; packs, ToolPackContextProviderOptions? options)

Validates the policy, the packs (non-empty, no null) and the options, then stores a composition that
builds a fresh `ToolPackBuilder` over the policy, declares the host capabilities, adds every pack and
builds — so each pack's `CreateTools` runs per conversation and creates that conversation's stores. A
trial composition runs at construction, so a prefix collision (`ArgumentException` from the builder)
or a composition the capabilities leave empty is reported here. The limits are the policy's.

#### ToolPackContextProvider(Func&lt;IReadOnlyList&lt;AIFunction&gt;&gt; composeTools, ToolLimits limits, ToolPackContextProviderOptions? options)

Validates the arguments and stores a composition that calls `composeTools` and bounds each function
with `GuardedToolFactory.AsGuarded(limits)`, so a foreign tool is held to the same ceilings.

#### ValidateOptions(ToolPackContextProviderOptions? options, ToolLimits limits)

Returns the defaults for null; throws `ArgumentException` for a blank state key, or for retention
requested over a result ceiling below `ToolOutputSpill.MinimumResultCharacters`.

#### ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken)

1. Throw `InvalidOperationException` when `context.Agent.GetService(typeof(ChatClientAgent))` is null —
   the agent neither is nor wraps a chat-client agent, so it would ignore the tools. The message names
   `CopilotAgentFactory` and `ToolOutputRetention` as the alternatives.
2. Look up the session's composition, creating it with `Compose` on first sight; a run with no session
   gets a throwaway composition that nothing persists.
3. Return an `AIContext` with the composition's tools and the configured instructions; the framework
   merges them with the agent's own.

#### Compose(AgentSession? session)

Calls the stored composition (an empty result is an `InvalidOperationException`). Without retention
the tools are returned as they are. With retention, it restores a `ToolOutputRetention` from the
session's state bag (`TryGetValue<ToolOutputRetentionSnapshot>` with the snapshot's source-generated
serializer options) when a snapshot is present, otherwise creates a new one, and returns
`retention.Wrap(tools)` followed by `retention.ReaderTools`. A malformed persisted snapshot throws the
`ArgumentException` `ToolOutputRetention.Restore` raises rather than being silently discarded.

#### InvokedCoreAsync(InvokedContext context, CancellationToken cancellationToken)

After every run, successful or failed — overriding the base, which skips failed runs, because a run
that failed after a tool retained output has still issued references — exports the session's
retention and writes it to the state bag under the state key. With `PersistRetainedOutputs` false, the
snapshot written carries the counter and no outputs.

#### StateKeys

Returns the configured state key, so the framework can detect two providers writing to one key.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null policy, packs, function list or limits | `ArgumentNullException` |
| Empty pack list, null pack, empty or colliding composition | `ArgumentException` at construction |
| Blank state key; retention over a too-small ceiling | `ArgumentException` at construction |
| Agent is not a chat-client agent | `InvalidOperationException` from the run |
| Delegate composes no tool | `InvalidOperationException` from the run |
| Malformed persisted snapshot | `ArgumentException` from the run |
| Refusals, oversized outputs, unknown references | Returned to the model as tool results |

### Design Constraints

**What is persisted, and what is not.** Retained outputs (or only the counter) are persisted. Task
lists (`TodoPack` creates a store per composition) and in-memory memory stores are not: a reloaded
conversation starts them empty. Retention composed through `ToolPackBuilder.WithToolOutputSpill` or
`ToolOutputSpill.Apply` has no handle and cannot be exported; this unit uses `ToolOutputRetention`
directly for that reason.

**Size and privacy.** Persisted outputs can reach `ToolOutputOptions.MaxRetainedCharacters` and have
the privacy class of the chat history, which already holds tool results. `PersistRetainedOutputs`
opts out of the text while keeping references honest.

**Timing.** State is written after a run, so a session serialized while a run is in flight carries the
state of the previous run.

The provider is safe for concurrent use: the weak table is thread-safe, each retention is thread-safe,
and every other member is immutable.

### Dependencies

- **AgentKitCore** — `ToolPackBuilder`, `IToolPack`, `PathPolicy`, `HostCapabilities`,
  `GuardedToolFactory.AsGuarded`, `ToolLimits`, `ToolOutputOptions`, `ToolOutputRetention`,
  `ToolOutputRetentionSnapshot` and `ToolOutputSpill.MinimumResultCharacters`; see *ToolOutputRetention
  Unit Design* and *ToolPackBuilder Unit Design*.
- **Microsoft.Agents.AI.Abstractions** — `AIContextProvider`, `AIContext`, `AgentSession` and its
  `AgentSessionStateBag`; see *Microsoft.Agents.AI.Abstractions Design*.
- **Microsoft.Agents.AI** — `ChatClientAgent`, the only agent the provider runs for.
- **Microsoft.Extensions.AI.Abstractions** — `AIFunction` and `AITool`.

### Callers

A public API entry point: a host passes an instance in `ChatClientAgentOptions.AIContextProviders`.
The Agent Framework calls `ProvideAIContextAsync` at the start of each run and `InvokedCoreAsync` at
its end. Nothing within this system calls it.
