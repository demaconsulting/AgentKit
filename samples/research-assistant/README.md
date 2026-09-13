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
  found again with `memory_recall`.
- **A contradiction caught by arithmetic rather than judgement.** The corpus deliberately contains
  a superseding revision. Filing the second, contradicting statement of one fact is *refused*:
  `memory_file` returns structured data with `stored: false` and names the conflicting memory. A
  refusal you can see is what keeps a model from reporting that it stored something it did not.
- **A correction that cites the right document.** The agent is told, explicitly, that a correction
  drawn from a *different* document is made with `memory_revise` stating that new document — not
  with `memory_update`, which keeps the source the memory already had. See
  [Why the instructions say that](#why-the-instructions-say-that).
- **Delegation that cannot reach back.** A child agent reads one document and reports what it says.
  It has no task list, no memories, and no ability to write anything — by composition, not by
  convention. See [Safe delegation](#safe-delegation).
- **Provider neutrality, again.** The same composition runs on the GitHub Copilot runtime or any
  Ollama model. Nothing above the single factory-selection switch knows which.

## Running it

From the repository root, with nothing installed beyond the .NET SDK and a Copilot login:

```bash
dotnet run --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --prompt "Review every document in the corpus and record what you find." \
  --prompt "What is the relief valve set to, and which document says so?"
```

Against an Ollama server instead:

```bash
dotnet run --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b \
  --embeddings ollama --embedding-model nomic-embed-text \
  --prompt "Review every document in the corpus and record what you find."
```

Omitting `--prompt` starts an interactive session that ends on `exit`, `quit`, Ctrl-C, or
end-of-input. `--help` lists every option with its default.

## Where the embeddings come from

`MemoryPack` requires an `IEmbeddingGenerator<string, Embedding<float>>` and never inspects which
backend it wraps — choosing one is the application's decision, which is exactly why a sample has to
make it visibly.

This sample offers two, and the flag that swaps them is the *entire* difference between them:

| `--embeddings` | Backend | Needs | Quality |
| -------------- | ------- | ----- | ------- |
| `local` (default) | `LexicalEmbeddingGenerator`, written in this sample | nothing | lexical, not semantic |
| `ollama` | an embedding model served by Ollama | a running Ollama and a pulled model | a real semantic embedding |

The default is offline so the sample runs from a fresh clone with no server, no credential, and no
model binary committed to this repository. `LexicalEmbeddingGenerator` is a hashed bag-of-words
vector: deterministic, L2-normalized, about forty lines of arithmetic. It measures **shared wording,
not shared meaning**. That is enough to demonstrate the behavior this sample is about — a fact
re-stated or contradicted in substantially the same words is caught as a near-duplicate — and it is
*not* enough for a real application, where a memory phrased differently from the question must still
be recalled. Its own unit tests assert both halves of that, including the paraphrase it fails to
match, so the limitation is a checked fact rather than a caveat in a comment.

One consequence worth stating: `MemoryOptions.DefaultNearDuplicateThreshold` (0.88) was calibrated
against a real embedding model, not against this generator. The threshold is the author's setting;
an application using an unusual embedding backend should expect to choose its own.

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

**The task list is described imperatively.** Measured against a five-phase task, a soft instruction
("keep track of multi-step work so progress is visible") produced use of the family in 1 of 5 runs;
an explicit one produced 3 of 3 with every phase recorded and closed. The sample appends
`TodoPack.SuggestedInstruction` verbatim rather than paraphrasing it, so the wording it ships cannot
drift from the wording that was measured.

**A correction from a new document must be a revision that cites it.** This is the live risk.
`memory_update` keeps the provenance a memory already carried; `memory_revise` sets it. Offered a
conflict raised by a *new* document, a model frequently picks `memory_update` anyway — leaving
corrected text beside a citation of the superseded source — and it does so even though
`memory_update` reports the source it retained and its own description says a changed source calls
for a revision. Provenance was correct in every run only when the instructions demanded the new
document be cited. So this sample's instructions demand it, and
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

No adherence figure is claimed for the memory instruction. The task-list numbers above are measured;
there is no memory equivalent, and one borrowed from a neighbor would be invented.

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
