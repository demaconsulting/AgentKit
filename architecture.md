# AgentKit Architecture

## Purpose

AgentKit is a family of .NET libraries providing hardened, provider-neutral agent tools. It
exists so that a developer can hand an AI agent a set of capabilities that are safe **by
construction** — where the unsafe operation is not refused at call time, but is impossible to
express.

The driving scenario is safe tools placed in the hands of untrained users. A document
management agent that can read and write only within its working folder. An interactive
question-and-answer agent with read-only access to a database. These users are not qualified
to adjudicate a security prompt, and asking them to does not produce safety — it produces
approval fatigue and manufactured consent. The guardrail must therefore be structural, not
conversational.

AgentKit is consumed by .NET application developers building console, desktop, and
background-worker applications. It is developed internally by DEMA Consulting first, but
published publicly with a commitment to a stable API.

## Scope

### Included

- A small, stable contract package defining policy primitives, guarded tool construction,
  tool result helpers, and a tool-pack composition model.
- A general-purpose tool pack covering files, text, images, transfer buffers, work queues,
  user interaction, and sub-agent delegation.
- Capability-specific tool packs that adapt other DEMA Consulting libraries, or external
  capabilities, into guarded agent tools.
- Example applications demonstrating correct and safe usage.

### Explicitly excluded

AgentKit does **not** provide an agent runtime. Microsoft Agent Framework reached 1.0 GA in
April 2026 and ships the agent abstraction, the tool-calling loop, conversation state, context
compaction, tool approval, multi-agent orchestration, and a batteries-included harness.
Duplicating that would be waste. AgentKit therefore does not provide:

- An agent or session abstraction, or any tool-calling loop.
- Context-window management, compaction, summarization, or long-term memory.
- Provider or model abstraction, selection, or credential management.
- Multi-agent orchestration, workflows, or handoffs.
- Data residency and confidentiality routing. Where an application handles confidential
  material, it is the application operator's responsibility not to configure an off-premises
  provider without an appropriate data-protection policy. A labeling system inside AgentKit
  would not prevent that misconfiguration, and would invite false confidence that it had.

### Not a sandbox

AgentKit provides **guardrails, not a sandbox**. Its tools prevent a well-intentioned model
from straying outside its intended working area. They are not a defense against a determined
prompt-injection attack against a powerful tool set. For the target scenario — non-technical
users performing document and data work — guardrails are the correct and sufficient model.
Overstating this would be the single most dangerous thing the documentation could do.

## Package Structure

```text
AgentKit
├── DemaConsulting.AgentKit.Core    — policy primitives, guarded tool construction,
│                                     tool result helpers, tool-pack contract
├── DemaConsulting.AgentKit.Tools   — general pack: text file, file system, image,
│                                     transfer buffer, work queue, interaction, sub-agent
├── DemaConsulting.AgentKit.Speech  — agent-initiated speaking, listening, and voice
│                                     selection, adapting DemaConsulting.Speech
├── DemaConsulting.AgentKit.DocDown — document text and image extraction, adapting
│                                     DemaConsulting.DocDown
└── DemaConsulting.AgentKit.Repo    — future; version-control operations
```

Every package targets `net8.0`, `net9.0`, and `net10.0`.

**Core** depends only on `Microsoft.Extensions.AI.Abstractions`. It is deliberately small and
slow-moving, because every other package depends on it and inherits its churn.

**Tools** depends only on Core and the base class library. It is a peer of the capability
packs, not a layer beneath them; no capability pack depends on it.

**Capability packs** depend on Core plus whatever library supplies their capability. They never
depend on each other.

A new package is justified by a dependency that must be kept out of Core, not by a concept that
deserves a name. `AgentKit.DocDown` qualifies because it carries `DemaConsulting.DocDown`; `AgentKit.Speech`
because it carries `DemaConsulting.Speech` and its audio devices and models; `AgentKit.Repo` will
qualify when it carries a git implementation.

A capability is only a package when it is genuinely general. Database access is the instructive
counter-example: a useful database tool is inseparable from a particular schema — which tables
are exposed, which columns mean what, and how the model should be told to interpret them. A
schema-agnostic version would be either unsafe or useless. Such tools are built by the
application against Core's contract, using the same guarded construction, limits, and denial
conventions as any shipped pack. That an application can build a first-class guarded tool
without a pack existing for it is a measure of whether Core's contract is right.

