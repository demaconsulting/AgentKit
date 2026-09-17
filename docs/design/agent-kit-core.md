# System Design

This document provides the system-level design for the AgentKit Core.

![AgentKit Core Structure](AgentKitCoreView.svg)

## Architecture

The AgentKit Core is the small, stable contract package on which every other AgentKit package
depends. It supplies the policy primitives that bound where a tool may act, the single guarded
path by which a tool is constructed, the result constructors a tool returns through, and the pack
contract by which a package publishes its tools to an application. It provides no agent, no
tool-calling loop and no provider abstraction; those belong to the agent framework an application
chooses. Core is deliberately slow-moving, because every other package inherits its churn.

The system consists of:

- **RealPathResolver Unit**: Reports the absolute, normalized location of a path, with relative
  segments collapsed
- **PathRule Unit**: One access grant — unrestricted or confined to a location — carrying an
  access level and its own denied patterns
- **PathPolicy Unit**: Holds the one working directory relative paths are anchored to and the
  zero-or-more access grants that permit locations, keeping addressing and permission orthogonal,
  and provides the single containment decision used by both direct access and directory
  enumeration
- **ToolLimits Unit**: Carries the ceilings a tool observes when reading, returning and
  attaching content
- **ToolResult Unit**: Constructs the results a guarded tool returns to the model, and defines
  the reasons a tool may refuse an operation
- **ToolName Unit**: Defines the tool naming convention and validates names against it
- **GuardedToolFactory Unit**: The only supported way to construct a tool, applying the
  result-delivery guard and the naming rules to every tool it creates
- **ToolPack Unit**: Defines the contract a package implements to publish its tools as one
  capability-gated family, and the set of host capabilities a pack may require
- **ToolPackBuilder Unit**: Composes tool packs into the tool list an application offers a model,
  registering a pack only when the host provides every capability the pack requires
- **ImagePromotingChatClient Unit**: Makes an image a tool returned visible to a provider whose
  tool-result channel cannot carry one, by promoting it onto a following user message
- **AgentSession Unit**: The session contract an application programs against, and what one turn
  reports back about the answer, the usage, the rotation, the compaction level, and any dropped
  material
- **AgentSessionOptions Unit**: What an application configures about one session, including the
  maximum number of most-recent turns kept verbatim
- **ContextUsage Unit**: The one usage shape every provider session answers with — how full the
  context is and out of how much
- **SessionTranscript Unit**: The append-only verbatim history kept out of session, grouped into
  whole turns so a tool call is never separated from its result
- **ContextLayout Unit**: The whole context as the engine accounts for it: the verbatim tail, the
  rings of consolidated slots, and the coarsest-first seed
- **RotationEngine Unit**: The deterministic aging function: consolidate older turns into a tier-one
  slot, cascade a full tier into the next, and report the consolidations and any material a failed
  consolidation left unrecorded
- **Summarizer Unit**: The injected out-of-session consolidation contract, the request that carries
  the consolidation instruction, and the documented default prompt
- **ProviderSession Unit**: The whole interface between the compaction engine and a provider
  adapter: the seed, the turn, the session that answers for its own window, and the factory
- **InMemoryProviderSession Unit**: A provider session that contacts nothing, so the engine can be
  exercised end to end without a live model
- **CompactingAgentSession Unit**: The implementation that sequences turns, usage reads, rotations
  and provider-session disposal

There are no subsystems. The three path-safety units form one collaboration:
`PathPolicy` makes a relative request absolute against its `WorkingDirectory`, resolves the
result through `RealPathResolver`, and then consults the applicable `PathRule` grants. Each
rooted grant resolved its confined location through `RealPathResolver` when it was created, and
each grant carries the access level that decides whether it can authorize a write as well as a
read. `RealPathResolver` depends on nothing within the system, so the collaboration is acyclic.

The four tool-contract units form a second collaboration. A pack author composes a tool name
through `ToolName` and hands it, with a delegate, to `GuardedToolFactory`, which validates the
name through `ToolName` and applies the result-delivery guard to every tool it creates. The
delegate returns a value built by `ToolResult` — text, content, or a refusal — and the guard is
what delivers that value to the runtime in the form the tool produced it. The two collaborations
meet at `PathPolicy`, which carries a `ToolLimits` so that every tool a host governs observes one
budget. `ToolLimits` and `ToolName` depend on nothing within the system, so this collaboration is
likewise acyclic.

The two pack-contract units form a third collaboration, which is where the first two meet an
application. A package author implements `IToolPack` to publish its tools as one family; an
application constructs a `ToolPackBuilder` with its `PathPolicy`, declares what its host supports,
and adds a pack per capability it wishes to attach. The builder registers a pack only when the
host provides every capability the pack declared, hands the registered packs the one policy it
holds, and verifies that each tool a pack returns carries the family prefix that pack claimed.
`IToolPack` depends only on `PathPolicy`, and `ToolPackBuilder` depends only on `IToolPack` and
`PathPolicy`, so this collaboration is acyclic too.

`ImagePromotingChatClient` stands apart from all three. It depends on nothing within the system —
only on the chat client abstraction the OTS dependency supplies — and nothing within the system
depends on it. It exists because delivering an image to a model is not finished when a tool
returns one: one provider carries an image out of a tool result to the model, while another
preserves the same content through the framework and discards it at the wire, after which the
model describes a picture it never received. A host that has chosen such a provider wraps its
client in this decorator, beneath the function-invocation loop, and the image is carried onto a
user message instead. It is opt-in rather than automatic because a host whose provider already
delivers images gains nothing from it, and because Core provides no agent loop of its own into
which it could be installed.

