# AgentKit Samples

Runnable applications that demonstrate AgentKit end to end against live models. Each sample is a
self-contained console application; none is a shipped library, so none is packed, published, or
given an SBOM — they exist to be read and run.

There are three, and they answer three different questions. Read the **document-assistant** sample to
learn how to *consume* AgentKit: how an application composes the shipped tool packs onto a policy
and hands the result to a provider. Read the **research-assistant** sample to learn how an agent
*works across turns*: planning, remembering, delegating safely, and keeping a conversation alive
past the model's context window. Read the **custom-tools** sample
to learn how to *extend* AgentKit: how an application author writes their own guarded tools and
publishes them as packs alongside the shipped ones.

All three share the same provider-selection design — a tool set composed once, then a single
factory-selection switch choosing the runtime — so a reader who has seen one recognizes the shape
of the others immediately.

## [document-assistant](document-assistant/)

**The consumption path.** A console chat application that grants an agent exactly two locations — a
workspace folder to read and a separate session folder to write artifacts into — and hands it the
shipped text-file, file, Markdown, and image tool packs. It makes the AgentKit safety model *observable*: it prints
every tool call and result, so containment, asymmetric read/write grants, capability gating, the
relative-versus-absolute path dialect, and the adapters' suppression of provider built-ins are all
visible as they happen. It runs unchanged against the GitHub Copilot runtime and any Ollama model.

Read this first if you are attaching AgentKit's ready-made tools to an agent and want to see how a
policy, its grants, and the pack builder fit together.

## [research-assistant](research-assistant/)

**The agent-infrastructure path.** A console application composing the three families that let an
agent work across turns rather than within one: `todo` to plan, `memory` to remember, and `agent` to
delegate. It researches a read-only corpus, writes its conclusions into a separate notes folder, and
makes the mechanisms observable — a plan written down before work starts, a finding filed with the
document it came from, a contradiction the agent notices by reading and corrects in place with
`memory_revise` (5 of 5 neutral live runs, `claude-sonnet-5`, `--embeddings local`), and a child
agent started with a task its parent stated. Near-duplicate refusal sits underneath that as a
backstop for the conflict a model does *not* notice: it fired in 0 of the 8 live runs measured, and
the sample's own README states where it does fire. A final
`--recall-question` turn answers on a fresh session with the memory tools and no way to read
anything, so recall — rather than the conversation it would otherwise still be sitting in — is
demonstrably what the answer rests on.

Two things in it are worth reading even if you never run it. First, **where the embeddings come
from**: `MemoryPack` requires an `IEmbeddingGenerator` and never inspects it, so the sample supplies
one of its own — an offline, dependency-free lexical generator, with `--embeddings ollama` swapping
in a real model and changing nothing else. Second, **how a child agent is contained**: the packs a
delegated agent may draw on are listed explicitly and exclude the task list and the memory store, so
a child cannot reach its parent's plan or record even by accident.

It is also the sample that **uses the session engine**, on either provider. The conversation runs
on a `CompactingAgentSession` — built from `ChatClientProviderSessionFactory` and
`ChatClientSummarizer` on Ollama, and from `CopilotProviderSessionFactory` and `CopilotSummarizer`
on Copilot — so it outlives the model's context window: older history is consolidated
into tiered records, a fresh provider session is seeded with them, and the turn loop carries on. The
sample prints what each turn occupies, whether it rotated, how hard it is compacting, and — the one
signal that matters most — whether compacting bought nothing and history had to be dropped. The
memory store lives outside the session and survives every rotation, which is why the recall turn
still works after one. Both providers run on a compacting session; on `--provider copilot` the
runtime reports its own window, and `--context-window` becomes a ceiling that lowers it so a
rotation is reachable at all. The startup banner says which shape a run got.

Read this if your agent's work spans turns, or if you are about to give an agent the ability to
start another one.

## [custom-tools](custom-tools/)

**The extension path.** A console chat application that demonstrates how an application author
writes their *own* guarded tools with `GuardedToolFactory` and publishes them as packs, composed
together with a shipped pack. It ships two author-written packs:

- **`docstats`** — a `docstats_wordcount` tool that reports the word, line, and character counts of
  a text file. It is a *path-taking* tool: it goes through `PathPolicy` for containment exactly as
  a shipped tool does, reports the file's location in the same path dialect the shipped tools use
  (via the public `PathPolicy` helpers), and returns its findings as a structured, machine-readable
  result. Its `docstats` family prefix is deliberately one the shipped library does not publish — the
  library now owns the `markdown` prefix — so the custom pack never collides with a built-in family.
- **`clock`** — a `clock_now` tool that reports the current local and UTC time. It takes *no path at
  all*, demonstrating that `GuardedToolFactory` is the construction path for **every** tool, not
  only path-based ones, and that a tool needing no policy simply does not consult one.

Read this if you are writing tools of your own and want a worked, compiling example of the
authoring contract — the naming convention, the pack's family prefix and required capabilities, the
structured-result and refusal shapes, and the path-dialect helpers a third-party tool reuses.