## Inter-Package Interfaces

Core defines the entire contract between packages. A capability pack author writes tools against
these and nothing else.

**Path policy.** Read and write rules are expressed independently, each either unrestricted or
rooted, and each carrying its own deny rules. All containment decisions resolve symbolic links
and directory junctions at every path component. Both access and directory enumeration flow
through the same decision.

**Tool limits.** Ceilings on bytes read, result size returned to the model, and attachments per
turn. Limits are carried with the policy so every pack observes the same budget.

**Guarded tool construction.** A factory that wraps tool creation and applies the safety
conventions automatically — most importantly passing results through without JSON serialization,
so binary content survives to the provider. Pack authors cannot omit this by forgetting it,
because it is the only supported way to construct a tool.

**Tool results.** Constructors for text results, binary and image results, and denials. Denials
carry a reason, redact sensitive detail, and may redirect the model to a more appropriate tool.

**Tool pack contract.** Each package exposes its tools as a pack. An application composes packs
through a builder that gates registration on host capability, so a tool that cannot operate in a
given host is never offered to the model at all. Adding a capability pack costs a consumer one
line.

**Naming convention.** Tool names are owned by the family that defines them and carry a family
prefix. Core defines the convention; each pack applies it.

## Architectural Decisions

**The division of responsibility.** The application author decides which tools an agent receives,
and configures the controls those tools carry — which paths are readable, which are writable,
what limits apply. AgentKit's responsibility is that each tool, once chosen and configured, behaves
correctly and predictably within those controls. It does not decide which tools an application
should offer, nor override the author's configuration with judgments of its own.

What makes this safe rather than permissive is that the controls cannot be omitted. A tool cannot
be constructed without its policy, so an author may deliberately widen a boundary but cannot
accidentally forget one. AgentKit guarantees the mechanism; the author governs the settings. Every
decision below is an application of this principle.

**Build on Microsoft Agent Framework rather than beside it.** Agent Framework 1.0 GA already
provides the agent abstraction, tool loop, compaction, approval, and orchestration. The
originally scoped AgentKit would have duplicated roughly ninety percent of it. What remains
genuinely missing is the tool safety layer, and that is what AgentKit builds.

**Depend only on `Microsoft.Extensions.AI.Abstractions`.** Tools expressed as `AIFunction` work
against the GitHub Copilot SDK, Agent Framework, and a bare `IChatClient` alike. Taking a
dependency on `Microsoft.Agents.AI` would import 104 `[Experimental]` types and a monthly
minor-version cadence that has already renamed `AgentThread` to `AgentSession` and
`AgentRunResponse` to `AgentResponse` — an unacceptable foundation for a library committed to
API stability and to fingerprint-based review evidence.

**Core is the contract, not the tools.** Because every package depends on Core, anything placed
there is inherited by every consumer of every pack, and every change there churns the most
widely depended-upon review surface in the repository. Core therefore holds only what packs must
share. General-purpose tools live in a peer pack alongside the capability packs.

**Guardrails are mandatory at construction.** A tool cannot be obtained without supplying a
policy. An unguarded tool instance is unrepresentable, so safety cannot be forgotten, only
deliberately widened.

**Path containment resolves reparse points at every path component.** `Path.GetFullPath`
normalizes `..` but does not follow symbolic links or Windows directory junctions. A junction
placed inside an allowed root, pointing outside it, produces a path string that passes a naive
prefix check while the actual I/O lands elsewhere. Resolving only the leaf, or only the deepest
existing ancestor, is insufficient: a real file beneath a junction reports no link target of its
own. Containment must therefore walk each component from the volume root. This was proven
experimentally — a deliberately security-aware implementation failed this exact test on first
attempt, and the tool read a file outside its workspace.

**Directory enumeration is filtered through the same policy.** Recursive enumeration follows
junctions, so listing a directory can surface files outside the workspace even when reading them
is correctly denied. Enumeration and access share one containment decision.

**Read rules and write rules are expressed separately.** Real deployments differ: a
document-management agent confines both; a development assistant may read widely while writing
only within a project, excluding version-control metadata and protected files. A single root
model cannot express this.