The remaining ten units form the fourth collaboration, the session engine. Core is the one library
guaranteed to be imported, and a kit of safe tools is worth nothing to an agent that cannot run long
enough to use them, so the engine that keeps a long-running conversation alive belongs here rather
than in a package a developer has to discover. It is provider-agnostic by construction: nothing in it
names a provider and no path in it performs network access. An application asks for a session, sends
messages to it, and reads answers back. When the conversation approaches the provider's context
limit, the session rotates into a fresh provider session seeded with preserved context. The
application receives the answer for every accepted turn and can observe whether rotation happened,
which compaction level is active, and whether any material had to be dropped.

The engine's units divide into four responsibility groups:

- **The contract an application programs against** — `AgentSession` provides `IAgentSession` and
  `AgentSessionResponse`; `AgentSessionOptions` carries instructions, tools, the summarizer and the
  one compaction setting, `VerbatimTurns`.
- **The accounting** — `ContextUsage` records how full a provider session is and out of how much;
  `SessionTranscript` holds whole turns; and `ContextLayout` holds the verbatim tail plus
  consolidated tiers.
- **The engine** — `RotationEngine` applies the round-robin aging rules, and `Summarizer` defines the
  injected out-of-session consolidation contract and prompt composition.
- **The provider seam** — `ProviderSession` defines seed, turn, session and factory contracts and the
  window question every adapter answers; `InMemoryProviderSession` is the deterministic in-memory
  provider used to exercise the lifecycle; and `CompactingAgentSession` sequences live turns,
  rotation and disposal.

A session runs as follows. `CompactingAgentSession` sends the message to the live provider session.
Only after the provider accepts the turn does the session record the user message, answer and all
intervening tool calls and results as one whole turn in `ContextLayout.Tail`. It then reads that
provider session's own account of how full it is. When conversation occupancy reaches 0.70 of the
window left once the provider's reported overhead is paid for, the session chooses a compaction level
from how quickly the window refilled, asks `RotationEngine` to age the layout at that level, builds a
`ProviderSessionSeed` from the result, creates and adopts a replacement provider session, and finally
disposes the superseded session.

The session engine needs none of the guarded-construction contract to do its work, and the
guarded-construction contract needs none of the session engine. The relationship is conceptual rather
than compiled: the tools an application puts into `AgentSessionOptions` are exactly the ones
`ToolPackBuilder` produces, expressed in the same `AIFunction` currency.

### Rotation, Not In-Place Reduction

When the context fills, the session consolidates older history, creates a fresh provider session
seeded with the preserved content, adopts that replacement, and only then disposes the session it
replaced.

This is the reduction mechanism both provider shapes support. One provider shape re-sends history on
every turn; another holds history server-side and has no general way to retract a prior turn. A
replacement seeded from preserved context works in both cases. Disposal is part of the mechanism
rather than housekeeping: for a provider that holds conversation state, disposal is what releases the
superseded context.

### Round-Robin Retention

Context is laid out most-stable-first:

```text
[system][tool declarations][tier 3 coarse] [tier 2] [tier 1] [verbatim tail]
```

The verbatim tail holds whole recent turns. Behind it are three tiers, each a ring of at most four
consolidated slots. Resolution decays with age: recent turns remain word for word, older material is
held in a tier-one slot, older still in progressively coarser slots. Nothing is weighed against a
per-tier token allowance; the shape is defined by counts.

`AgentSessionOptions.VerbatimTurns` is the one setting an application controls. The default is 20
turns, and it is a maximum rather than a quota. Internal constants define four slots per tier, three
tiers, the 0.70 rotation threshold, and the hysteresis windows `k = VerbatimTurns` and
`m = 2 * VerbatimTurns`.

The alternative — a single rolling summary — was measured and rejected. Over 50 rotations with a
compressed window (n = 50 rotations, one run per arrangement, recorded in the compaction spike that
preceded this engine), recall by rotations-ago was:

- **Flat rolling summary** (100% at 0-4, 50% at 5-9, 67% at 10-14, 0% beyond 15) — 5 of 23
- **Tiered** (50-100% held out to 50 rotations) — 13 of 17

The tiered arrangement also spent 17 percent fewer summarizer tokens (381,440 against 461,173)
because it consolidated only material that aged into a coarser tier. The cost is that remembered tier
content occupies context window space; the same measurement completed roughly 27 percent fewer turns
of work per rotation. That is the deliberate trade for retaining older detail.

### Aging Only at Rotation, in a Batch

Between rotations the context is append-only. Nothing already sent to a provider is rewritten while
a session is live, preserving provider prompt-cache prefixes. All reshaping happens during rotation,
where the provider session is being replaced anyway.

The rotation rules are:

1. Append each accepted exchange as one whole turn.
2. At 0.70 occupancy, consolidate everything older than the level-adjusted tail into one tier-one
   slot, always moving at least the oldest turn out of the tail.
3. When a coarse tier from tier one through the next-to-last tier is full and another slot arrives,
   consolidate that tier's slots as peers into one slot of the next tier, then clear the full tier.
4. The last tier is a ring: when it is full, appending a slot drops its oldest slot.
5. Answer pressure with counts. A window that fills again within `k` turns of a rotation escalates the
   compaction level; at the highest level it discards the oldest slot of the coarsest tier holding
   one, which is rule 4's ring brought forward, and reports that material was dropped. After `m`
   quiet turns the level relaxes.

