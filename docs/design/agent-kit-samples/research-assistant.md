## ResearchAssistant

![AgentKit Samples Structure](AgentKitSamplesView.svg)

The `research-assistant` sample is a console application that demonstrates the agent-infrastructure
path: how an agent plans, remembers, and delegates safely across turns, and how an application
supplies the embedding backend the memory family requires.

### Purpose

ResearchAssistant exists to demonstrate the three families that let an agent work across turns rather
than within one: `todo` to plan, `memory` to remember, and `agent` to delegate. It researches a
read-only corpus, writes its conclusions into a separate notes location, and makes the mechanisms
observable — a plan written down before work starts, a finding filed with the document it came from, a
contradicting restatement refused as a near-duplicate with the conflicting memory named, and a child
agent started with a task its parent stated. A final `--recall-question` turn answers on a fresh
session with the memory tools and no way to read anything, so recall is demonstrably what the answer
rests on.

Two design decisions carry most of the sample's teaching value. First, **where the embeddings come
from**: the memory pack requires an embedding generator and never inspects it, so the sample supplies
one of its own — an offline, dependency-free lexical generator that lets a reader run the sample with
nothing installed, with `--embeddings ollama` swapping in a real model and changing nothing else.
Second, **how a child agent is contained**: the packs a delegated agent may draw on are listed
explicitly and exclude the task list and the memory store, so a child cannot reach its parent's plan
or record even by accident.

This unit is a folder of several source files rather than a single class, the same deviation from the
one-file-per-unit convention that all three samples share. The unit's `sourceRef` in the SysML2 model
points at the `samples/research-assistant/` folder and its `testRef` at the sample's test project
folder.

### Data Model

- **`CommandLineOptions`** — the run configuration: corpus (required), embedding backend, provider,
  model, accumulated prompts, an optional recall question kept apart from the prompts, a delegation
  flag, and a GitHub token resolved from the command line or the environment without ever being
  printed.
- **`LexicalEmbeddingGenerator`** — the sample's offline embedding backend: it maps text to a stable,
  unit-length, order-independent vector measuring shared wording rather than shared meaning.
- **`AgentProfile`** values — the named child agents the sample registers (a reader narrowed to the
  corpus and a summarizer with no tools), each declaring the tools and grants it admits.
- **`InMemoryMemoryStore`** — the store the run files into and the recall turn reads from.

### Key Methods

- **`AgentComposition.BuildInstructions(corpusPath, notesPath, delegationEnabled)`** — builds the
  system instructions, naming both locations and their access, appending the library's published
  suggested instructions verbatim rather than transcribing them, requiring a changed-source correction
  to be a revision citing the new document, warning that recall applies no similarity floor, requiring
  a subject-only descriptor, requiring conclusions to be written to the notes location, forbidding a
  plan item that is already finished, and describing delegation truthfully in both directions.
- **`AgentComposition.BuildTools(corpusPath, notesPath, embeddings, store, delegationEnabled, runner)`**
  — composes the todo, memory, and reading tools, adding the delegation tool only when the host grant
  enables it (capability gating, not call-time refusal).
- **`AgentComposition.CreateChildPacks()`** and **`CreateProfiles(corpusPath)`** — define the
  delegation boundary: a child is composed from the reading families alone, the reader profile is
  narrowed to a single read-only corpus grant admitting only reading tools, and the summarizer profile
  legitimately declares no tools.
- **`AgentComposition.BuildRecallTools(corpusPath, embeddings, store)`** and
  **`BuildRecallInstructions()`** — compose the final recall turn: the memory family and no way to
  read anything, over the same store the earlier turns filed into, with instructions forbidding an
  answer from anything but recalled memories.
- **`AgentComposition.CreateEmbeddingGenerator(options)`** — resolves the embedding backend, using the
  offline generator for the local backend.
- **`CommandLineOptions.Parse(args)`**, **`ResolveGitHubToken()`**, **`HelpText()`**, and
  **`DescribeModel()`** — the command-line surface: it requires a corpus, rejects unknown input,
  defaults to an offline delegating run, accumulates prompts in order, resolves a credential without
  printing it, and describes the run (help text, and the concrete model the run will use or an honest
  statement that it cannot be known).

### Error Handling

A malformed command line — a missing corpus, an unknown argument, an unknown embedding backend, or a
flag left without its value — raises `CommandLineException` naming the offending flag. A blank
credential is treated as no credential rather than a failed authentication. A contradicting fact whose
descriptor collides with an existing memory is refused as a near-duplicate with the conflict named,
returned as a value rather than thrown. A request outside a granted location is refused by the policy.

### Dependencies

- **AgentKitCore** — the policy, grants, and pack builder; see _AgentKitCore System Design_.
- **AgentKitTools** — the shipped todo, memory, agent, text-file, file, and Markdown packs; see
  _AgentKitTools System Design_.
- **AgentKitAgentsChatClient** and **AgentKitAgentsCopilot** — the provider factories; see their
  system design documents.
- **Ollama** (`OllamaSharp`) and the GitHub Copilot SDK — the provider runtimes, and, with
  `--embeddings ollama`, the real embedding model that substitutes for the offline generator.

### Callers

The sample is an application entry point run by a reader; nothing in this repository calls it. Its
composition and command-line methods, and its offline embedding generator, are exercised directly by
the `DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests` project across its agent-composition,
command-line, descriptor-phrasing, and embedding-generator scenarios.