**Denied operations return a readable result rather than throwing.** A thrown exception ends the
turn and strands the agent. A denial returned as an ordinary tool result lets the model
understand what was refused and try something legitimate. This matters most in unattended
operation, where no human is present to intervene.

**Binary tool results bypass JSON marshalling, and Core enforces it.** `AIFunctionFactory`
JSON-serializes function return values into a `JsonElement` by default. A returned `DataContent`
is therefore flattened to a `{"$type":"data","uri":"data:..."}` string before any provider can
recognize it as an image. Setting `AIFunctionFactoryOptions.MarshalResult` to pass the value
through without serialization allows the consuming runtime to convert it into a genuine image
attachment. This was verified end to end through Agent Framework with a Copilot back-end. The
failure mode is severe and silent: without it, the model reports that it can see the image and
then fabricates a description of it. Because this trap is invisible and its consequence is
confident fabrication, the correction belongs in Core's tool factory where no pack author can
omit it — including future capability packs that return extracted images.

**Tools are registered only when they can operate.** Where a host lacks a capability — no vision
support, no interactive user, no transfer buffer — the dependent tools are not registered at
all, rather than registered and refused on use. The model never sees a capability it cannot use,
so it cannot waste a turn on it or rationalize around the refusal.

**Tool names are owned by their families and carry a family prefix.** A tool's name is part of
its identity and belongs with its definition, not in a separate constants class. The prefix is
not cosmetic: Agent Framework's own `FileAccessProvider` registers bare names such as `read`,
`write`, and `delete`, so an application combining both would collide. `text_file_read` cannot.
Names are public API, because the model calls them and application prompts reference them.

**Deletion is recoverable by default.** Deletion is the only irreversible operation in the set,
and the target users are least equipped to notice it happened. The default moves content to a
quarantine location within the workspace rather than removing it.

**The transfer buffer is in-process and never the operating-system clipboard.** Reading the OS
clipboard would expose whatever the user most recently copied, which is routinely a credential.
The buffer exists to move content between files within a session, and the boundary is permanent.

**The boundary between a modality and a tool is who initiates.** Where the application decides —
every utterance transcribed, every response spoken — no tool is required, because recognition and
synthesis sit either side of the agent. Where the *agent* decides — speak now, listen now, capture
an image now, use a different voice — a decision is being made by the model, and that is
necessarily a tool. The second is strictly more capable: an agent that works silently and then
announces completion cannot be expressed as application-driven synthesis, which would narrate
every intermediate step. Both forms may coexist in one application.

**Registering a tool is the decision to grant it.** Where an application author adds speaking,
listening, or image capture to an agent, that is the authorization; the library does not
second-guess it with additional enablement gates. Capability-gated registration already means a
tool that should not be available is simply not offered. What these tools do carry are the same
ordinary robustness limits as any other: a bounded listen duration, so a turn cannot block
indefinitely in an unattended process, and a ceiling on listen-and-respond cycles, so a
conversational loop cannot run without limit. These are liveness and cost controls of the same
kind as result-size caps, not consent controls. Whether a user is visibly informed that a
microphone or camera is active is an interface decision, and belongs to the application that owns
the interface.

Giving an agent a voice by granting it shell access to invoke a command-line speech utility is the
failure mode this avoids. It works, but it confers general command execution to obtain one narrow
capability — the opposite of safety by construction. A dedicated tool also keeps recognition and
synthesis models resident, which a spawned process cannot.

**Example applications are written before the APIs they exercise.** They are a design instrument,
not documentation produced afterwards. If the sample is not pleasant to write, no amount of
internal structure will redeem the API. They also serve as the safety exemplar, since consumers
copy whatever the first sample does.

## Deliverables

### Example applications

Built in CI, not packed, and not requirement-traced.