Rule 5 terminates because it is arithmetic on counts: there are finitely many levels, the discard is
one slot, and both predicates are decided by how many turns have passed. Nothing in it measures a
context that has not been sent.

### Rotating at Seventy Percent of the Window the Provider Reports

Occupancy is measured after each provider turn, by asking the live provider session how full it is
and out of how much. The window left for conversation is that reported window minus the overhead the
same reading credits to the system prompt and tool declarations, and rotation fires when the reported
conversation reaches 0.70 of it. Overhead is not a small correction — the compaction spike estimated
a declaration block of 2,589 tokens for a set of 11 tools (n = 11 tools, one measurement, recorded in
that spike) — so removing it before applying the threshold keeps the trigger meaningful.

There is one source for these figures and therefore one currency. The adapter answers however its
provider allows: some publish current and limit counts, some publish a context length and count
occupancy against it, and an adapter for a provider that reveals nothing estimates and owns that
choice. Because the window, the overhead and the conversation all come back from the same reading,
nothing is ever subtracted from a figure somebody else counted.

The engine holds no token arithmetic of its own. Tokens serve one purpose — noticing that the window
is filling — and the only party that can count them honestly is the adapter, so the only party that
counts them is the adapter. Nothing sizes a seed before it is sent, because a context nobody has been
sent has not been counted by anyone whose count would mean anything.

A rotation always moves at least one turn. The tail keeps at most what the level asks for and at most
one turn fewer than it holds, whichever is smaller, so a provider whose tokenizer runs well ahead of
an ordinary conversation's growth — which reaches its threshold while the tail is still shorter than
its configured maximum — cannot produce a rotation that consolidates nothing and leaves the session on
a provider already past its window.

### The Summarizer Runs Out of Session

Consolidation is a separate stateless call that receives material as input. Asking the live provider
session to summarize itself would spend the session's own context and could invoke provider behavior
that the library is trying to avoid. The system therefore keeps its own transcript and hands rendered
material to an injected `ISummarizer`.

Each consolidation is one call, whatever the material. A rotation fires at a fraction of the
provider's own window, so the material ageing out is already bounded by that window; and where an
application pairs a small summarizer with a large provider anyway, the call fails or comes back
empty, which the engine already answers — the material stays verbatim, the window keeps filling, and
the session sheds its oldest slot. Splitting the material to forestall that was machinery guarding a
door that is already locked.

### Consolidation as Peer Reduction

A `ConsolidationRequest` carries three values: `Instruction`, `Material` and `TierIndex`. There is no
previous record, target size, or degradation flag. The pieces handed to a consolidation are peers:
either a span of older transcript entries or the slots of a full tier.

The prompt preserves specific named facts, decisions, paths, values, errors, resolutions,
constraints and outstanding work. It also permits collapsing repetition across the material, because
removing redundancy is how a consolidation buys room. The prompt does not ask a model to hit a token
count. In the compaction spike, consolidations asked for between 9,870 and 19,741 tokens returned
1,665 and 4,259 tokens (n = 2 requests, recorded in that spike), demonstrating that output size
cannot be dictated to a model.

### Compaction Level and Dropped Material

`CompactionLevel` is session state with `Low`, `Medium` and `High` values. It is reported on every
`AgentSessionResponse` and exposed on `IAgentSession`. Low keeps up to `VerbatimTurns` recent turns,
Medium keeps half, and High keeps a quarter, always leaving at least one turn. The level also selects
the plain-language terseness clause passed to the summarizer.

The level adapts by hysteresis, in turns. If a window fills again within `k` turns of a prior
rotation, the next rotation runs one level terser. If the session runs for `m` turns without filling,
with `m` greater than `k`, the level relaxes one step. The engine itself does not change the level it
is given: it rotates once at that level and reports what it did.

`AgentSessionResponse.MaterialDropped` reports history discarded rather than reduced. It is set when
the session was already at its tersest level and the window filled again, so the oldest consolidated
slot went in the bin, or when a consolidation came back blank and the material it was given could not
be recorded anywhere. The library reports that fact and leaves any policy decision to the
application.

### The Public Surface of the Session Engine

The public surface is a deliberate list, asserted against the built assembly rather than maintained
only in prose. `CompactionLevel`, the consolidation prompt and the in-memory provider session are
public because an application author writing a summarizer or exercising a session needs them; the
layout, the transcript and the rotation engine are internal, along with the slot and tier storage
used by the round-robin layout, because pinning the shape of the machinery in a consumer would make
any change to the arrangement a breaking one.

## External Interfaces

The system exposes the following public API to external consumers.

The path-safety API:

- **RealPathResolver.Resolve(string path)**: Returns the absolute, normalized location of
  `path`. Throws `ArgumentNullException` for a null path, `ArgumentException` for an empty or
  invalid path, and `IOException` when the platform cannot express the normalized result.
- **AccessLevel.ReadOnly**, **AccessLevel.ReadWrite**: Permission levels a grant can carry.
  `ReadWrite` implies read; there is no write-only level.
- **PathRule.Unrestricted(AccessLevel access, IEnumerable&lt;string&gt;? denyPatterns)**: Creates a
  grant with no location constraint and the supplied access level. Throws `ArgumentException` for
  a null or empty pattern.
