# Sample: Research Assistant

A console application that gives an agent the three tool families an agent needs in order to work
across turns rather than within one: **`todo`** to plan, **`memory`** to remember, and **`agent`**
to delegate. It researches a read-only corpus of documents, writes its findings into a separate
notes folder, and prints every tool call and result so that what it is doing is visible rather than
asserted.

Where [`document-assistant`](../document-assistant/) shows what an agent may *touch*, this sample
shows how it *proceeds*.

## What it demonstrates

- **A plan the user can see.** The agent writes its steps down with `todo_set` before starting and
  marks each one `done` as it goes, so progress is observable rather than inferred.
- **Findings that outlive the turn that produced them.** Each document read becomes a memory — a
  short descriptor, a fuller payload, and the document it came from — filed with `memory_file` and
  found again with `memory_recall`. `--recall-question` answers a question on an agent that has the
  memory tools and *no way to read anything*, in a fresh session, so recall is the only thing the
  answer can rest on. See [What the recall turn proves](#what-the-recall-turn-proves-and-what-the-prompts-cannot).
- **A writable location that is actually written to.** Conclusions go into the notes folder, granted
  read-write and lying outside the read-only corpus. The instructions tell the agent to write its
  conclusions there before answering, because a granted capability that is never exercised
  demonstrates nothing about the policy governing it. Observed: notes were written in the run whose
  prompt asked for a review and summary, and not in narrow single-step runs — the file appears when
  a turn actually produces conclusions, which is 1 of the 3 live runs measured.
- **A contradiction the agent resolves, with near-duplicate arithmetic as the backstop.** The corpus
  deliberately contains a superseding revision. In **5 of 5 neutral live runs** (`claude-sonnet-5`,
  `--embeddings local`) the agent noticed the contradiction itself, cited `02-field-revision.md` and
  used `memory_revise` to correct the memory in place, leaving exactly one relief-valve memory at
  18 psi. The near-duplicate refusal fired in **none of those 5 runs — and neither did the failure
  it exists to catch**. The refusal is what covers the case where the model has *not* noticed: file
  a near-identical *subject* descriptor and `memory_file` declines it with structured data,
  `stored: false`, naming the conflicting memory. A refusal you can see is what keeps a model from
  reporting that it stored something it did not. Across **8 live runs it never fired at all**; it
  fires 5 of 5 when descriptors genuinely collide, and in the unit tests. See
  [When the refusal fires, and when it does not](#when-the-refusal-fires-and-when-it-does-not).
- **A correction that cites the right document.** The agent is told, explicitly, that a correction
  drawn from a *different* document is made with `memory_revise` stating that new document — not
  with `memory_update`, which keeps the source the memory already had. See
  [Why the instructions say that](#why-the-instructions-say-that).
- **Delegation that cannot reach back.** A child agent reads one document and reports what it says.
  It has no task list, no memories, and no ability to write anything — by composition, not by
  convention. See [Safe delegation](#safe-delegation).
- **A conversation that outlives the context window.** On `--provider ollama` the conversation runs
  on an AgentKit `CompactingAgentSession`: when the window fills, older history is consolidated into
  tiered records, a fresh provider session is seeded with them, and the turn loop carries on. Every
  turn prints what it occupies, whether it rotated, and whether compacting bought nothing and
  history had to be dropped. See
  [Context that outlives the window](#context-that-outlives-the-window).
- **Provider neutrality, with one honest difference.** The same composition — the same policy, the
  same packs, the same instructions — runs on the GitHub Copilot runtime or any Ollama model, and
  nothing above the single factory-selection switch knows which. Both now carry an AgentKit
  compacting session, so a conversation outlives the model's window on either. The difference is
  where the window comes from: Ollama publishes none through `IChatClient`, so the sample reads it
  from the server and states it; Copilot reports its occupancy and its limit with every turn, so
  nothing is stated. The startup banner says which a run got. The other difference is the tool
  trace: the sample surfaces one beneath the Ollama session through a decorator of its own, and the
  Copilot runtime carries the tool loop itself and offers no equivalent seam.

## Running it

From the repository root, with nothing installed beyond the .NET SDK and a Copilot login. `-c Release`
matches what `build.ps1` produces, so the sample starts from binaries that are already built;
without it `dotnet run` compiles a separate Debug build of the sample and the whole library first.

```bash
dotnet run -c Release --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --prompt "Review every document in the corpus and record what you find." \
  --recall-question "What is the relief valve set to, and which document says so?"
```

Against an Ollama server instead:

```bash
dotnet run -c Release --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b \
  --embeddings ollama --embedding-model nomic-embed-text \
  --prompt "Review every document in the corpus and record what you find."
```

Omitting `--prompt` starts an interactive session that ends on `exit`, `quit`, Ctrl-C, or
end-of-input. `--help` lists every option with its default.

## Context that outlives the window

This sample's subject is work that spans turns, and a conversation that spans enough turns runs out
of context. On `--provider ollama` the conversation is therefore carried by an AgentKit
**compacting session** rather than by a plain message list.

An application states three things and nothing else:

```csharp
// 1. A provider-session factory carrying the client and the window. AgentKit refuses to guess a
//    context window, so the application answers for it — here by asking Ollama.
var providerSessions = new ChatClientProviderSessionFactory(sessionClient, window.Tokens);

// 2. A summarizer, which runs OUTSIDE the conversation it compacts, on a client of its own.
var summarizer = new ChatClientSummarizer(summaryClient);

// 3. The options: what the agent is, what it may call, and how much recent history to keep word
//    for word. Everything else about compaction is the library's, not a setting.
var options = new AgentSessionOptions(summarizer, instructions, tools);

await using var session = await CompactingAgentSession.CreateAsync(options, providerSessions);

var turn = await session.SendAsync("Review every document in the corpus.");
Console.WriteLine(turn.Text);
```

Each turn hands back four facts, and the sample acts on all four rather than only printing the
answer:

- **`Usage`** — prints the conversation's occupancy against the window, every turn, so the next
  rotation is comprehensible rather than sudden.
- **`RotationOccurred`** — prints the rotation with the running count of summarizer calls beside it,
  because consolidation is the expensive part of the arrangement.
- **`Level`** — prints how hard the session is compacting. A level climbing to `High` over several
  rotations is what a struggling long-running agent looks like.
- **`MaterialDropped`** — prints a warning. It is the one signal that compacting bought nothing and
  history was discarded outright.

```text
  [session] conversation 24180 of 40960 tokens (overhead 0), compaction level Low
  [session] rotated — older history consolidated. Rotations: 1. Summarizer calls so far: 1.
```

**The memories are not compacted, and that is the point of having them.** A rotation consolidates
the *conversation*; the memory store is the application's, lives outside the session entirely, and
survives every rotation intact. An agent whose window has turned over three times can still answer
from what it filed on its first turn — which is precisely what `--recall-question` checks.

### Where the window comes from

An `IChatClient` publishes no context window, so AgentKit is told one once, where the provider is
configured, and answers with it thereafter. This sample reads it from Ollama rather than hard-coding
a number, and prefers the figure the server will actually enforce:

1. `--context-window <tokens>`, if stated. It settles the question.
2. The **loaded** model's context length, from the server's list of running models. This is what the
   server enforces, and it is often smaller than the model's maximum.
3. The model's **published** context length, from its metadata. A maximum, not a limit in force —
   the banner says so.
4. Failing all of that, Ollama's own default of 4096 tokens, announced as an assumption.

The banner prints which of the four a run used, because a session told a window larger than the
server enforces will not rotate until the provider has already truncated the conversation, and
nothing downstream can detect that.

### Where the consolidations go

`--summary-model <name>` sends each consolidation to a different Ollama model. Consolidation is
summarization rather than reasoning, so a smaller model is usually the right choice; without the
flag the conversation's own model does the work, on a separate client. Either way it runs *outside*
the session being compacted, because a consolidation sent through the live session would spend the
very context it exists to reclaim.

### What the sample still writes for itself

One chat-client decorator sits beneath the session — `ToolCallReportingChatClient` — and it is there
for a reason the library cannot serve:

- **A session turn reports no tool activity.** The session records every call and result into the
  transcript it later consolidates, and hands none of them back, so the reporting client reads them
  from the conversation on its way to the provider instead. Watching the agent plan, file, recall and
  delegate is the sample's whole demonstration, so it is worth the class.

Two other decorators used to sit here and no longer do. The tool-calling loop and the occupancy
figure are now AgentKit's, not the application's:

- **The tool-calling loop is installed by `ChatClientProviderSessionFactory`.** A session declares
  its tools to the model on every request, so a bare client emits tool calls that nothing answers.
  The factory closes that loop itself, rather than leaving a composition that looks right and is
  silently wrong.
- **The occupancy figure is the last prompt, not the loop's summed usage.** A turn that calls six
  tools is six requests, and the response the loop returns reports their input tokens added
  together. Read as occupancy that made a tool-using agent appear to fill its window on its first
  turn. The factory now records the size of each real prompt beneath the loop and reports the last
  one, so the sample no longer writes a recorder and a repairer to undo it.

On `--provider copilot` none of this applies, for a different reason: there is no chat-client
pipeline to place anything in. `CopilotProviderSessionFactory` creates one Copilot session per
rotation and the runtime carries the tool loop itself, so there is no loop to close and no summed
usage to undo — the runtime reports the occupancy, the limit and the conversation's own share
directly. What the sample gives up in exchange is the live tool trace: the Copilot runtime offers no
seam beneath the session to print calls from, so a compacting Copilot run prints answers and session
figures rather than each call as it happens. `--transcript` therefore records tool calls on the
Ollama path only.

## What the recall turn proves, and what the prompts cannot

`--recall-question` is not another `--prompt`, and the difference is the point.

A `--prompt` turn shares one session with the turns before it. A final turn asked "what is the
relief valve set to?" therefore already has the document text in its context, because it read the
document a few turns earlier — so it can answer correctly without calling `memory_recall` at all,
and in measured live runs it did exactly that: **the answering turn made no recall call in any
run.** The memories were filed and were never demonstrably the thing an answer came from. A
single-session transcript cannot tell *remembered* from *still in context*.

`--recall-question` removes both alternatives rather than asking the model not to use them. It runs
on a separate agent composed from `MemoryPack` and nothing else — no file, text or markdown tool
exists in that composition, so no document can be reopened — in a **fresh session**, so no earlier
turn is in its context. What remains is the store the earlier turns filled. An answer that states a
corpus fact can then only have arrived through `memory_recall`, and the printed tool calls show it
doing so.

What this still does not prove: that memories survive the process. The store is
`InMemoryMemoryStore`, so everything filed is gone when the run ends. Durable memory is an
`IMemoryStore` an application supplies; this sample does not ship one.

## When the refusal fires, and when it does not

The sample is built around a superseded engineering value — 12 psi in Revision A, 18 psi in
Revision B — and how the agent handles that is the thing worth watching. The near-duplicate refusal
is a **backstop**, not the primary mechanism, and the honest measurement is that **it did not fire
in any of the 8 live runs** measured here (`claude-sonnet-5`, `--embeddings local`).

### The neutral arm (n = 5): the agent revises, and that is the better outcome

Five runs whose prompts never mention descriptors at all. What happened, every time:

| What was measured | Result |
| ----------------- | ------ |
| Subject-only descriptor rule obeyed (value/revision kept out of the descriptor) | **5 / 5** |
| Near-duplicate refusal fired | **0 / 5** |
| Two contradictory memories stored silently — the failure the refusal exists to catch | **0 / 5** |
| Contradiction noticed and corrected in place with `memory_revise`, citing `02-field-revision.md` | **5 / 5** |
| Store left holding exactly one relief-valve memory, at 18 psi | **5 / 5** |
| `memory_revise` chosen over `memory_update` | **5 / 5** |
| Corrected memory cites the new source | **5 / 5** |
| Fresh-session `--recall-question` turn answered 18 psi through `memory_recall` | **5 / 5** |

The refusal never firing here is not a gap. The refusal fires when the model files a near-identical
descriptor — which is to say, **when it has not noticed the conflict**. That is exactly the case a
backstop is for, and exactly the case no amount of reading is going to fix. When the model *does*
notice, it revises, and a revision is the better of the two outcomes: one memory, corrected, citing
the document that corrected it, instead of a refusal the model then has to act on. **Revise-in-place
is the primary path in practice; the refusal is the floor under it.**

The arithmetic itself is not in doubt. It fired **5 of 5** in an earlier, directly driven
measurement in which the two descriptors genuinely collided, and it fires in this sample's own unit
tests (`DescriptorPhrasingTests`). What the live runs measure is how often a model reaches it, not
whether it works.

### The adversarial arm (n = 3): two memories, and that is arguably right

Three runs whose second prompt demands *"a separate new memory… do not revise or update"*.

| What was measured | Result |
| ----------------- | ------ |
| Subject-only descriptor rule obeyed | **0 / 3** |
| Near-duplicate refusal fired | **0 / 3** |
| Both memories stored — store left holding 12 psi and 18 psi at once | **3 / 3** |

The model narrated exactly what it was doing: *"I used a distinct descriptor ('…per field revision')
so it wouldn't collide with the Revision A memory."* Only the descriptor is embedded, so a
descriptor chosen to distinguish is a descriptor that will not collide.

**This is an adversarial lower bound, not everyday behavior** — being told to keep two memories is
itself pressure toward two distinct labels — and the outcome is arguably the *correct* one. "12 psi
per Revision A" and "18 psi per Revision B" are both true statements, about different documents. Two
memories carrying accurate provenance is a legitimate representation of a superseded specification,
and it is the representation the user explicitly asked for. This is the **application author
governing the settings**, which is the intended design: AgentKit guarantees that every file is
checked, and leaves what *should* happen to a conflict over the author's own corpus to the author,
their instructions and their threshold.

### Seeing it fire

Add a prompt that states the descriptor rule outright, so the two statements land on one subject:

```bash
dotnet run -c Release --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --prompt "Read 01-initial-spec.md and file what it says about the relief \
valve. Use the subject alone as the descriptor: no document name, no \
revision, no value in it." \
  --prompt "Now read 02-field-revision.md and file what it says about the \
relief valve, under that same subject descriptor."
```

**The measurement above deliberately did not use these prompts.** They state the descriptor rule in
the prompt, which is the very cue being tested — an agent told the rule twice cannot evidence
whether the shipped *instruction* alone is enough. The neutral arm's prompts therefore never mention
descriptors, and its 5-of-5 adherence figure is about the instruction rather than about the prompt.
These prompts are for reproducing the refusal on demand, not for measuring adherence.

This is still not a guarantee: the model writes the descriptor, and no prompt makes it obey. If the
refusal does not appear, read the `memory_file` arguments the console prints — the descriptors are
right there, and they are the whole explanation.

**What is still not covered.** The refusal is visible; two contradictory memories filed under
distinct descriptors are not. In the measured neutral runs that silent case did not occur — the
agent corrected the record instead — but nothing guarantees it. An application whose correctness
depends on contradictions being *raised*, rather than merely being handled well, needs a check
outside the memory family.

## Where the embeddings come from

`MemoryPack` requires an `IEmbeddingGenerator<string, Embedding<float>>` and never inspects which
backend it wraps — choosing one is the application's decision, which is exactly why a sample has to
make it visibly.

This sample offers two, and the flag that swaps them is the *entire* difference between them:

| `--embeddings` | Backend | Needs | What it measures |
| -------------- | ------- | ----- | ---------------- |
| `local` (default) | `LexicalEmbeddingGenerator`, written in this sample | nothing | shared wording |
| `ollama` | an embedding model served by Ollama | a running Ollama and a pulled model | shared meaning |

The default is offline so the sample runs from a fresh clone with no server, no credential, and no
model binary committed to this repository. `LexicalEmbeddingGenerator` is a hashed bag-of-words
vector: deterministic, L2-normalized, about forty lines of arithmetic.

**It is not simply a weaker version of a semantic model, and the difference matters most on exactly
the case this sample is built around.** Two descriptors that differ in one word out of `n` share
`n - 1` unit coordinates, so their cosine is `(n - 1) / n` — whatever that word is. A numeral is one
token like any other, and the fact that two numerals *contradict* each other enters the arithmetic
nowhere. The sample's own unit tests measure the consequence:

| Descriptor pair | Tokens | Cosine | Against the 0.88 default |
| --------------- | ------ | ------ | ------------------------ |
| "The relief valve is set at 12 psi." / "…18 psi." | 8 | 0.875 | **below** — both stored |
| "Relief valve setting for the Harbor Skiff bilge pump is 12 psi." / "…18 psi." | 12 | 0.917 | above — refused |

So whether this generator catches the corpus's superseded 12 psi → 18 psi value depends on how
verbosely the model happened to phrase its descriptor. And when a model writes a *topic* as the
descriptor ("Relief valve pressure setting") and puts the value only in the details, the two
memories embed identically and score exactly 1.0 — because **only the descriptor is embedded** and
the details are never vectorized. That 1.0 is a property of the descriptor/payload split, not of
this generator; any backend gives 1.0 for identical input.

None of this is evidence about what a real semantic model would do with the same pair. It might
score a numeric contradiction higher than this generator does, or lower — low enough to fall under
the threshold, in which case both statements are stored silently and the contradiction is never
raised. **Near-duplicate detection is only as good as the vector space it is given**, and that space
is the application's choice. `MemoryOptions.DefaultNearDuplicateThreshold` (0.88) was calibrated
against a real embedding model on a real corpus during a spike, not against this generator; an
application whose domain turns on numeric values should measure its own threshold against its own
generator and corpus rather than inheriting either number. See
[the memory design note](../../docs/design/agent-kit-tools/memory.md) for how to do that.

Its unit tests assert all of this — the paraphrase it fails to match, the token-count arithmetic,
and the exact 1.0 — so the limitations are checked facts rather than caveats in a comment. **Do not
use it in a real application**: run `--embeddings ollama`, or supply any other
`IEmbeddingGenerator`, and nothing else in this sample changes.

## Safe delegation

`AgentPack` starts a second agent. Getting that wrong is how a sub-agent ends up writing into its
parent's state — in a separate system, a sub-agent sharing its parent's task store replaced that
parent's plan wholesale. This sample is written so that cannot happen, at three independent levels:

1. **AgentKit's own shape.** `AgentPack` accepts *packs*, never tools. A child's tools are composed
   afresh against the child's own policy, so per-composition state — a task list, a memory store the
   pack allocated itself — belongs to that child alone. A caller cannot hand a built tool list down;
   the API has no way to express it.
2. **The pack list this sample passes.** `AgentComposition.CreateChildPacks()` lists the reading
   families only: no `TodoPack`, no `MemoryPack`, no delegation of a child's own. A profile that
   named `todo_set` would conjure nothing, because no attached pack publishes it. Withholding the
   pack is stronger than withholding the name, and it is asserted by a unit test.
3. **The profiles.** The application writes every word of a child's instructions and lists the tools
   it may use; the parent agent supplies only the task. The `document-reader` profile narrows its
   grants to the corpus alone, so it cannot reach the notes folder its parent can write to —
   narrowing is permitted, widening is a composition-time error. The `summarizer` profile declares
   no tools at all.

Note the second reason `MemoryPack` is absent from the child pack list: this application supplies
its *own* store to the parent's pack, and a supplied store is shared by **every** composition that
pack is used in. Listing that same pack for children would give every child the parent's memories.
That is a legitimate thing to want and an easy thing to get by accident, so the sample states the
opposite intent explicitly.

Run with `--no-delegation` to watch the other half of the mechanism: the tool is not refused at call
time, it is never created, because the `Delegation` host capability was not declared.

## Why the instructions say that

Three sentences in this sample's instructions are there because of measured behavior, not taste.
Two more, further down, were added after a live run showed a granted capability going unused and a
plan being written after the fact.

**The task list is described imperatively.** Measured against a five-phase task, a soft instruction
("keep track of multi-step work so progress is visible") produced use of the family in 1 of 5 runs;
an explicit one produced 3 of 3 with every phase recorded and closed. The sample appends
`TodoPack.SuggestedInstruction` verbatim rather than paraphrasing it, so the wording it ships cannot
drift from the wording that was measured.

**A descriptor names the subject, never the source, the revision or the value.** Detection compares
descriptors, because only the descriptor is embedded — so a model that recognizes a conflict and
writes "…per field revision (Revision B)" to distinguish it has stepped past the arithmetic by
understanding the problem correctly. That is what happened in 3 of 3 live runs *before* this
instruction existed, and it is still what happens in 3 of 3 adversarial runs that demand a separate
memory. With the instruction and neutral prompts the rule is obeyed in **5 of 5** runs: values and
revisions go into the details and the provenance parameters, which are not embedded. The instruction
states the rule *and its reason*, because this project has repeatedly found that a rule without a
reason gets applied inconsistently — and here the reasoning that overrides it is the same reasoning
that produced the collision-free descriptors. `MemoryPack.SuggestedInstruction` carries the same
guidance for any application.

**A correction from a new document must be a revision that cites it.** This is the live risk.
`memory_update` keeps the provenance a memory already carried; `memory_revise` sets it. Offered a
conflict raised by a *new* document, a model frequently picks `memory_update` anyway — leaving
corrected text beside a citation of the superseded source — and it does so even though
`memory_update` reports the source it retained and its own description says a changed source calls
for a revision. Provenance was correct in every run only when the instructions demanded the new
document be cited — and with that demand in place it was correct in 5 of 5 neutral live runs. So
this sample's instructions demand it, and
`MemoryPack.SuggestedInstruction` now carries the same guidance for any application. It is
deliberately *not* enforced in the tools: which source a corrected memory should cite is the
author's policy over their own corpus, and AgentKit guarantees the mechanism rather than governing
the policy.

**Recall has no similarity floor.** `memory_recall` returns the nearest memories it holds, whatever
their similarity, so asking about something never recorded still returns matches. Both failure modes
have been observed: answering confidently from a nearest match that was not about the question, and
reporting "no matches found" when two low-similarity matches were in fact returned. The agent is
told to read each returned descriptor and judge it. Try
`--prompt "What do you know about the mooring winch?"` — nothing in the corpus is about one.

No general adherence figure is claimed for the memory instruction as a whole. What is measured is
narrower and is stated where it belongs: the subject-only descriptor rule was obeyed in 5 of 5
neutral runs and 0 of 3 adversarial ones, and `memory_revise` was chosen over `memory_update` with
the new source cited in 5 of 5 neutral runs. The task-list 1-of-5 versus 3-of-3 comparison above is
a different measurement of a different family's wording, and is not transferable to this one.

**Conclusions must be written to the notes folder, and the instruction names the tool.** In three
live runs the notes folder stayed empty. It is the sample's only writable location, so the only
demonstration of the read-write half of the path policy was a grant nothing ever used. The model was
not refusing; nothing had told it to write. The instruction now names `text_file_create`, names the
folder, and says to write before answering.

**A plan is written before the work, not after it.** One live run opened by filing a task already
marked `done` — "Review all corpus documents", recorded as complete before any document had been
read. That is a summary wearing a plan's clothes, and it tells a watcher nothing about what is
coming. The instruction forbids adding an already-finished item.

## The corpus

Three short documents, written so the interesting cases are real rather than staged:

- `01-initial-spec.md` — Revision A. The relief valve is set at **12 psi**.
- `02-field-revision.md` — Revision B, which **supersedes** Revision A: the relief valve is set at
  **18 psi**. This is the changed-source correction.
- `03-service-log.md` — per-hull service entries that reference both settings without changing
  either, so the agent has to distinguish a specification from a record of work.

The corpus is granted **read-only**: a research assistant cites sources, it does not revise them.
Notes are written to a separate folder, granted read-write, outside the corpus — so it can only be
reached by its absolute path, never by a relative name.

## Checking a run without reading it

`--transcript <path>` appends one line per tool call, naming the tool and nothing else. That is what
the repository's opt-in live-model workflow (`.github/workflows/live_samples.yaml`) asserts on: that
the agent actually planned, filed, recalled and delegated, rather than merely describing doing so.
Arguments are deliberately never written to it, because a serialized argument would carry document
contents into a log.

## Reading the console output

Tool calls arrive in parallel, so the console shows a block of calls and then a block of results in
*completion* order — which is not call order. Every line therefore carries the provider's call
identifier, and each result repeats the tool name of the call it answers:

```text
  [tool call <call-id>] memory_file {"descriptor":"Relief valve pressure setting", …}
  [tool result <call-id>] memory_file ->
      {
        "stored": false,
        "reason": "near_duplicate",
        "similarity": 1,
        …
      }
```

Match on the identifier — the provider's own opaque call id, repeated verbatim on both lines — not
on position. Results are trimmed to one line at 300 characters, with
one exception: **`memory_*` results are printed whole**, indented one field per line beneath their
header, because a `memory_recall` showing one of five returned matches makes the demonstration
impossible to check by reading. Printing a recall whole on one line produced the opposite problem —
about fourteen hundred characters wrapping into a block — so the content is kept and the shape is
given back to it. A recall is bounded by the author's configured count; a file read is not, which is
where the one-line limit still earns its keep.

The startup banner names the model when it can. With `--provider ollama` that is always possible.
With `--provider copilot` and no `--model`, the runtime resolves a model at session time from what
the signed-in user may use and reports nothing back, so the banner says the model is unknown rather
than printing a placeholder — **pass `--model` for any run whose behavior you intend to cite.** The
banner also names the conversation shape — a compacting session with its window and where that
window came from, or a provider-managed session that does not compact — because a run that cannot
compact and a run that simply never needed to look identical from a transcript of answers.
