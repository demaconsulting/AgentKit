## Memory

![AgentKit Tools Memory Structure](MemoryView.svg)

The Memory subsystem is the memory tool family: the pack an application attaches to give an agent a
searchable record of what it has learned, which it can file into, recall from, correct and forget.

### Overview

The subsystem's responsibility is to hold, for one agent or for one application, a set of memories —
each a short descriptor, a richer detail payload, optional provenance and the vector the descriptor
produced — and to publish five tools over that set: one that files a memory, one that finds the
closest memories to a question, one that corrects what a memory says, one that changes what a memory
is about and where it came from, and one that removes a memory.

The boundary is deliberately narrow. The family carries no path, consults no policy decision and
touches no file. It does not choose an embedding backend, does not choose where memories live and
does not choose the thresholds it applies; all three arrive from the composing application. What it
guarantees is the mechanism: that only descriptors are embedded, that every file is checked for a
near-duplicate before anything is stored, that a recall returns whole memories, and that no
correction silently keeps provenance it has reason to believe is stale.

The subsystem contains nine modeled units and two shared helpers:

| Unit               | Responsibility                                                         |
| ------------------ | ---------------------------------------------------------------------- |
| `MemoryOptions`    | The author's near-duplicate threshold and recall count                 |
| `MemoryRecord`     | One memory, and the memory-plus-similarity pair a search returns       |
| `MemoryStore`      | The substitutable persistence contract and its in-process default      |
| `MemoryFileTool`   | Publishes `memory_file`: stores a memory unless one already says it    |
| `MemoryRecallTool` | Publishes `memory_recall`: vector search returning matches whole       |
| `MemoryUpdateTool` | Publishes `memory_update`: replaces details, embedding nothing         |
| `MemoryReviseTool` | Publishes `memory_revise`: replaces all three parts, re-embedding      |
| `MemoryForgetTool` | Publishes `memory_forget`: removes one named memory                    |
| `MemoryPack`       | Publishes the five tools as one family under the `memory` prefix       |

`MemoryEmbedding` and `MemoryDenials` are internal shared helpers rather than modeled units, in the
same way `TextLines` and `TextFileBinaryGuard` are for the TextFile subsystem; both are described
under _Design_ below and are reviewed with this subsystem.

### Interfaces

The subsystem exposes `MemoryPack`, `MemoryOptions`, `MemoryRecord`, `MemoryMatch`, `IMemoryStore`
and `InMemoryMemoryStore` as public types, plus the name constant each tool unit publishes and
`MemoryPack.SuggestedInstruction`. Each tool's factory is `internal`, so a tool cannot be obtained
except through the pack that claims the family prefix.

| Interface                         | Direction | Format                     | Constraints                          |
| --------------------------------- | --------- | -------------------------- | ------------------------------------ |
| `MemoryPack`                      | Outbound  | AgentKitCore `IToolPack`   | Prefix `memory`; no capability       |
| `Memory*Tool.ToolName`            | Outbound  | `string` constant          | The name each tool is published as   |
| `MemoryPack.SuggestedInstruction` | Outbound  | `string` constant          | Instruction text for the agent       |
| `MemoryOptions`                   | Inbound   | Immutable control object   | Threshold 0.0-1.0; recall count >= 0 |
| `IMemoryStore`                    | Inbound   | Persistence contract       | Cosine similarity, nearest first     |
| `IEmbeddingGenerator`             | Inbound   | Microsoft.Extensions.AI    | Backend never inspected              |
| `PathPolicy`                      | Inbound   | AgentKitCore policy object | Accepted at construction and ignored |

The subsystem consumes `PathPolicy`, `ToolResult`, `GuardedToolFactory`, `IToolPack` and
`HostCapabilities` from AgentKitCore, and `AIFunction`, `IEmbeddingGenerator` and `Embedding<float>`
from `Microsoft.Extensions.AI.Abstractions`. It never reads or writes a file.

### Design

**The evidence this shape rests on.** Five configurations were built and measured against a
seventeen-document technical corpus with a fixed question set, and all five plateaued at the same
answer accuracy. The ceiling belonged to the corpus and the questions, not to any configuration, so
the design that ships is the simplest one that reaches it. Three findings survived that exercise and
are load-bearing here.

_Coarser granularity is monotonically better._ Fine-grained extraction fragmented a single answer
across several memories, so a top-k recall returned incoherent partials. A memory is therefore
roughly one document or one section. Nothing in the data model encourages finer splitting, and the
`memory_file` description tells the model so directly, because granularity is the one choice the
library cannot make for it.

_There is no graph._ Two configurations built links between memories and produced 530 of them. Not
one ever contributed to a correct answer, and a deterministic probe confirmed the links were
correctly wired — so the graph genuinely added nothing rather than having been built wrongly. There
is no link, edge or traversal in this subsystem and none is planned.

_Descriptor and payload are different jobs._ A sentence short enough to embed well cannot answer a
question, and a passage long enough to answer one does not embed well. Only the descriptor is
embedded; the details are returned whole on recall and are never searched. This is what lets a
recall both find the right memory and be sufficient to answer from.