- **PathRule.ReadOnly(string root, IEnumerable&lt;string&gt;? denyPatterns)**: Creates a read-only
  grant confined to `root`, resolved to its real location. Throws `ArgumentNullException` for a
  null location and `ArgumentException` for an empty or invalid location or pattern.
- **PathRule.ReadWrite(string root, IEnumerable&lt;string&gt;? denyPatterns)**: Creates a read-write
  grant confined to `root`, resolved to its real location. Throws `ArgumentNullException` for a
  null location and `ArgumentException` for an empty or invalid location or pattern.
- **PathRule.Access**, **PathRule.Root**, **PathRule.DenyPatterns**: Read-only properties
  exposing the grant's access level, real confined location (or none), and denied patterns.
- **PathRule.Allows(string realPath)**: Returns whether an already-resolved location is
  permitted.
- **PathPolicy(string workingDirectory, IEnumerable&lt;PathRule&gt; grants)**: Constructs a policy
  with the library's documented resource ceilings. The working directory is required, is resolved
  to its real location, and grants no permission by itself. Throws `ArgumentException` for a
  missing or empty working directory and `ArgumentNullException` for a null grant collection or
  entry.
- **PathPolicy(string workingDirectory, IEnumerable&lt;PathRule&gt; grants, ToolLimits limits)**:
  Constructs a policy with explicit resource ceilings. Grants may be empty, producing a valid
  policy that permits nothing. Throws `ArgumentException` for a missing or empty working
  directory and `ArgumentNullException` for a null grant collection, null grant entry, or null
  limits.
- **PathPolicy.WorkingDirectory**, **PathPolicy.Grants**: Read-only properties exposing the real
  anchor for relative paths and the permitted locations. The two are orthogonal; a working
  directory is only permitted when the application also grants it.
- **PathPolicy.Limits**: Read-only property exposing the ceilings every governed tool observes.
- **PathPolicy.WorkingDirectoryIsGranted**: Read-only property exposing whether the working
  directory is itself permitted for reading, which helps decide whether tool output may be
  emitted relative to it.
- **PathPolicy.TryResolveRead / TryResolveWrite(string? path, out string? realPath, out string?
  denialMessage)**: Returns whether the access is permitted, with the real location on success
  and a denial on refusal. A read consults any grant; a write consults only read-write grants. A
  relative path is interpreted against `WorkingDirectory`; an omitted, empty, whitespace or
  placeholder path denotes `WorkingDirectory` itself. A denial echoes the request, states the
  absolute interpretation only when a relative request was joined to the working directory, and
  enumerates the permitted locations with their access levels.
- **PathPolicy.EnumerateFiles(string? directory, string searchPattern)**: Returns the real
  locations of the permitted files beneath a directory, which is `WorkingDirectory` when no
  directory is named.
- **PathPolicy.DiscoveryRoots()**: Returns the distinct locations a discovery listing should
  enumerate, one per grant.
- **PathPolicy.EmitRelative(string resultRealPath, string? callerInput)**: Returns whether a tool
  should report a result relative to the working directory, mirroring the caller's dialect only
  when doing so names the correct location.
- **PathPolicy.IsDiscoveryRequest(string? directory)**: Returns whether a directory argument
  denotes the discovery case that establishes the relative-eligible dialect.

| Interface | Direction | Format | Constraints |
| --- | --- | --- | --- |
| `RealPathResolver.Resolve` | Inbound/Outbound | Method call / `string` return | `path` non-null, non-empty |
| `AccessLevel` | Outbound | Enum values | Read-only or read-write |
| `PathRule.Unrestricted` | Inbound/Outbound | Factory call / `PathRule` | Access defined, patterns valid |
| `PathRule.ReadOnly` | Inbound/Outbound | Factory call / `PathRule` | `root`, patterns non-empty |
| `PathRule.ReadWrite` | Inbound/Outbound | Factory call / `PathRule` | `root`, patterns non-empty |
| `PathRule.Access` | Outbound | `AccessLevel` property read | None; always succeeds |
| `PathRule.Root` | Outbound | `string?` property read | None; always succeeds |
| `PathRule.DenyPatterns` | Outbound | `IReadOnlyList<string>` read | None; always succeeds |
| `PathRule.Allows` | Inbound/Outbound | Method call / `bool` return | Resolved, non-null, non-empty |
| `new PathPolicy(...)` | Inbound | Constructor call | Anchor, grants, limits valid |
| `PathPolicy.WorkingDirectory` | Outbound | `string` property read | None; always succeeds |
| `PathPolicy.Grants` | Outbound | `IReadOnlyList<PathRule>` read | None; always succeeds |
| `PathPolicy.Limits` | Outbound | `ToolLimits` property read | None; always succeeds |
| `PathPolicy.WorkingDirectoryIsGranted` | Outbound | `bool` property read | None; always succeeds |
| `PathPolicy.TryResolveRead` | Inbound/Outbound | Method call / `bool` and `out` | None; any path is answered |
| `PathPolicy.TryResolveWrite` | Inbound/Outbound | Method call / `bool` and `out` | None; any path is answered |
| `PathPolicy.EnumerateFiles` | Inbound/Outbound | Method call / `IEnumerable` | Pattern non-null, non-empty |
| `PathPolicy.DiscoveryRoots` | Inbound/Outbound | Method call / list return | None; always succeeds |
| `PathPolicy.EmitRelative` | Inbound/Outbound | Method call / `bool` return | Result non-null |
| `PathPolicy.IsDiscoveryRequest` | Inbound/Outbound | Method call / `bool` return | None; always succeeds |

