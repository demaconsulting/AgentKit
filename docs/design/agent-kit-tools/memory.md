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
guarantees is the mechanism: that only descriptors are embedded, that a filing call checks for a
near-duplicate against what the store holds when it runs before storing anything, that a recall
returns whole memories, and that no
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

That spike finding is narrower than it sounds, and later live measurement bounds it. The spike had
no instruction telling a model to look for conflicts; an instructed live model does notice them, in
5 of 5 runs measured below. What survives is the mechanism's _role_ rather than its primacy: the
arithmetic is what catches a conflict the model did not notice, which is by definition the case
judgement cannot cover.

**Near-duplicate detection is a backstop, not the primary conflict mechanism, and a reader must not
leave this document believing it is routine or reliable.** The mechanism is guaranteed within the
call that performs it — a file call compares against the nearest memory the store holds when it runs
— but what that check can see is one sentence the model
chose the wording of, and the wording decides the outcome. What a live model does with that latitude
is the measurement that matters, and it is not what the spike predicted.

_What a live model actually does with a conflict._ Eight live runs of the `research-assistant`
sample were measured after the subject-only descriptor instruction was added — model pinned
`claude-sonnet-5`, `--embeddings local` — in two arms. In the **neutral arm (n = 5)**, whose prompts
never mention descriptors at all, the descriptor rule was obeyed **5 of 5**: values and revisions
went into `details` and the provenance parameters rather than into the embedded field. The
near-duplicate refusal fired **0 of 5**. But the failure it exists to catch — two contradictory
memories stored silently — also occurred **0 of 5**. The model recognized the contradiction by
reading, cited `02-field-revision.md`, and used `memory_revise` to correct the memory in place
**5 of 5**; each run ended holding exactly one relief-valve memory, at 18 psi. The regression checks
were clean across the same five runs: `memory_revise` chosen over `memory_update` 5 of 5, the new
source cited 5 of 5, and the fresh-session recall turn answering 18 psi through `memory_recall`
5 of 5.

_So the refusal covers the unnoticed conflict, which is the case worth covering._ The refusal fires
when the model files a near-identical descriptor — which is to say, when it has _not_ noticed that
it is contradicting something the store already holds. That is precisely when a backstop is wanted,
and precisely when no amount of judgement is going to produce a revision. When the model does
notice, it revises, and a revision is the better outcome of the two: one memory, corrected, citing
the document that corrected it, instead of a refusal the model then has to act on. Revision in place
is therefore the primary path in practice and the refusal is the floor under it. Stated plainly: the
refusal did not fire in any of the 8 live sample runs. It did fire 5 of 5 in an earlier directly
driven measurement in which the descriptors genuinely collided, and it fires in the unit tests, so
the arithmetic is not in doubt — only how often a live model reaches it.

_Descriptor length moves the same conflict across the threshold._ Independently of phrasing, a
lexical bag-of-words generator scores two descriptors differing in one token out of `n` at exactly
`(n - 1) / n`, so one conflict measures 0.875 stated in eight tokens and 0.917 stated in twelve —
below and then above the 0.88 default, on verbosity alone.

_The comparison covers one filing call, not two in flight together._ `memory_file` reads the store
and then writes to it: it embeds the descriptor, asks for the nearest match, and adds the memory if
nothing crossed the threshold. Those are two separate operations against the store, so two filing
calls running at the same time can both complete their search before either adds, and both then
store. This is reachable rather than theoretical — `IMemoryStore` states that an agent may have more
than one tool call in flight, and a live sample run returned eleven `memory_file` results with
interleaved store counts, which only happens when the calls genuinely overlap.

_Why that window is stated rather than closed._ It is not a store defect and cannot be fixed in one:
`IMemoryStore` already requires implementations to be safe for concurrent use, and
`InMemoryMemoryStore` takes its lock on every operation, yet the window survives because it lies
between two operations rather than inside either. Closing it would mean either an atomic
check-and-add on the store contract — which every author substituting persistence would then have to
implement — or a lock inside the family serializing calls against a store the author owns. Both are
this library deciding something about the author's persistence, which is the opposite of the
principle that AgentKit guarantees the mechanism and the author governs the settings. So the scope
is documented instead, and it belongs with the other reasons this check is best-effort: only the
descriptor is embedded, and the model chooses its wording. It does not change the value of the
mechanism, because the refusal is a backstop that fired in 0 of the 8 live sample runs above while
revision in place handled the conflict in 5 of 5 neutral-arm runs (n = 5), and because — as the
adversarial arm shows below — two memories carrying accurate provenance can be a correct
representation rather than a failure.

_What an adversarial prompt produces, and why it is not a defect._ In the **adversarial arm
(n = 3)**, whose second prompt demands "a separate new memory… do not revise or update", the
descriptor rule was obeyed **0 of 3** and the model narrated exactly why: _"I used a distinct
descriptor ('…per field revision') so it wouldn't collide with the Revision A memory."_ Nothing was
refused (0 of 3), both memories were stored, and the store was left holding 12 psi and 18 psi at
once. This arm is an adversarial lower bound rather than everyday behavior — being told to keep two
memories is itself pressure toward two distinct labels — and its outcome is arguably the correct
one. "12 psi per Revision A" and "18 psi per Revision B" are both true statements, about different
documents; two memories carrying accurate provenance is a legitimate representation of a superseded
specification, and it is the representation the user explicitly asked for. This is the application
author governing the settings, which is the intended design: the family guarantees that a filing call
checks what the store holds when it runs, and leaves what _should_ happen to a conflict over the
author's own corpus to the author,
their instructions and their threshold. An earlier commit message in this repository framed this arm
as a failure mode. That framing was wrong, and is corrected here: it is neither evasion nor a
defect.

