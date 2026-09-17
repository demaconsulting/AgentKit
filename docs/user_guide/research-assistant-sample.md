# Sample: Research Assistant

The repository ships a runnable sample under `samples/research-assistant`. Where the
document-assistant sample shows what an agent may *touch*, this one shows how an agent *proceeds*:
it composes the three families that let work span turns — `todo` to plan, `memory` to remember, and
`agent` to delegate — onto a single policy, carries the conversation on a session that compacts its
own context, and runs against the GitHub Copilot runtime or
any Ollama model. Refer to the sample's own README for the full option reference and a catalogue of
things to try.

## Three Families, One Composition

The sample builds one `PathPolicy` over two locations and attaches five packs to it, and a sixth —
the agent family — when delegation is enabled. The corpus is
the working directory — the anchor relative paths resolve against — and is granted **read-only**,
because a research assistant cites sources rather than revising them. The notes folder is granted
read-write and lies outside the corpus, so it is reachable only by its absolute path:

```csharp
var policy = new PathPolicy(
    corpusRoot,
    [PathRule.ReadOnly(corpusRoot), PathRule.ReadWrite(notesRoot)]);

var builder = new ToolPackBuilder(policy)
    .Add(new TextFilePack())
    .Add(new FilePack())
    .Add(new MarkdownPack())
    .Add(new TodoPack())
    .Add(new MemoryPack(embeddingGenerator, MemoryOptions.Default, memoryStore));

if (delegationEnabled)
{
    builder = builder
        .WithHostCapabilities(HostCapabilities.Delegation)
        .Add(new AgentPack(profiles, runner, childPacks, HostCapabilities.None));
}
```

Delegation is gated by a host capability exactly as vision is in the document-assistant sample: with
`--no-delegation`, `agent_run` is not refused at call time, it is never created.

## A Conversation That Outlives the Window

This sample is the session engine's first user, and it is the right one: an agent whose work spans
turns runs out of context sooner than one that answers a question and stops. On `--provider ollama`
the conversation is carried by a `CompactingAgentSession`, so it outlives the model's window.

The application states three things and nothing else — a provider-session factory carrying the
client and the window, a summarizer that runs outside the conversation, and the options:

```csharp
var providerSessions = new ChatClientProviderSessionFactory(sessionClient, window.Tokens);
var summarizer = new ChatClientSummarizer(summaryClient);
var options = new AgentSessionOptions(summarizer, instructions, [.. tools]);

await using var session = await CompactingAgentSession.CreateAsync(options, providerSessions);

var turn = await session.SendAsync("Review every document in the corpus.");
Console.WriteLine(turn.Text);
```

Each turn is then acted on rather than merely printed. Occupancy is reported every turn, so a
reader watches it climb toward the window; `RotationOccurred` is printed with the running count of
summarizer calls beside it, because consolidation is the dominant cost of the arrangement; `Level`
says how hard the session is compacting; and `MaterialDropped` is printed as a warning, because it
is the one signal that compacting bought nothing and history was discarded.

**The memory store is not compacted, and that separation is the point.** A rotation consolidates the
*conversation*. The store is the application's, lives outside the session entirely, and survives
every rotation intact — which is why the `--recall-question` turn still answers correctly after one.

**On `--provider ollama` the window is read from the provider rather than assumed.** An
`IChatClient` publishes no context window, so the application has to answer for it. The sample
prefers, in order: a window stated with `--context-window`; the **loaded** model's context length,
which is what the Ollama server actually enforces; the model's **published** maximum, which the
server may have loaded it below; and finally Ollama's own default, announced as an assumption. The
startup banner names which of the four a run used, because a session told a window larger than the
server enforces will not rotate until the provider has already truncated the conversation, and
nothing downstream can detect that.

**On `--provider copilot` no window is stated, because the runtime answers for itself.** Copilot
reports the tokens it currently holds, the limit it will hold them to, and how much of the total the
conversation accounts for, with every turn. So the Copilot adapter is never told a window. What
`--context-window` does there is set a *ceiling*: it can lower the figure the session accounts
against, making it rotate sooner, but never raise it above what the runtime reports. The banner says
"at most N tokens" in that case, because the effective window is the lower of the two and is not
known until the first turn reports it. That is the honest difference between the two providers.

`--summary-model` sends each consolidation to a different model, on either provider. Consolidation
is summarization rather than reasoning, so a smaller model is usually right; either way it runs
outside the session being compacted — on a separate client for Ollama, and on a separate, tool-free
Copilot session for Copilot.

**One thing genuinely differs between the two providers: the tool trace.** On `--provider ollama`
the sample installs a chat-client decorator of its own beneath the session, surfacing the tool calls
a session turn does not report; its README explains why that one is the application's work and the
tool-calling loop and the occupancy figure are not. The Copilot runtime carries the tool loop itself
and offers no equivalent seam, so a compacting Copilot run prints answers and session figures rather
than a live tool trace.

## Supplying an Embedding Generator

`MemoryPack` requires an `IEmbeddingGenerator<string, Embedding<float>>` and never inspects which
backend it wraps, because choosing one is the application's decision. The sample therefore makes that
decision visibly, and offers two:

- `--embeddings local` (the default) uses `LexicalEmbeddingGenerator`, written in the sample itself:
  a deterministic, L2-normalized hashed bag-of-words vector needing no server, no credential and no
  model file. It lets the sample run from a fresh clone with nothing installed.
- `--embeddings ollama` uses a real embedding model served by Ollama.