The system additionally exposes the tool-contract API:

- **new ToolLimits(int maxReadBytes, int maxResultCharacters, int maxBinaryBytes, int
  maxAgentDepth)**: Creates a set of resource ceilings. Every parameter is optional and
  defaults to the corresponding published constant. Throws `ArgumentOutOfRangeException` for a
  negative ceiling; a ceiling of zero is accepted and disables the operation.
- **ToolLimits.Default**: The shared set of ceilings a host receives when it configures nothing.
- **ToolLimits.MaxReadBytes**, **MaxResultCharacters**, **MaxBinaryBytes**,
  **MaxAgentDepth**: Read-only properties exposing the configured ceilings.
- **ToolResult.Text(string text)**: Returns the supplied text. Throws `ArgumentNullException`
  for a null text; an empty text is permitted.
- **ToolResult.Structured(object value)**: Returns the supplied value, which the guarded
  construction path serializes to JSON on the way to the runtime. Throws
  `ArgumentNullException` for a null value.
- **ToolResult.Binary(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)**:
  Returns content carrying its media type, preceded by the caption when one is supplied. Throws
  `ArgumentNullException` / `ArgumentException` for a missing or empty media type.
- **ToolResult.Image(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)**: As
  `Binary`, and additionally throws `ArgumentException` when the media type does not denote an
  image.
- **ToolResult.Denied(DenialReason reason, string message, string? redirectToolName)**: Returns
  the refusal text. Throws `ArgumentOutOfRangeException` for an undefined reason,
  `ArgumentNullException` / `ArgumentException` for a missing or empty message, and
  `ArgumentException` for an invalid redirect tool name.
- **ToolName.Create(string family, string verb)**: Composes and validates `{family}_{verb}`.
  Throws `ArgumentNullException` / `ArgumentException` for a missing or invalid part.
- **ToolName.Validate(string name)**: Validates a tool name against the naming convention.
  Throws `ArgumentNullException` for a null name and `ArgumentException` for any violation.
- **GuardedToolFactory.Create(Delegate method, string name, string description,
  JsonSerializerOptions? serializerOptions)**: Creates a tool whose result is delivered to the
  runtime unchanged. Throws `ArgumentNullException` for a missing delegate or description, and
  `ArgumentException` for an empty description or an invalid name.

| Interface                   | Direction        | Format                        | Constraints                       |
|-----------------------------|------------------|-------------------------------|-----------------------------------|
| `new ToolLimits(...)`       | Inbound          | Constructor call              | Every ceiling zero or greater     |
| `ToolLimits.Default`        | Outbound         | `ToolLimits` property read    | None; always succeeds             |
| `ToolLimits.MaxReadBytes`   | Outbound         | `int` property read           | None; always succeeds             |
| `ToolResult.Text`           | Inbound/Outbound | Method call / `object` return | `text` non-null                   |
| `ToolResult.Structured`     | Inbound/Outbound | Method call / `object` return | `value` non-null                  |
| `ToolResult.Binary`         | Inbound/Outbound | Method call / `object` return | `mediaType` non-null, non-empty   |
| `ToolResult.Image`          | Inbound/Outbound | Method call / `object` return | `mediaType` denotes an image      |
| `ToolResult.Denied`         | Inbound/Outbound | Method call / `object` return | Reason defined, message non-empty |
| `ToolName.Create`           | Inbound/Outbound | Method call / `string` return | Family and verb non-empty         |
| `ToolName.Validate`         | Inbound          | Method call                   | `name` satisfies the convention   |
| `GuardedToolFactory.Create` | Inbound/Outbound | Method call / `AIFunction`    | Delegate, name, description valid |

The system additionally exposes the pack composition API:

- **IToolPack.FamilyPrefix**: Read-only property exposing the family prefix every tool in the pack
  carries. Must be non-null and non-empty.
- **IToolPack.RequiredCapabilities**: Read-only property exposing the capabilities the host must
  provide for the pack's tools to operate. `HostCapabilities.None` means the pack is always
  registered.
- **IToolPack.CreateTools(PathPolicy policy)**: Creates the pack's tools, governed by the supplied
  policy. Must return a non-null collection containing no null element, every tool of which
  carries the declared family prefix. Not called at all when the host does not provide the
  required capabilities.
- **HostCapabilities**: Flags enumeration naming what a host can provide — `None`, `Vision` and
  `Delegation`.
- **new ToolPackBuilder(PathPolicy policy)**: Creates a composition governed by `policy`. Throws
  `ArgumentNullException` when the policy is null.
- **ToolPackBuilder.WithHostCapabilities(HostCapabilities capabilities)**: Declares what the host
  provides, replacing any earlier declaration, and returns the builder.
- **ToolPackBuilder.Add(IToolPack pack)**: Adds a pack and returns the builder. Throws
  `ArgumentNullException` for a missing pack, and `ArgumentException` for an empty family prefix or
  a prefix another added pack already claims.
- **ToolPackBuilder.Build()**: Returns the tools of every pack the host can support, in pack-add
  order. Throws `InvalidOperationException` when a pack returns no collection, a null tool, or a
  tool outside its declared family.