| Sample | Packages | Demonstrates |
| --- | --- | --- |
| `01-document-assistant` | Core, Tools | Folder-confined read and write, no shell, unattended operation |
| `02-image-reviewer` | Core, Tools | Attaching images and PDFs for analysis, encoding the marshalling fix |
| `03-voice-assistant` | Core, Tools, Speech | Agent-initiated speaking and listening, spoken questions and approvals |
| `04-batch-worker` | Core, Tools | Unattended operation, auto-deciding approvals, work queue, exit codes |
| `05-custom-tool` | Core only | An application building its own guarded tool — read-only SQLite question and answer |
| `06-provider-swap` | Core, Tools | The same tools over both a Copilot and an `IChatClient` back-end |

Two samples carry architectural weight beyond illustration, and a third makes a claim no other
sample can.

`03-voice-assistant` drives an agent by speech using `DemaConsulting.Speech` for local offline
recognition and synthesis, with the agent choosing when to speak and when to listen rather than
the application narrating every response. It demonstrates that AgentKit's tools carry no
assumption about how the user is present — the same folder-confined tools serve a spoken
conversation as a typed one — and it exercises the listen-duration and cycle limits that
agent-initiated device tools carry.

`05-custom-tool` is the only sample that uses Core without any pack, and it is the test of whether
the pack contract is right. If an application can express a schema-bound, read-only database tool
naturally — with the same guarded construction, limits, and denial conventions as a shipped pack —
then Core is the correct shape. If it has to fight the contract, Core is wrong. It also keeps the
question-and-answer scenario from the Purpose section honest, by showing how it is actually built.

`06-provider-swap` is the executable proof of the provider-neutrality claim, and therefore of the
decision to depend only on `Microsoft.Extensions.AI.Abstractions`.

A capability-pack sample should follow once `DemaConsulting.DocDown` stabilizes, demonstrating
document extraction through `AgentKit.DocDown`.

## Implementation Sequencing

Build Core and the general pack first, and only then the first capability pack, so that the
contract is shaped by a real consumer rather than by anticipation.

Extraction from the existing DocPilot tool catalog should not be a single large migration. Those
tools are in production, and a careless lift would regress safety in a shipping product. Begin
with the image and text-file families, where both the failure modes and the corrections are
already proven, make DocPilot the first consumer, and let genuine migration friction shape the
contract before extracting the remaining families.

`AgentKit.DocDown` should wait until `DemaConsulting.DocDown` itself stabilizes, and serves as
the proof that the pack contract genuinely supports capability packs written against it.

## Open Concerns

1. 🟡 **MEDIUM** The repository is scaffolded for a single system, `AgentKitCore`. The remaining
   packages must be added as sibling systems, each with its own requirements, design,
   verification, SysML2 model, and review sets.
2. 🟡 **MEDIUM** Extraction risk. AgentKit must match DocPilot's current safety behavior exactly
   before replacing anything in that product.
3. 🟡 **MEDIUM** Deletion semantics. Whether recoverable quarantine is sufficient, and whether
   deletion ships in the first release at all.
4. 🟡 **MEDIUM** Overlap with Agent Framework's `FileAccessProvider` on the harness path.
   Family-prefixed names prevent collisions, but the duplication needs documenting so consumers
   understand which to choose.
5. 🟡 **MEDIUM** Sample review treatment. Including samples in the Purpose review while excluding
   them from requirements traceability requires a change to `.reviewmark.yaml`, a protected file.
6. 🟡 **MEDIUM** Interaction tools sit outside the containment safety model. Their risk is a batch
   run blocking forever rather than unauthorized access, and they should be documented as a
   different category of tool.
7. 🟡 **MEDIUM** Per-package compliance cost. Each package carries requirements, design,
   verification, and at least two review sets; the pack count should stay justified by dependency
   pressure alone.
8. 🟢 **LOW** Sample rot. Mitigated by CI builds. Six samples is at the upper limit of what is
   worth maintaining.
9. 🟢 **LOW** Samples 03 and 06 cannot run unattended in CI — the first needs audio devices and a
   downloaded speech model, the second a second back-end. Both degrade to compile-only checks
   unless additional infrastructure is provided.
10. 🟡 **MEDIUM** Limits for agent-initiated listening — maximum listen duration and the ceiling on
    listen-and-respond cycles — are not yet chosen. They are liveness and cost controls and belong
    with the other entries in tool limits.
11. 🟢 **LOW** A camera capture pack, returning a captured image through the same binary result
    path as file-based images. Deferred until a demonstrated need arises.
