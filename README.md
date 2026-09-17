# AgentKit

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

DEMA Consulting libraries of hardened, provider-neutral agent tools for .NET.

AgentKit provides hardened, provider-neutral agent tools: an application attaches a
permission-governed set of tools to the agent framework of its choice, bounded by a policy the
application configures and a tool cannot omit. It also gives that agent a session of its own, so a
long conversation outlives the model's context window on every provider alike.

> **Status**: Early development. The Core contract — path policy, tool limits, guarded tool
> construction, tool results, and the tool pack contract — is implemented; seven guarded tool
> families — text file, file, markdown, image, todo, memory and agent — are built on it in
> `DemaConsulting.AgentKit.Tools`; and two
> provider-adapter packages build a Microsoft Agent Framework agent from any `IChatClient` or from
> a GitHub Copilot `CopilotClient`. The provider-agnostic session engine with tiered context
> compaction ships in `DemaConsulting.AgentKit.Core` alongside the contract, and
> `DemaConsulting.AgentKit.Agents.ChatClient` supplies the provider session, session factory and
> summarizer that run it on any `IChatClient`. The GitHub Copilot adapter does not yet carry a
> provider session, so a Copilot conversation still runs on that runtime's own session.

Three runnable [samples](https://github.com/demaconsulting/AgentKit/tree/main/samples) show AgentKit
end to end: **document-assistant** demonstrates consuming the shipped tools,
**research-assistant** demonstrates the planning, memory and delegation families working together on
a self-compacting session, and **custom-tools** demonstrates writing your own guarded tools. See the
[Samples](#samples) section below.

## Capabilities

- **Guarded tool families**: each family is bound at construction to the policy, store, or
  collaborators that constrain what it may touch, and is added as a pack. The families shipping in
  `DemaConsulting.AgentKit.Tools` today are **text file** (search, read, create, replace, and
  line-range cut, copy and paste through a recoverable buffer), **file** (list, copy, move and
  delete files of any type), **markdown** (outline a document's headings with their line ranges),
  **image** (read images and PDF documents for a vision-capable agent, gated on the `Vision` host
  capability), **todo** (a flat task list the agent records steps in, updates, lists back and drops
  steps from), **memory**
  (file, recall, update, revise and forget memories, over an embedding generator the application
  supplies; supplying no store gives each composition a fresh in-memory store that does **not**
  persist beyond it), and **agent** (delegate a task to an application-defined child agent profile,
  gated on the `Delegation` host capability, and bounded by the delegation-depth limit below). The
  user guide's *Available Tools* table names every tool in
  each family; this README deliberately does not restate it.
- **Capability packs**: adapting other libraries, such as document extraction and speech,
  into guarded agent tools (planned)
- **A session that outlives the context window**: an AgentKit-owned session keeps its own transcript
  out of session, and when the provider's window fills it consolidates older history into a
  round-robin structure of tiered records, seeds a fresh provider session with them, and only then
  disposes the one it replaced. Counts, not token budgets, decide what ages out; tokens serve one
  purpose, which is noticing that the window is filling. Every turn reports its occupancy, whether
  the session rotated, how hard it is compacting, and whether compacting bought nothing and history
  had to be dropped.
- **Provider neutrality**: tools are `AIFunction` instances, so they work with Microsoft
  Agent Framework, the GitHub Copilot SDK, and any `IChatClient` implementation. Two provider-adapter
  packages turn a provider into a tool-using agent in one call: `DemaConsulting.AgentKit.Agents.ChatClient`
  for any `IChatClient` (installing faithful image delivery automatically, and supplying the provider
  session, session factory and summarizer the session engine runs on), and
  `DemaConsulting.AgentKit.Agents.Copilot` for the GitHub Copilot SDK (suppressing the runtime's
  built-in tools).

AgentKit does not provide an agent runtime or a provider abstraction. Microsoft Agent Framework
supplies those. It does provide **context-window management**, and treats it as core rather than
as an extra: the `DemaConsulting.AgentKit.Core` package ships the session engine alongside the tool
contract, so a long-running agent behaves the same way on every provider that has an AgentKit
provider session. Today that means any `IChatClient`, through
`DemaConsulting.AgentKit.Agents.ChatClient`.

## Packages

- **`DemaConsulting.AgentKit.Core`** — policy primitives, guarded tool construction, tool result
  helpers, the tool-pack contract, and the provider-agnostic session engine: a session that
  keeps its own transcript out of session and, when the context window fills, consolidates older
  history into a round-robin structure of tiered slots, creates a fresh provider session seeded with
  the preserved content, and only then disposes the one it replaced. The only compaction setting an
  application configures is how many recent turns to keep verbatim; the window comes from the
  provider session itself. When the context fills again quickly the session compacts harder and, at
  its tersest, discards its oldest consolidated slot, and it reports how hard it is working. Ships
  an in-memory provider session and factory so the whole lifecycle can be exercised without a live
  model.
- **`DemaConsulting.AgentKit.Tools`** — the ready-made guarded tool families listed under
  [Capabilities](#capabilities), each composed onto a policy through the pack contract.
- **`DemaConsulting.AgentKit.Agents.ChatClient`** — builds a Microsoft Agent Framework agent from any
  `IChatClient`, installing the image-promoting decorator on every agent so a tool-returned image
  reaches the model even on a provider that would otherwise drop it. It also carries the session
  engine's provider side for that whole family: a provider session over any `IChatClient`, the
  factory that produces one at every rotation, and a summarizer that consolidates through a client
  of the application's choosing.
- **`DemaConsulting.AgentKit.Agents.Copilot`** — builds a Microsoft Agent Framework agent from a
  GitHub Copilot `CopilotClient`, suppressing the runtime's built-in tools by deriving the session
  allow-list from the supplied tools. It carries no provider session yet, so a Copilot conversation
  runs on the runtime's own session rather than on an AgentKit compacting one.

Additional provider and tool packages will be added as the architecture is implemented.

## Engineering Practices

- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Multi-Runtime Support**: Targets .NET 8, 9, and 10
- **xUnit v3**: Modern unit testing with xUnit framework version 3
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks and builds
- **Linting Enforcement**: markdownlint, cspell, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Installation

Install the libraries using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
```

Add the adapter for the provider you target:

```bash
dotnet add package DemaConsulting.AgentKit.Agents.ChatClient   # any IChatClient provider
dotnet add package DemaConsulting.AgentKit.Agents.Copilot      # the GitHub Copilot SDK
```

## API Documentation

Detailed API documentation for all public types and members is distributed in the `api/` folder
of the NuGet package.

It is generated from the XML doc comments by the build, and organized for gradual disclosure — an
index, then a page per namespace, type, and member — so that a coding agent working against
AgentKit can read the one page it needs rather than the whole reference.

## Usage

`DemaConsulting.AgentKit.Core` provides the contract that other AgentKit packages — and
an application's own tools — are built against, plus the session engine that keeps a long
conversation alive:

- **Path policy**: one required working directory that a relative path is anchored to (and nothing
  else — it carries no permission), plus zero or more access grants, each unrestricted or confined
  to a location and each carrying an access level (read-only or read-write) and its own denied
  patterns, with all containment decisions made on the normalized absolute location a path
  denotes
- **Tool limits**: ceilings on bytes read, result size returned to the model, binary content
  returned, attachments per turn, and how deep a chain of delegated agents may run (two levels
  beneath the root agent by default; zero forbids delegation entirely), carried with the policy so
  every tool observes the same budget
- **Guarded tool construction**: the only supported way to build a tool, so the safety conventions
  cannot be forgotten
- **Tool results**: text, structured data, binary, and image results, and refusals that carry a
  reason
- **Tool pack contract**: composition of packs into the tool list an application offers a model,
  gated on host capability
- **Session engine**: an agent session that compacts its own context, so a conversation outlives the
  provider's window. See [Sessions](#sessions) below.

`DemaConsulting.AgentKit.Tools` ships ready-made guarded tool families built on this contract. An
application composes a policy, adds the packs it wants, declares what its host supports, and
receives the tool list to hand to its agent framework of choice:

```csharp
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);

IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .WithHostCapabilities(HostCapabilities.Vision)
    .Add(new TextFilePack())
    .Add(new ImagePack())
    .Build();

// Hand `tools` to ChatOptions.Tools, an IChatClient, or Microsoft Agent Framework.
```

A `PathPolicy` separates two orthogonal ideas. The **working directory** is the single location a
relative path is anchored to, and nothing else — it carries no permission of its own. A **grant**
is a permitted location carrying an access level, and nothing else — it says a location may be read,
or read and written, but says nothing about addressing. Here one folder plays both roles: it is the
anchor, and it is granted read-write. The anchor matters because a model asks for `notes.txt`, not
for its absolute location — a policy that measured that name from wherever the host process was
started would refuse every legitimate request. Absolute paths remain expressible and remain subject
to the same containment decision, and a request naming no path at all means the working directory
itself. Add more grants (each read-only or read-write) when an agent needs several locations, and
grant the working directory whatever access it should have — it receives none implicitly.

> **Transition hazard.** With a single granted working directory, tool output uses the relative
> dialect: a listing reports bare relative names and the model imitates them. Adding a second granted
> location moves paths under it to the absolute dialect (they are reported under an absolute header),
> and nothing else warns you the switch happened.

The policy is a guardrail, not a sandbox: a tool cannot express an operation the policy forbids,
but AgentKit does not replace OS-level isolation for untrusted code. Symbolic links, directory
junctions and other reparse points are not a protection boundary: a path that reaches outside a
granted location through a link is not detected. Because the image family
requires the `Vision` host capability, `ImagePack` contributes its tool only when the host
declares that capability; a host that does not is never offered `image_read`.

Providers differ in where they accept images. Some deliver an image a tool returned straight to
the model; others accept images only on messages and silently discard one that arrives in a tool
response, after which the model describes a picture it never received. A host targeting such a
provider wraps its chat client in `ImagePromotingChatClient`, beneath the function-invocation loop,
and the image is carried onto a user message instead.

## Sessions

A conversation eventually fills the provider's context window, and what happens next is the
provider's decision unless the application takes it. AgentKit takes it. `CompactingAgentSession`
keeps its own transcript out of session; when the conversation approaches the window it consolidates
older history into tiered records, creates a fresh provider session seeded with them, and only then
disposes the one it replaced.

An application states three things: a summarizer, a provider-session factory, and how much recent
history to keep word for word.

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;

// The window is a fact about the provider, so the provider side answers for it. Read it from the
// provider where you can — Ollama publishes the loaded model's context length — or state the
// window of the model you chose.
var providerSessions = new ChatClientProviderSessionFactory(chatClient, windowTokens: 32768);

// Consolidation runs outside the conversation it compacts, on a client of its own: sending it
// through the live session would spend the very context it exists to reclaim.
var summarizer = new ChatClientSummarizer(summaryChatClient);

var options = new AgentSessionOptions(
    summarizer,
    instructions: "You are a research assistant confined to the permitted locations.",
    tools: tools);

await using var session = await CompactingAgentSession.CreateAsync(options, providerSessions);

var turn = await session.SendAsync("Review every document in the corpus.");
Console.WriteLine(turn.Text);

if (turn.RotationOccurred)
{
    Console.WriteLine($"Rotated at level {turn.Level}; {session.RotationCount} so far.");
}

if (turn.MaterialDropped)
{
    // Compacting bought nothing: history had to be discarded to make room.
    Console.WriteLine("History was dropped outright.");
}
```

The structure is a round-robin database in **counts, not tokens**: a verbatim tail of recent turns,
behind it three tiers of at most four consolidated records each, and an oldest record binned when
the top tier is full and another arrives. Tokens serve exactly one purpose — noticing that the
window is filling — and the provider session answers for that, so the engine asks one question, how
full out of how much, and believes the answer. Nothing weighs a record against a token budget.

`InMemoryProviderSession` and `InMemoryProviderSessionFactory` ship in Core so the whole lifecycle
can be exercised without a live model, which is what lets an application test its own summarizer,
its verbatim tail length, and what it does with a reported rotation.

`DemaConsulting.AgentKit.Agents.ChatClient` supplies the provider side for any `IChatClient`:
`ChatClientProviderSession`, `ChatClientProviderSessionFactory` and `ChatClientSummarizer`. The
GitHub Copilot adapter does not carry one yet, so a Copilot conversation runs on that runtime's own
session.

## Documentation

Generated documentation includes:

- **Build Notes**: Release information and changes
- **User Guide**: Comprehensive usage documentation
- **API Reference**: Compact, gradually-disclosed Markdown API documentation generated from the XML
  doc comments and shipped inside each NuGet package, written for a coding agent to read: an index,
  then a page per namespace, type, and member. Every `<example>` it contains is compiled against the
  real API by the build, so a documented example cannot describe an API the code does not have.
- **Code Quality Report**: CodeQL and SonarCloud analysis results
- **Requirements**: Functional and non-functional requirements
- **Requirements Justifications**: Detailed requirement rationale
- **Trace Matrix**: Requirements to test traceability

## Samples

Runnable samples live under [`samples/`](https://github.com/demaconsulting/AgentKit/tree/main/samples).
See [`samples/README.md`](https://github.com/demaconsulting/AgentKit/blob/main/samples/README.md) for an index
of what each demonstrates and when to read it.

- **[Document Assistant](https://github.com/demaconsulting/AgentKit/tree/main/samples/document-assistant)**
  — *the consumption path.* A console chat application that grants an agent two locations — a
  workspace folder to read and a separate session folder to write artifacts into — and gives it the
  shipped text-file, file, Markdown, and image tool packs. A `--read-only-workspace` switch makes the grants
  asymmetric, so a refused write enumerates the writable location and the agent recovers. It runs
  unchanged against the GitHub Copilot runtime and any Ollama model, and prints every tool call so
  the containment, capability gating, and built-in suppression are visible as they happen.
- **[Research Assistant](https://github.com/demaconsulting/AgentKit/tree/main/samples/research-assistant)**
  — *the agent-infrastructure path, and the session engine's first user.* A console application
  composing the `todo`, `memory`, and
  `agent` families onto one policy: it plans its work as a task list, files what it learns as
  searchable memories with the document each came from, and delegates the reading of a single
  document to a child agent. On `--provider ollama` the conversation runs on a
  `CompactingAgentSession` built from `ChatClientProviderSessionFactory` and `ChatClientSummarizer`,
  reading the context window from the Ollama server rather than assuming one, and printing what each
  turn occupies, whether it rotated, how hard it is compacting, and whether history had to be
  dropped. The memory store lives outside the session, so what the agent filed survives every
  rotation. Its corpus is granted read-only and contains a superseding revision, and
  in the eight live runs measured (`claude-sonnet-5`, `--embeddings local`) the agent noticed the
  contradiction by reading and corrected the memory in place with `memory_revise` in 5 of 5 of the
  neutral runs; the near-duplicate refusal — the backstop for a conflict the model has *not*
  noticed — fired in 0 of the 8. Those live figures come from an opt-in workflow run on request and
  on a weekly schedule, never as part of the pull-request merge gate, so they are re-measured
  deliberately rather than continuously and can drift as models change. It
  supplies its own offline embedding generator — `MemoryPack` requires one and never inspects it —
  so it runs from a fresh clone with no server, no credential, and no model binary in the
  repository; `--embeddings ollama` swaps in a real model and changes nothing else. The packs a
  delegated agent may draw on are listed explicitly and exclude the task list, the memory store and
  the agent family itself, so a child agent cannot reach its parent's plan or record, nor delegate
  further.
- **[Custom Tools](https://github.com/demaconsulting/AgentKit/tree/main/samples/custom-tools)**
  — *the extension path.* A console chat application that shows how an application author writes
  their own guarded tools with `GuardedToolFactory` and publishes them as packs, composed alongside
  a shipped pack. It ships a path-taking `docstats_wordcount` tool (going through `PathPolicy` for
  containment and returning a structured result of a text file's word, line, and character counts)
  and a no-path `clock_now` tool (the deliberate contrast — every tool is built through the guarded
  factory, not only path-based ones). Its `docstats` family prefix is deliberately one the shipped
  library does not publish — the library now owns the `markdown` prefix — so the custom pack never
  collides with a built-in family.

Each sample has its own README explaining how to run it and what to try.

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Badge References -->
[badge-forks]: https://img.shields.io/github/forks/demaconsulting/AgentKit?style=plastic
[badge-stars]: https://img.shields.io/github/stars/demaconsulting/AgentKit?style=plastic
[badge-contributors]: https://img.shields.io/github/contributors/demaconsulting/AgentKit?style=plastic
[badge-license]: https://img.shields.io/github/license/demaconsulting/AgentKit?style=plastic
[badge-build]: https://img.shields.io/github/actions/workflow/status/demaconsulting/AgentKit/build_on_push.yaml?style=plastic
[badge-quality]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentKit&metric=alert_status
[badge-security]: https://sonarcloud.io/api/project_badges/measure?project=demaconsulting_AgentKit&metric=security_rating
[badge-nuget]: https://img.shields.io/nuget/v/DemaConsulting.AgentKit.Core?style=plastic

<!-- Link References -->
[link-forks]: https://github.com/demaconsulting/AgentKit/network/members
[link-stars]: https://github.com/demaconsulting/AgentKit/stargazers
[link-contributors]: https://github.com/demaconsulting/AgentKit/graphs/contributors
[link-license]: https://github.com/demaconsulting/AgentKit/blob/main/LICENSE
[link-build]: https://github.com/demaconsulting/AgentKit/actions/workflows/build_on_push.yaml
[link-quality]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentKit
[link-security]: https://sonarcloud.io/dashboard?id=demaconsulting_AgentKit
[link-nuget]: https://www.nuget.org/packages/DemaConsulting.AgentKit.Core
[link-continuous-compliance]: https://github.com/demaconsulting/ContinuousCompliance
[link-contributing]: https://github.com/demaconsulting/AgentKit/blob/main/CONTRIBUTING.md