| Interface                              | Direction        | Format                        | Constraints           |
|----------------------------------------|------------------|-------------------------------|-----------------------|
| `IToolPack.FamilyPrefix`               | Outbound         | `string` property read        | Non-null, non-empty   |
| `IToolPack.RequiredCapabilities`       | Outbound         | `HostCapabilities` read       | None; always succeeds |
| `IToolPack.CreateTools`                | Inbound/Outbound | Method call / `IEnumerable`   | No null element       |
| `new ToolPackBuilder(...)`             | Inbound          | Constructor call              | `policy` non-null     |
| `ToolPackBuilder.WithHostCapabilities` | Inbound/Outbound | Method call / builder         | None; always succeeds |
| `ToolPackBuilder.Add`                  | Inbound/Outbound | Method call / builder         | Prefix unclaimed      |
| `ToolPackBuilder.Build`                | Inbound/Outbound | Method call / `IReadOnlyList` | Packs honor contracts |

The system additionally exposes one provider-compatibility API:

- **new ImagePromotingChatClient(IChatClient innerClient)**: Wraps a chat client so that an image
  a tool returned is promoted onto a user message before the request is forwarded. Throws
  `ArgumentNullException` when the inner client is null. Both the whole-response and the
  streaming member rewrite the conversation identically and are otherwise pass-through.

| Interface                           | Direction        | Format                 | Constraints       |
|-------------------------------------|------------------|------------------------|-------------------|
| `new ImagePromotingChatClient(...)` | Inbound          | Constructor call       | Client non-null   |
| `GetResponseAsync`                  | Inbound/Outbound | Method call / `Task`   | Messages non-null |
| `GetStreamingResponseAsync`         | Inbound/Outbound | Method call / sequence | Messages non-null |

The system additionally exposes the agent session API:

- **`IAgentSession`** — Direction: inbound, application to system; format: .NET interface;
  constraints: one conversation per session, sequential turns, caller disposes the session.
- **`AgentSessionResponse`** — Direction: outbound, system to application; format: immutable .NET
  class; constraints: includes answer text, usage, rotation flag, compaction level and
  dropped-material flag.
- **`AgentSessionOptions`** — Direction: inbound configuration; format: immutable .NET class;
  constraints: summarizer required, `VerbatimTurns` positive, no provider window configured here.
- **`ISummarizer`** — Direction: outbound, system to application implementation; format: .NET
  interface; constraints: stateless, out of session, safe for concurrent use, must not return null.
- **`IProviderSessionFactory` / `IProviderSession`** — Direction: outbound, system to adapter;
  format: .NET interfaces; constraints: factory is safe for concurrent use, session serves one
  conversation and is disposable, and every session answers `CurrentUsage` — how much of its
  provider's window it occupies and out of how much — without contacting the provider to do so.

| Interface                             | Direction | Format                         | Constraints                     |
|---------------------------------------|-----------|--------------------------------|---------------------------------|
| `IAgentSession.SendAsync`             | In/Out    | Method call / `Task`           | Message non-blank, not disposed |
| `AgentSessionResponse`                | Outbound  | Immutable class read           | Answer and usage non-null       |
| `new AgentSessionOptions(…)`          | Inbound   | Constructor call               | Summarizer non-null             |
| `ISummarizer.ConsolidateAsync`        | Outbound  | Method call / `Task<string>`   | Must not return null            |
| `IProviderSession.SendAsync`          | Outbound  | Method call / `ProviderTurn`   | Message non-null, not disposed  |
| `IProviderSession.CurrentUsage`       | Outbound  | Property read / `ContextUsage` | Answered without a round trip   |
| `IProviderSessionFactory.CreateAsync` | Outbound  | Method call / session          | Seed non-null                   |

Tools a session carries are `Microsoft.Extensions.AI` `AIFunction` instances, the same tool currency
the rest of AgentKit uses, so a session accepts exactly what a tool pack produces with no conversion
layer.

## Dependencies

The AgentKit Core takes exactly one runtime NuGet dependency,
`Microsoft.Extensions.AI.Abstractions`. It exists because a tool is an `AIFunction`, and
`AIFunction` is the common currency across the GitHub Copilot SDK, the Microsoft Agent Framework
and any `Microsoft.Extensions.AI` `IChatClient`. Depending on the abstractions package — rather
than on any provider, runtime or agent loop — is what lets one guarded tool be offered to all of
them without Core choosing a provider on the application's behalf. Nothing further is taken:
there is no provider package, no agent framework package and no transitive runtime. As a
third-party published library, `Microsoft.Extensions.AI.Abstractions` is an OTS item; its
integration is recorded in _OTS Integration Design_ (`docs/design/ots.md`) and its dedicated
_Microsoft.Extensions.AI.Abstractions Design_.

The following OTS items are used for building and verifying this system and are not consumed at
runtime; see _OTS Integration Design_ (`docs/design/ots.md`) and each item's dedicated design
document for details:

- **BuildMark** — generates build-notes documentation; see _BuildMark Design_
- **FileAssert** — validates generated documents against acceptance criteria; see
  _FileAssert Design_
- **Pandoc** — converts Markdown documentation to HTML; see _Pandoc Design_
- **ReqStream** — enforces requirements-to-test traceability; see _ReqStream Design_
- **ReviewMark** — enforces file review coverage and currency; see _ReviewMark Design_
- **SarifMark** — converts CodeQL SARIF results to markdown; see _SarifMark Design_
- **SonarMark** — generates SonarCloud quality reports; see _SonarMark Design_
- **VersionMark** — captures and publishes tool-version information; see _VersionMark Design_
- **WeasyPrint** — converts HTML documentation to PDF; see _WeasyPrint Design_
- **xUnit** — executes unit and integration tests; see _xUnit Design_