The offline generator measures **shared wording rather than shared meaning**. That is enough to
demonstrate what this sample is about — a fact restated or contradicted in substantially the same
words is caught as a near-duplicate — and it is deliberately not enough for a real application,
where a memory phrased differently from the question must still be recalled. Its unit tests assert
both halves of that, including the paraphrase it fails to match. Note also that
`MemoryOptions.DefaultNearDuplicateThreshold` was calibrated against a real embedding model; the
threshold is the author's setting, and an unusual backend deserves its own.

## Delegating Safely

`AgentPack` starts a second agent, and the sample is written so a child cannot reach its parent's
state, at three independent levels:

1. **AgentKit's API shape.** `AgentPack` accepts packs, never tools, so a child's tools are composed
   afresh against the child's own policy. Per-composition state — a task list, a store the pack
   allocated itself — belongs to that child alone, and a caller cannot hand a built tool list down.
2. **The pack list the application passes.** `CreateChildPacks()` lists the reading families only:
   no `TodoPack`, no `MemoryPack`, and no `AgentPack` — so a child has no task list, no memories,
   and no means of delegating further. A profile naming `todo_set` would conjure nothing, because
   no attached pack publishes it. This is asserted by a unit test.
3. **The profiles.** The application writes every word of a child's instructions and lists the tools
   it may use; the parent supplies only the task. The reading profile narrows its grants to the
   corpus alone, so it cannot reach the notes folder its parent can write to.

The second level carries a consequence worth stating on its own. This application supplies its *own*
store to the parent's `MemoryPack`, and a supplied store is shared by every composition that pack is
used in — so listing that same pack for children would hand every child the parent's memories. That
is a legitimate thing to want and an easy thing to get by accident.

## Instructions That Reflect Measured Behavior

The sample appends `TodoPack.SuggestedInstruction` and `MemoryPack.SuggestedInstruction` verbatim
rather than paraphrasing them, so the wording it ships cannot drift from the wording the library
published. It then adds three sentences of its own, each answering something observed rather than
imagined:

- **A correction from a different document is a revision that cites it.** `memory_update` keeps the
  provenance a memory already carried; `memory_revise` sets it. Offered a conflict raised by a new
  document, a model frequently picks `memory_update` anyway, leaving corrected text beside a
  citation of a superseded source — even though the update tool reports the source it retained.
  Provenance was correct in every run only when the instructions demanded the new document be cited,
  and with that demand in place it was correct in 5 of 5 neutral live runs (`claude-sonnet-5`,
  `--embeddings local`).
- **Recall applies no similarity floor.** `memory_recall` returns the nearest memories it holds
  whatever their similarity, so a question about an unrecorded subject still returns matches. The
  agent is told to read each returned descriptor and judge it.
- **A child agent shares none of the parent's state.** It has no task list and no memories, so
  whatever it reports that is worth keeping, the parent must file itself.

The task-list family's instruction wording is backed by a measurement of its own: against a
five-phase task, a soft instruction produced use of the family in 1 of 5 runs against 3 of 3 with an
explicit one. That figure comes from an earlier measurement of the task-list family, recorded in the
Todo subsystem design, and **not** from this sample's live runs; the model and the embedding arm it
was taken against are not recorded, so it is reported here as the separate, differently-conditioned
measurement it is.

The memory instruction has narrower figures of its own, from eight live runs of this sample pinned
to `claude-sonnet-5` with `--embeddings local`: its subject-only descriptor rule was obeyed in 5 of
5 neutral runs whose prompts never mentioned descriptors and in 0 of 3 adversarial runs whose prompt
demanded a separate memory, and `memory_revise` was chosen over `memory_update` with the new source
cited in 5 of 5 of those neutral runs. In those same five neutral runs the agent noticed the
corpus's superseded value by reading and corrected it in place, and the near-duplicate refusal fired
in none of the eight. The refusal is a backstop for the conflict a model does *not* notice, not the
ordinary path; see the sample's own README for the full measurement.

**These live figures are measured deliberately, not continuously.** They come from the repository's
opt-in live-model workflow, which runs on request and on a weekly schedule and is no part of the
pull-request merge gate — every test that gate runs is hermetic and offline. Unlike the test totals,
the lint gates and the generated trace matrix, these numbers are therefore not re-verified on every
change, and they can drift silently as the models behind them change.

## Running the Sample

Run the sample from the repository root. Against the GitHub Copilot runtime, with nothing else
installed:

```bash
dotnet run --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --prompt "Review every document in the corpus and record what you find." \
  --prompt "What is the relief valve set to, and which document says so?"
```

Against an Ollama server hosting a tool-calling model and an embedding model:

```bash
dotnet run --project samples/research-assistant -- \
  --corpus samples/research-assistant/workspace \
  --provider ollama --host http://your-ollama-host:11434 --model qwen3.5:9b \
  --embeddings ollama --embedding-model nomic-embed-text \
  --prompt "Review every document in the corpus and record what you find."
```

Omitting `--prompt` starts an interactive session. `--transcript <path>` appends one line per tool
call, naming the tool and nothing else, which is how an unattended run can be checked without
reading its prose. `--context-window <tokens>` states the window the compacting session is accounted
against when the Ollama server cannot be asked; on `--provider copilot` it is a downward-only ceiling
instead, lowering the window the session accounts against but never raising it above what the runtime
reports. `--summary-model <name>` sends each consolidation to a smaller model, and
applies to both providers.