**Near-duplicate detection is arithmetic, not judgement.** Every attempt during the spike to have
the model notice a contradiction by reading failed. The arithmetic succeeded: two statements of one
fact — 12 psi against 18 psi — scored 0.965 cosine, comfortably above a 0.88 threshold, while
unrelated statements from the same corpus sat well below it. `memory_file` therefore embeds the new
descriptor, asks the store for the single nearest memory, and declines to store anything whose
nearest match reaches the author's threshold. The comparison is nearly free: the vectors it compares
were computed when their memories were filed.

**Two defects from the spike are fixed here, and both fixes are structural.**

_Stale provenance._ The spike's revision silently preserved the original `sourceDocument` and
`sourceLocator`. A fact revised from a different document therefore kept citing the superseded one —
observed with details drawn from `02-revision.md` sitting beside a structured field still reading
`01-initial.md`. The tool knew the memory had changed, kept data it had every reason to believe was
stale, and gave the model no way to correct it. `memory_revise` now takes provenance as parameters,
sets the memory's provenance to exactly what the caller states, clears it when the caller states
nothing, and reports the provenance the memory then carries. No policy about whether sources
replace, accumulate or require corroboration is baked in; that is the application author's
instruction to give. The tool's only job is not to silently keep wrong data.

_Refusal prose invited confabulation._ After a near-duplicate refusal delivered as a prose sentence,
the model asserted that it had stored the fact. `memory_file` now returns structured data carrying
`stored: false`, the conflicting memory's identifier, descriptor and details, the similarity and the
threshold. A field named `stored` holding `false` is not open to the reading a sentence was.

**Denials state facts and never prescribe remedies.** This is a project-wide rule learned
expensively: a denial that helpfully named a replacement tool was once followed by a model
destroying a file. `MemoryDenials` holds the two refusals more than one tool composes — an
identifier the store does not hold, and a call naming no memory — so that three tools cannot drift
into three wordings for one mistake. The not-found refusal states the identifier and how many
memories are held, and stops there; unlike the todo family, it does not list the identifiers that do
exist, because a memory store may hold thousands of machine-assigned ones. A subsystem test asserts
that no refusal or non-storage result in the family contains the name of any tool in the family.

**Tool output is a demonstration.** Whatever shape a tool emits is the shape the model mirrors back.
A recall therefore returns a list of fielded matches rather than a paragraph, because fielded
matches teach attribution and a paragraph teaches blurring. An update and a revision report the
provenance the memory now carries, with an explicit `sourceStated` flag, because JSON serialization
drops a null field and an absent field reads identically to one the reader did not notice.

**Update and revise are two tools because re-embedding is the expensive half.** Most corrections are
to a memory that is still about the right thing, and `memory_update` handles those without touching
the embedding backend. The separation is also a safety property: changing a descriptor without
recomputing its vector would leave a memory findable only under its old meaning — found by the wrong
questions, invisible to the right ones, with nothing in the store to show it had happened. A
revision is deliberately not near-duplicate checked, because it names one existing memory and
changes it, and checking would refuse a memory against itself.

**Shared helpers.** `MemoryEmbedding` is the one place a descriptor becomes a vector. It hands the
text to the generator exactly as the model wrote it — no task prefix, no instruction wrapper, no
normalization. Several embedding models publish prefixes callers are invited to prepend; the absence
of one is what reproduced a reference implementation's output at 1.00000 cosine, and adding one
would in any case be this library deciding something about the author's chosen backend. A generator
that produces no vector raises rather than returning a refusal: a refusal is for a request the model
could have made differently, and an unreachable backend is a host fault the model can do nothing
about. `MemoryDenials` is described above.

**Where memories live is the author's decision.** `IMemoryStore` is public so that an author who
wants memories in a database, a vector service or a file writes one implementation and changes
nothing else. `InMemoryMemoryStore` is a working default — a list, a lock and an exhaustive cosine
scan — at the scale the default is for. It refuses a vector whose length differs from the ones it
holds, because mixing two embedding models in one store produces similarity numbers that look
ordinary and mean nothing; that can only happen when an author changes generator against a persisted
store, so it is surfaced as the configuration error it is.

**One store per composition, unless the author supplied one.** When no store is supplied, `MemoryPack`
allocates a fresh `InMemoryMemoryStore` inside `CreateTools` — as a local, never a field — so two
compositions never share memories and a delegated agent keeps its own. This is the same containment
the Todo subsystem gives a task list. An application that supplies a store gets that store every
time, because sharing or persisting memories is the only reason to supply one.

**No host capability is required.** The capability the family really depends on is an embedding
backend, and that arrives as a constructor argument: an application that cannot embed cannot
construct the pack at all, so there is nothing left for a capability flag to gate.

**Attaching the tools is not enough; the application must instruct the agent to use them.** A model
reading a document answers from the document it is holding and never thinks to write anything down,
because from inside one turn there is no observable difference between knowing something and having
just read it. The same effect was measured for the Todo subsystem, where a soft instruction produced
use in 1 of 5 runs against 3 of 3 for an explicit one; the Todo design document carries those
numbers. `MemoryPack.SuggestedInstruction` publishes the wording so an application can append it
rather than transcribe it, and so the text that ships cannot drift. The library deliberately does not
inject it: silently editing an agent's system prompt is exactly the kind of invisible behavior an
application author cannot audit.

**What was considered and not built.** A `memory_merge` tool that combined two memories into one was
considered and left out: it is unproven, it needs a policy about which provenance survives — exactly
the decision this subsystem refuses to make on the author's behalf — and the pair of operations it
would replace, revise-then-forget, already exists.