## Risk Control Measures

Path containment is a risk control measure (IEC 62304 §5.3.3). An agent acts on instructions
derived from model output, which may be influenced by content the operator did not author, so the
file locations an agent can reach must be bounded by the host rather than by the agent's own
restraint.

The measure is segregated into three units whose responsibilities do not overlap:

- **RealPathResolver** establishes _one spelling for a location_, making a path absolute and
  collapsing relative segments, so that containment is a comparison between two values expressed
  the same way. Links are not followed and not detected; containment rests on paths alone.
  Isolating this keeps the normalization separately reviewable and separately testable.
- **PathRule** establishes _what a location grants_, carrying an access level that is permission
  only: read-only or read-write. It has no addressing meaning and cannot silently change where a
  relative path resolves.
- **PathPolicy** makes _the single decision_, and both direct access and directory enumeration
  are routed through it so that a listing can never advertise a file that access would refuse. It
  also holds _the one location a relative request means_, so that a bare file name a model states
  is made absolute against the working directory before the normalization and the containment
  test are applied. That anchor grants no permission; the application grants it, or does not
  grant it, exactly as it grants any other location.

Three further properties are part of the control: a refused access is reported as a returned
denial rather than an exception, so a refusal cannot terminate an agent's turn; that denial
echoes the request, states how a relative request was interpreted, and enumerates the permitted
locations with access levels, so a refusal is a step the agent can recover from rather than a
dead end it retries; and a policy cannot be constructed without an explicit working directory and
grant collection, so an accidental process-global anchor is unrepresentable.

Two properties of the tool contract are risk control measures in their own right. **Selective
result marshalling** ensures a tool's output reaches the provider in the form the provider can
read: text, content and sequences of content are delivered as the tool produced them, because
when they are not, a returned image is flattened into JSON, the provider never recognizes an
attachment, and the model states that it can see an image and then fabricates a description of
it — a silent failure producing confidently wrong output. Anything else is serialized as the
underlying factory would have serialized it, because preserving every result indiscriminately was
tried and produced the opposite failure: a tool returning structured data reached the provider as
a raw object it could not read. **Family-prefixed naming** ensures an
application combining this library with the Agent Framework cannot present the model with two
identically named tools, a situation in which which tool is invoked is undefined. Both are
enforced at the single supported construction path, `GuardedToolFactory`, so a tool author cannot
omit either; see _GuardedToolFactory Unit Design_. Resource ceilings carried with the access
policy are a liveness control rather than a safety control, and are described in _ToolLimits Unit
Design_.

**Capability-gated registration** is a risk control measure of the same kind. Where a host cannot
support a tool, the tool is not offered at all rather than offered and refused: a model that can
see a tool it cannot use will spend a turn on it, and a model told only that a tool refused will
reason around the refusal rather than abandon the approach. Because an unsupported pack is never
asked to create its tools, accidental registration is unrepresentable rather than merely unlikely;
see _ToolPackBuilder Unit Design_. Family prefix ownership is part of the same measure: a pack
claims a prefix no other pack claims, and every tool it publishes must carry that prefix, so an
application cannot present the model with two tools it cannot tell apart.

Four further controls belong to the session engine. The first is **segregation between the engine and
any provider**. The engine never touches a provider API. It produces `ProviderSessionSeed`, consumes
`ProviderTurn`, and asks an adapter how full it is through `IProviderSession.CurrentUsage`. This
boundary makes the compaction behavior testable against `InMemoryProviderSession` with no network
access, credentials or model.

The second is **one-currency accounting**. The window, the overhead and the conversation come back
together from one reading taken at one place, and the engine performs no token arithmetic of its own,
so there is no second figure for a provider's figure to be combined with. `ContextUsage` carries
conversation usage beside total usage, so whoever produced the total also produced the split.

The third is **adaptive reporting**. Repeated pressure raises `CompactionLevel`; a discard at the
tersest level, or a consolidation that came back blank, sets `MaterialDropped`. The library keeps
answering when it can, but it does not hide the fact that fidelity has been reduced or material was
discarded.

The fourth is **provider-session ownership**. `CompactingAgentSession` pairs the live provider
session with its release state so disposal remains retryable. Creation and rotation guard the windows
where a provider session has been returned but not yet adopted: if adoption fails, the unowned session
is released, and the original failure is what the caller learns even when that release fails too.

## Data Flow

**Path containment path:**

1. **Input**: A working directory, zero or more access grants and resource ceilings at policy
   construction, then a requested path per access
2. **Interpretation**: An omitted, empty, whitespace or placeholder request is read as denoting
   the working directory itself. Every relative request is made absolute against the working
   directory first — never against the location the host process happens to be running from — and
   that interpretation wins whenever it names an existing path; a unique bare segment matching one
   grant's last path segment is read as that grant's location only as a fallback, when the
   working-directory interpretation does not resolve to an existing path
3. **Resolution**: The now-absolute path is normalized, so that `.` and `..` segments are
   collapsed. This happens after step 2 so that
   a relative escape and an absolute one reach the same decision
4. **Decision**: The real location is offered to every grant for a read, or only to read-write
   grants for a write. Each grant applies its denied patterns first and then its location
   constraint
5. **Output**: Either the real location the caller may act on, or a denial carrying the request,
   any working-directory interpretation, and the permitted locations with access levels