_What follows for an author._ Write the instruction, because it is the only lever that exists, and
`MemoryPack.SuggestedInstruction` carries the wording and the reasoning; it was obeyed in 5 of 5
neutral runs, which is the situation an ordinary application is in. Then do not read the refusal as
a guarantee. An application author cannot guarantee how a model phrases a descriptor, and a
descriptor a model deliberately chose to distinguish is one that will not collide. An application
whose correctness depends on contradictions being _raised_ — rather than merely being handled well,
which is what the measurement shows the model doing — needs a check outside this family;
near-duplicate detection raises what it can see and claims nothing more.

**The threshold is only meaningful inside the vector space the author injected, so 0.88 is a
starting point to measure against rather than a value to adopt.** The 0.965 above is one embedding
model's opinion of one pair of sentences on one corpus. Two properties of the mechanism make it
unsafe to carry that number anywhere else unmeasured, and an author whose corpus turns on numeric
values — a setting, a tolerance, a rating — should read both before accepting the default.

_Only the descriptor is embedded._ The details payload is never vectorized and never searched, so
two memories whose descriptors are built from the same words score 1.0 against each other however
much their payloads disagree. A model that files a topic ("relief valve pressure setting") and puts
the value in the details produces exactly that, and the refusal it triggers is correct but is
evidence about the descriptor, not about the values. It is tempting to conclude that the value
should therefore go into the descriptor so that it participates in the comparison. It should not:
adding the value makes two statements of one fact differ by a token instead of being identical,
which lowers their similarity rather than raising it, and the same reasoning taken one step further
is what produces the source-qualified descriptors described above that never collide at all. The
subject-only descriptor is the one that gets conflicts caught, and getting it is instruction, not
tool behavior, for the reasons given below.

_A numeral carries no special weight._ Similarity is whatever the injected generator says it is, and
nothing in this family knows that two numbers contradict each other. Where two statements of one
fact land relative to any threshold is therefore a property of the model, not of the conflict.
Two backends can disagree in opposite directions on the same pair: a lexical bag-of-words generator
scores two descriptors differing in one token out of `n` at exactly `(n - 1) / n`, so the same
12 psi / 18 psi conflict measures 0.875 stated in eight tokens and 0.917 stated in twelve — one side
of a 0.88 threshold and then the other, on descriptor length alone. A semantic model has no such
arithmetic and may place a numeric contradiction anywhere, including below a threshold that a
lexical generator clears. A threshold that is too low files contradictions as duplicates of each
other and loses one; a threshold that is too high stores both statements silently, and the
contradiction is never raised at all. The second failure is the quieter one.

_What to do about it._ Choose `MemoryOptions.NearDuplicateThreshold` by measuring, not by
inheritance: embed a handful of the author's own descriptor pairs with the author's own generator —
genuine restatements, genuine contradictions, and genuinely different facts about one subject — and
set the threshold between the lowest score a pair that should be caught achieves and the highest
score a pair that should not achieves. If those two ranges overlap, no threshold separates them and
the descriptors or the generator need to change, not the number. Re-measure whenever the generator
changes: `InMemoryMemoryStore` refuses a vector whose _length_ differs from what it already holds,
which catches a swap between models of different widths, but two generators of the same width would
be mixed silently and a measured threshold would quietly stop meaning anything.

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

**The author's instruction, not the tool, decides which source a corrected memory cites.** The tool
surface makes both corrections possible and makes neither one automatic: `memory_update` leaves
provenance as it was and reports the source it retained, `memory_revise` sets provenance to exactly
what the caller states. That is the mechanism, and it is complete. What it cannot do is choose. A
live model offered a conflict raised by a _different_ document was repeatedly observed picking
`memory_update` — leaving corrected text beside a citation of the superseded source — and it did so
even though the update tool reports the source it kept and its own description says a changed source
calls for a revision. Provenance came out right in every run only when the agent's instructions
explicitly required the new document to be cited. The correct home for that requirement is therefore
the instruction an application gives its agent, and `MemoryPack.SuggestedInstruction` carries it so
an author can append it rather than rediscover it. It is deliberately _not_ enforced in tool
behavior: whether a corrected memory should cite the new source, keep the first, or hold both is the
author's policy over their own corpus, and a library that decided it would be governing rather than
guaranteeing.

**The task-list family's adherence figures are not transferred to this family.** The task-list
family has a measured comparison of its own — a soft instruction used in 1 of 5 runs against an
explicit one in 3 of 3 — and that number belongs to that family's wording; nothing here inherits
it. What is measured for this family's suggested instruction is narrower and is stated above: over
eight live `research-assistant` runs pinned to `claude-sonnet-5` with `--embeddings local`, the
subject-only descriptor rule was obeyed 5 of 5 in the neutral arm (n = 5) and 0 of 3 in the
adversarial arm (n = 3). No figure beyond those is claimed here or anywhere else.

**Recall applies no similarity floor, and an application should say so.** `memory_recall` returns
the nearest memories the store holds up to the author's configured count, whatever their similarity,
so a question about a subject never recorded still comes back with matches — whatever was least
unlike it. This is deliberate: a floor would be a second threshold with no measurement behind it,
and a recall that returned nothing would tell a model less than a recall that returns weak matches a
model can read and reject. The consequence belongs in an application's instructions, because both
failure modes have been seen — a model answering confidently from a nearest match that was not about
the question, and a model reporting "no matches found" when two low-similarity matches had in fact
been returned. An agent should be told to read each returned descriptor and judge whether it is
actually about the question.

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