6. **Enumeration**: A directory listing routes the directory and every candidate file through the
   same read decision, so the listing and direct access always agree

**Guarded tool construction and result path:**

1. **Input**: A tool name composed from a family and a verb, a description, and a delegate
   implementing the tool
2. **Validation**: The name is checked against the naming convention — lowercase characters, a
   family prefix, no reserved collision — and the description is required to be non-empty
3. **Construction**: The tool is created through the single supported path, which supplies the
   result-delivery guard internally so it cannot be omitted or replaced
4. **Invocation**: The runtime calls the tool, which consults the access policy and its resource
   ceilings and produces either text, binary content, a caption accompanied by an image,
   structured data, or a refusal naming its reason
5. **Output**: The guard delivers text and content to the runtime unchanged, so content remains
   recognizable to the provider and a refusal remains readable text the model can act on, and
   serializes anything else to JSON so that structured data reaches the model in a form it can
   read

**Provider-independent image delivery path:**

1. **Input**: The conversation a function-invocation loop has assembled, with tool results
   already appended to it
2. **Inspection**: Each tool message is examined for a function result carrying image content,
   in either of the two shapes a guarded tool produces
3. **Promotion**: A user message holding a fixed announcement and the same content instances is
   inserted immediately after each tool result that carried an image; a text-only result adds
   nothing
4. **Output**: The rewritten conversation is forwarded to the wrapped client, so a provider that
   accepts images on messages but not in tool responses still receives the image

**Pack composition path:**

1. **Input**: An access policy at builder construction, a declaration of what the host supports,
   and one pack per capability the application wishes to attach
2. **Registration check**: Each pack is registered only when every capability it requires is one
   the host declared; a pack requiring none is registered by every host
3. **Creation**: Only a registered pack is asked for its tools, and it is handed the one policy
   the builder holds — an unsupported pack's tools are never built at all
4. **Agreement check**: Every tool a pack returns must carry the family prefix that pack declared,
   so the prefix no other pack may claim is a promise that is verified rather than trusted
5. **Output**: One ordered tool list — pack-add order, then the order each pack produced its tools
   — which the application hands to the agent runtime

**Agent session turn path:**

```text
application message
  -> IProviderSession.SendAsync
  -> on success, record the user message and ProviderTurn entries as one whole turn
  -> read ContextUsage from IProviderSession.CurrentUsage
  -> if conversation occupancy is below the threshold: return the answer
  -> otherwise:
       choose the CompactionLevel from how many turns since the last rotation, and at the
         tersest level discard the oldest slot of the coarsest tier holding one
       RotationEngine.RotateAsync(layout, summarizer, level, verbatim turns)
         -> split the tail by whole turns at the level-adjusted tail length, always
            moving at least the oldest turn
         -> consolidate older material into tier one
         -> cascade full tiers as peer slot batches
       IProviderSessionFactory.CreateAsync(ContextLayout.BuildSeed())
       adopt the replacement and dispose the replaced IProviderSession
  -> return the answer, usage, rotation flag, level and dropped-material flag
```

## Design Constraints

- **Minimal contract**: The smallest public surface that packs must share, changed slowly,
  because every other AgentKit package depends on it and inherits its churn
- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage, complete documentation
- **Portability**: Compatible across supported .NET platforms
- **Provider-neutral session engine**: No type in the session engine names a provider, and no engine
  path performs network access
- **Deterministic session engine**: Every decision in the engine is arithmetic over the layout and
  the current level. Injecting the summarizer isolates the single collaborator that may vary
- **Turn-granular boundaries**: A turn is one exchange — the user message, answer and all
  intervening tool calls and results. Boundaries are placed between turns only
- **Append-only between rotations**: Existing history is reshaped only when the provider session is
  being replaced
- **Fixed internal shape**: The layout has three tiers, four slots per tier and a 0.70 rotation
  threshold. Applications configure only the maximum verbatim tail
- **Nothing judges a context it has not sent**: Pressure is answered in counts of turns and slots; a
  token figure is read only to notice that the provider's window is filling, and the engine performs
  no token arithmetic of its own
- **Owned copies behind read-only views**: Public list properties expose immutable snapshots or
  read-only views over storage the object owns

### Platform Support

The library targets the following frameworks, enabling broad compatibility across modern .NET
runtimes:

| Target Framework   | Runtime / Environment                             |
|--------------------|---------------------------------------------------|
| `net8.0`           | .NET 8 LTS                                        |
| `net9.0`           | .NET 9                                            |
| `net10.0`          | .NET 10                                           |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation to Base Class Library (BCL) APIs
available across all target frameworks and to the provider-neutral
`Microsoft.Extensions.AI.Abstractions` surface. No platform-specific native interop, OS-specific
APIs, or framework-version-specific features are used.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns
- **Providers That Drop Images From Tool Results**: Providers differ in where they accept image
  content, and the difference is silent. One carries an image out of a tool result to the model;
  another accepts images on messages but not in tool responses, preserves the content through the
  framework, and discards it at the wire — after which the model describes a picture it never
  received and reports no error. A control exchange placing the same bytes on a user message was
  described correctly by the same model on the same provider, so the limitation is one of channel
  rather than capability. A host targeting such a provider wraps its chat client in
  `ImagePromotingChatClient`, **beneath** the function-invocation loop so that the decorator
  observes tool results after they have been appended; see _ImagePromotingChatClient Unit Design_.
  A host whose provider already delivers images from tool results installs nothing.
