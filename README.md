# AgentKit

[![GitHub forks][badge-forks]][link-forks]
[![GitHub stars][badge-stars]][link-stars]
[![GitHub contributors][badge-contributors]][link-contributors]
[![License][badge-license]][link-license]
[![Build][badge-build]][link-build]
[![Quality Gate][badge-quality]][link-quality]
[![Security][badge-security]][link-security]
[![NuGet][badge-nuget]][link-nuget]

![AgentKit banner: the AgentKit name with the tagline 'Every tool, guarded. Every chat, compacted.' and the C# call 'new ToolPackBuilder(policy).Add(new FilePack()).Build()'. A model's tool calls such as text_file_read on notes.md, the application's read-write and read-only path grants, tool limits such as a 64 KB read ceiling, and long chats flow on curved arrows into the AgentKit .NET library, and arrows lead out to what it produces: confined results where a permitted read returns and a path outside every grant is refused, a guarded tool list, a session compacted into a verbatim tail and three tiers of consolidated records, and agents on any provider such as an IChatClient or GitHub Copilot. A footnote reads 'Unsafe calls cannot be expressed.'][hero]

**Safe agent tools for .NET.** AgentKit is a family of DEMA Consulting libraries that give an AI
agent hardened, provider-neutral tools. An application attaches a permission-governed set of tools
to the agent framework of its choice, bounded by a policy the application configures and a tool
cannot omit, and gives that agent a session of its own, so a long conversation outlives the model's
context window on every provider alike.

## Why AgentKit

- **Safe by construction**: an unsafe operation is not refused at call time; it cannot be
  expressed. Every tool is built through one guarded factory and bound at construction to the path
  policy and limits that constrain it.
- **You decide what an agent gets**: the application chooses which tools an agent receives, which
  locations it may read or write, and the ceilings every tool observes. AgentKit makes each tool
  behave correctly and predictably within those controls.
- **Provider-neutral**: tools are plain `AIFunction` instances, so they work with Microsoft Agent
  Framework, the GitHub Copilot SDK, and any `IChatClient` implementation.
- **A session that outlives the context window**: an AgentKit-owned session compacts its own
  history into tiered records and rotates to a fresh provider session when the window fills, so a
  long-running agent behaves the same way on every provider.

> **Status**: Early development. The Core contract (path policy, tool limits, guarded tool
> construction, tool results, and the tool pack contract) is implemented, and seven guarded tool
> families are built on it in `DemaConsulting.AgentKit.Tools`. The provider-agnostic session engine
> with tiered context compaction ships in `DemaConsulting.AgentKit.Core` alongside the contract.
> Two provider-adapter packages build a Microsoft Agent Framework agent, and supply the provider
> session, session factory and summarizer the session engine runs on:
> `DemaConsulting.AgentKit.Agents.ChatClient` for any `IChatClient`, and
> `DemaConsulting.AgentKit.Agents.Copilot` natively for the GitHub Copilot runtime, which exposes no
> `IChatClient` at all and reports its own context window rather than being told one.

## Quick Start

### Installation

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

On Ollama, add one more so the session is told the window the instance is actually running rather
than a guess:

```bash
dotnet add package DemaConsulting.AgentKit.Agents.Ollama       # asks for and reads Ollama's window
```

### A First Agent

Decide what the agent may touch, decide which tools it receives, and hand both to a provider:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.TextFile;
using Microsoft.Extensions.AI;

// What the agent may touch: relative paths anchor to /workspace, which is granted read-write.
var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);

// Which tools it receives: two of the shipped families, composed onto that policy.
IList<AIFunction> tools =
[
    .. new ToolPackBuilder(policy)
        .Add(new TextFilePack())
        .Add(new FilePack())
        .Build()
];

// A Microsoft Agent Framework agent over any IChatClient (OpenAI, Azure OpenAI, Ollama, ...).
var agent = ChatClientAgentFactory.Create(
    chatClient,
    tools,
    instructions: "You are a document assistant. Use the tools provided.");

var response = await agent.RunAsync("List the files in the workspace and summarize the largest one.");
Console.WriteLine(response.Text);
```

Every tool in that list observes the same policy: a request for a path outside `/workspace` is
refused with a reason the model can act on, never carried out. *Building an Agent* in the user
guide shows the same composition on the GitHub Copilot SDK, and the [Sessions](#sessions) section
below shows how to keep a long conversation alive.

### API Documentation

Detailed API documentation for all public types and members is distributed in the `api/` folder
of the NuGet package.

It is generated from the XML doc comments by the build, and organized for gradual disclosure (an
index, then a page per namespace, type, and member) so that a coding agent working against
AgentKit can read the one page it needs rather than the whole reference.

## Architecture

The picture below shows the AgentKit packages, what each builds on, and where the application's
controls are set and enforced. Read it from the top: the application adds tool packs and builds an
agent or session through an adapter; every tool is built through the Core safety contract, and
every adapter feeds the Core session engine.

![AgentKit architecture: your application adds AgentKit.Tools packs, which are built through the AgentKit.Core tool safety contract that enforces its controls, and builds an agent or session through an AgentKit.Agents adapter, which feeds the Core session lifecycle and builds on Microsoft Agent Framework, any IChatClient provider, the GitHub Copilot SDK, or an Ollama server, none of which is part of AgentKit][img-architecture]

- **Your application** holds every control: which packs an agent receives, the `PathPolicy`
  grants and `ToolLimits` those tools obey, the host capabilities that gate the image and agent
  families, and how many recent turns a session keeps word for word.
- **`AgentKit.Core`** enforces those controls and runs the session. Its tool safety contract is
  the only supported way to build a tool, and its session engine is provider-agnostic.
- **`AgentKit.Tools`** ships the ready-made tool families, each built on the Core contract.
- **`AgentKit.Agents.*`** adapt a provider: each builds a Microsoft Agent Framework agent and
  supplies the provider side the session engine runs on.

AgentKit does not provide an agent runtime or a provider abstraction. Microsoft Agent Framework
supplies those. It does provide **context-window management**, and treats it as core rather than
as an extra: the `DemaConsulting.AgentKit.Core` package ships the session engine alongside the tool
contract, so a long-running agent behaves the same way on every provider that has an AgentKit
provider session. Today that means any `IChatClient`, through
`DemaConsulting.AgentKit.Agents.ChatClient`, and the GitHub Copilot runtime, through
`DemaConsulting.AgentKit.Agents.Copilot`.

## Packages

Every package name starts with `DemaConsulting.AgentKit.`:

| Package | What it adds | Add it when |
| --- | --- | --- |
| `Core` | Safety contract, tool-pack contract, session engine | Always |
| `Tools` | Seven ready-made guarded tool families | You want the shipped tools |
| `Agents.ChatClient` | Agent, provider session and summarizer for any `IChatClient` | You target an `IChatClient` |
| `Agents.Copilot` | Agent, provider session and summarizer for GitHub Copilot | You target the Copilot SDK |
| `Agents.Ollama` | The context window an Ollama instance is really running | You target Ollama |

Additional provider and tool packages will be added as the architecture is implemented.

### Core

Policy primitives, guarded tool construction, tool result helpers, the tool-pack contract, and the
provider-agnostic session engine: a session that keeps its own transcript out of session and, when
the context window fills, consolidates older history into a round-robin structure of tiered slots,
creates a fresh provider session seeded with the preserved content, and only then disposes the one
it replaced. The only compaction setting an application configures is how many recent turns to
keep verbatim; the window comes from the provider session itself. When the context fills again
quickly the session compacts harder and, at its tersest, discards its oldest consolidated slot, and
it reports how hard it is working. Ships an in-memory provider session and factory so the whole
lifecycle can be exercised without a live model.

### Tools

The ready-made guarded tool families listed under [Tool Families](#tool-families), each composed
onto a policy through the pack contract.

### Agents.ChatClient

Builds a Microsoft Agent Framework agent from any `IChatClient`, installing the image-promoting
decorator on every agent so a tool-returned image reaches the model even on a provider that would
otherwise drop it. It also carries the session engine's provider side for that whole family: a
provider session over any `IChatClient`, the factory that produces one at every rotation, and a
summarizer that consolidates through a client of the application's choosing.

### Agents.Copilot

Builds a Microsoft Agent Framework agent from a GitHub Copilot `CopilotClient`, suppressing the
runtime's built-in tools by deriving the session allow-list from the supplied tools. It also
carries the session engine's provider side for the Copilot runtime: a provider session over one
Copilot session, the factory that produces one at every rotation, and a summarizer that
consolidates on a separate tool-free session. Copilot reports its own occupancy and its own context
window, so unlike every other adapter this one is never told a window. Because Copilot compacts its
own sessions by default, at a threshold barely above the one AgentKit rotates at, every session
AgentKit drives is created with the runtime's compaction threshold raised clear of that rotation
point so the two compactors never act on one conversation.

### Agents.Ollama

Makes the context window your session accounts against the window the Ollama instance is actually
running. It asks the server to run at a size you chose, on every request, and where you chose none
it reports the length the running instance says it is using, falling back to a conservative
default, named as assumed, when nothing is loaded. What the model file publishes as its maximum is
deliberately never used: on a live server that figure was 262,144 for a model running at 4,096.
Ollama is otherwise an ordinary `IChatClient` provider, and this is the one thing the generic
adapter cannot supply: an `IChatClient` publishes no window, and getting it wrong in the high
direction loses conversation history silently. Optional: add it only if you target Ollama, and it
carries the `OllamaSharp` dependency the `IChatClient` adapter deliberately does not.

## Tool Families

`DemaConsulting.AgentKit.Tools` ships seven guarded tool families. Each family is bound at
construction to the policy, store, or collaborators that constrain what it may touch, and is added
as a pack. The user guide's *Available Tools* table names every tool in each family; this README
deliberately does not restate it.

| Family | Pack | What it gives an agent | Requires |
| --- | --- | --- | --- |
| Text file | `TextFilePack` | Search, read, create, overwrite and edit text files | Write tools need a write grant |
| File | `FilePack` | List, copy, move and delete files and directories | Write tools need a write grant |
| Markdown | `MarkdownPack` | The heading outline of a document, with line ranges | Nothing |
| Image | `ImagePack` | Read images and PDF documents, crop and trim images | `Vision` host capability |
| Todo | `TodoPack` | A flat task list the agent plans its steps in | Nothing |
| Memory | `MemoryPack` | File, recall, update, revise and forget memories | An embedding generator |
| Agent | `AgentPack` | Delegate a task to an application-defined child agent | `Delegation` host capability |

What each family does, in more detail:

- **Text file**: search, read, create, set a file's whole content, replace, and line-range cut,
  copy and paste through a recoverable buffer.
- **File**: list, copy, move and delete files of any type, and create, move or rename, and
  recursively delete the directories that hold them. A recursive delete never follows a link out of
  the directory it was given, refuses a path it is asked to reach through such a link, and refuses a
  tree larger than the configured entry ceiling.
- **Markdown**: outline a document's headings with their line ranges.
- **Image**: read images and PDF documents for a vision-capable agent, reporting an image's pixel
  dimensions; return a rectangular region of a PNG or JPEG named in those pixels; and trim one to
  the region its own content occupies when no region can be named, inline or written as a new PNG
  where a read-write grant permits it. Gated on the `Vision` host capability.
- **Todo**: a flat task list the agent records steps in, updates, lists back and drops steps from.
- **Memory**: file, recall, update, revise and forget memories, over an embedding generator the
  application supplies. Supplying no store gives each composition a fresh in-memory store that does
  **not** persist beyond it.
- **Agent**: delegate a task to an application-defined child agent profile, gated on the
  `Delegation` host capability, and bounded by the delegation-depth limit under
  [Safety Model](#safety-model).

**Capability packs** that adapt other libraries, such as document extraction and speech, into
guarded agent tools are planned.

## Safety Model

`DemaConsulting.AgentKit.Core` provides the contract that other AgentKit packages, and an
application's own tools, are built against, plus the session engine that keeps a long conversation
alive:

- **Path policy**: one required working directory that a relative path is anchored to (and nothing
  else: it carries no permission), plus zero or more access grants, each unrestricted or confined
  to a location and each carrying an access level (read-only or read-write) and its own denied
  patterns, with all containment decisions made on the normalized absolute location a path
  denotes
- **Tool limits**: ceilings on bytes read, result size returned to the model, binary content
  returned, and how deep a chain of delegated agents may run (two levels beneath the root agent by
  default; zero forbids delegation entirely), carried with the policy so every tool observes the
  same budget
- **Guarded tool construction**: the only supported way to build a tool, so the safety conventions
  cannot be forgotten
- **Tool results**: text, structured data, binary, and image results, and refusals that carry a
  reason
- **Tool pack contract**: composition of packs into the tool list an application offers a model,
  gated on host capability and narrowed by the access policy; a pack withholds the tools whose
  writes the policy governs when no grant permits writing anywhere
- **Session engine**: an agent session that compacts its own context, so a conversation outlives the
  provider's window. See [Sessions](#sessions) below.

### Composing a Tool List

An application composes a policy, adds the packs it wants, declares what its host supports, and
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

### Working Directory and Grants

A `PathPolicy` separates two orthogonal ideas. The **working directory** is the single location a
relative path is anchored to, and nothing else; it carries no permission of its own. A **grant**
is a permitted location carrying an access level, and nothing else; it says a location may be read,
or read and written, but says nothing about addressing. Here one folder plays both roles: it is the
anchor, and it is granted read-write. The anchor matters because a model asks for `notes.txt`, not
for its absolute location; a policy that measured that name from wherever the host process was
started would refuse every legitimate request. Absolute paths remain expressible and remain subject
to the same containment decision, and a request naming no path at all means the working directory
itself. Add more grants (each read-only or read-write) when an agent needs several locations, and
grant the working directory whatever access it should have; it receives none implicitly.

> **Transition hazard.** With a single granted working directory, tool output uses the relative
> dialect: a listing reports bare relative names and the model imitates them. Adding a second granted
> location moves paths under it to the absolute dialect (they are reported under an absolute header),
> and nothing else warns you the switch happened.

### A Guardrail, Not a Sandbox

The policy is a guardrail, not a sandbox: a tool cannot express an operation the policy forbids,
but AgentKit does not replace OS-level isolation for untrusted code. Symbolic links, directory
junctions and other reparse points are not a general protection boundary: path resolution is
lexical, so a path that reaches outside a granted location through a link is not detected.

The two exceptions are the destructive directory tools: `file_delete_directory` and
`file_move_directory` refuse a path they are asked to reach *through* a link, because there the
content at stake is a whole tree rather than a single entry. That refusal is a **pre-flight check
over paths**: the path a caller names is classified, and the recursive delete additionally refuses
any link its walk discovers, both before anything is touched. It is not a race-resistant control.
A process able to write inside a granted location can replace a component after the check, and no
path-based API can close that window, though no removal these tools issue follows a link, so what a
race costs is bounded by a single entry rather than a tree.

`file_create_directory` judges every directory it would create, not only the one named, so a
request whose missing parents would reach above every grant is refused with nothing created. The
two destructive directory tools apply the same idea to the other end of the operation: they judge
every entry the operation would actually **touch**, not only the path they were given. A grant's
denied patterns exclude names *inside* a permitted location, so a tree holding an excluded entry is
refused whole: nothing removed, nothing moved, and never the permitted subset instead.

### Which Tools Exist

The policy also decides which tools exist at all, in one specific way: a pack withholds the tools
whose **writes** the policy governs, and only when no grant permits writing anywhere: the five
editing tools of the text file family and the six management tools of the file family. The question
is asked of the whole policy, so a read-only workspace paired with a read-write session folder still
publishes them all.

Read tools are not gated the same way, so a policy holding **no grants at all** still publishes
`text_file_read`, `text_file_search`, `text_file_copy_lines` and `image_read`, and every one of
them refuses every path it is given. That case is left alone deliberately: a grant is either
read-only or read-write, so write access always implies read access, and read tools are therefore
unusable only when nothing whatever is granted, a configuration that yields a non-functional agent
however its tool list is trimmed.

Because the image family requires the `Vision` host capability, `ImagePack` contributes its tools
only when the host declares that capability; a host that does not is never offered `image_read`,
`image_crop` or `image_auto_crop`.

### Delivering Images to Any Provider

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

### Running a Session

An application states three things: a summarizer, a provider-session factory, and how much recent
history to keep word for word.

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;

// The window is a fact about the provider, so the provider side answers for it. Read it from the
// provider where you can (Ollama reports the length the running instance is using) or state the
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

Every turn reports its occupancy, whether the session rotated, how hard it is compacting, and
whether compacting bought nothing and history had to be dropped.

### Counts, Not Tokens

The structure is a round-robin database in **counts, not tokens**: a verbatim tail of recent turns,
behind it three tiers of at most four consolidated records each, and an oldest record binned when
the top tier is full and another arrives. Tokens serve exactly one purpose, which is noticing that
the window is filling, and the provider session answers for that, so the engine asks one question,
how full out of how much, and believes the answer. Nothing weighs a record against a token budget.

`InMemoryProviderSession` and `InMemoryProviderSessionFactory` ship in Core so the whole lifecycle
can be exercised without a live model, which is what lets an application test its own summarizer,
its verbatim tail length, and what it does with a reported rotation.

### Provider Sides

`DemaConsulting.AgentKit.Agents.ChatClient` supplies the provider side for any `IChatClient`:
`ChatClientProviderSession`, `ChatClientProviderSessionFactory` and `ChatClientSummarizer`.

`DemaConsulting.AgentKit.Agents.Copilot` supplies the same three for the GitHub Copilot runtime
(`CopilotProviderSession`, `CopilotProviderSessionFactory` and `CopilotSummarizer`) natively,
because the Copilot SDK exposes no `IChatClient` to adapt. Copilot reports both its occupancy and
its limit, so that adapter is never told a window.

### The Window on Ollama

On Ollama, `DemaConsulting.AgentKit.Agents.Ollama` answers the one question the generic adapter
cannot: what window the instance is actually running.

```csharp
using DemaConsulting.AgentKit.Agents.Ollama;
using Microsoft.Extensions.AI;

var window = await OllamaContextWindow.ReadAsync(ollamaClient, model, stated: null, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollamaClient, window.Tokens);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens);
```

`window.Source` says which figure it is: the loaded instance's length, which is what the server
enforces, or a conservative default named as assumed. Report it rather than hiding it: an
assumption shown as a measurement is how a session ends up sized on something nothing guarantees.

Compose the decorator whichever figure you got. A window that was only *read* is no more durable
than one that was only claimed, because Ollama does not remember the length an instance was loaded
at. Asking for the figure makes it true.

To choose the size rather than discover it, state it to the same call; nothing else changes:

```csharp
const int windowTokens = 32768;

var window = await OllamaContextWindow.ReadAsync(ollamaClient, model, windowTokens, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollamaClient, window.Tokens);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens);
```

The size rides on *every* request, not only the first, and a summarizer's client needs wrapping too
whichever model it runs on. `OllamaContextSizingChatClient`'s API reference, shipped in the package,
gives the reasoning for both.

## Samples

Three runnable samples show AgentKit end to end. They live under
[`samples/`](https://github.com/demaconsulting/AgentKit/tree/main/samples); see
[`samples/README.md`](https://github.com/demaconsulting/AgentKit/blob/main/samples/README.md) for an
index of what each demonstrates and when to read it.

- **[Document Assistant][link-sample-document]**: *the consumption path.* A console chat
  application that grants an agent two locations, a workspace folder to read and a separate session
  folder to write artifacts into, and gives it the shipped text-file, file, Markdown, and image tool
  packs. A `--read-only-workspace` switch makes the grants asymmetric, so a refused write enumerates
  the writable location and the agent recovers. The same asymmetry is what makes the
  figure-preparation loop visible: one `image_crop` call reads a workspace image under the read
  grant and writes the region it cut into the session folder under the write grant, so the agent can
  reference that produced file from the document it then writes. It runs unchanged against the
  GitHub Copilot runtime and any Ollama model, and prints every tool call so the containment,
  capability gating, and built-in suppression are visible as they happen.
- **[Research Assistant][link-sample-research]**: *the agent-infrastructure path, and the session
  engine's first user.* A console application composing the `todo`, `memory`, and `agent` families
  onto one policy: it plans its work as a task list, files what it learns as searchable memories
  with the document each came from, and delegates the reading of a single document to a child
  agent. On **either provider** the conversation runs on a `CompactingAgentSession`:
  `ChatClientProviderSessionFactory` and `ChatClientSummarizer` on `--provider ollama`, reading the
  context window from the Ollama server rather than assuming one (or, with `--context-window`,
  asking the server to run at a size of your choosing); `CopilotProviderSessionFactory` and
  `CopilotSummarizer` on `--provider copilot`, which reports its own window so none is stated.
  Either way it prints what each turn occupies, whether it rotated, how hard it is compacting, and
  whether history had to be dropped. The memory store lives outside the session, so what the agent
  filed survives every rotation. Its corpus is granted read-only and contains a superseding
  revision, and in the eight live runs measured (`claude-sonnet-5`, `--embeddings local`) the agent
  noticed the contradiction by reading and corrected the memory in place with `memory_revise` in 5
  of 5 of the neutral runs; the near-duplicate refusal, the backstop for a conflict the model has
  *not* noticed, fired in 0 of the 8. Those live figures come from an opt-in workflow run on request
  and on a weekly schedule, never as part of the pull-request merge gate, so they are re-measured
  deliberately rather than continuously and can drift as models change. It supplies its own offline
  embedding generator (`MemoryPack` requires one and never inspects it), so it runs from a fresh
  clone with no server, no credential, and no model binary in the repository; `--embeddings ollama`
  swaps in a real model and changes nothing else. The packs a delegated agent may draw on are listed
  explicitly and exclude the task list, the memory store and the agent family itself, so a child
  agent cannot reach its parent's plan or record, nor delegate further.
- **[Custom Tools][link-sample-custom]**: *the extension path.* A console chat application that
  shows how an application author writes their own guarded tools with `GuardedToolFactory` and
  publishes them as packs, composed alongside a shipped pack. It ships a path-taking
  `docstats_wordcount` tool (going through `PathPolicy` for containment and returning a structured
  result of a text file's word, line, and character counts) and a no-path `clock_now` tool (the
  deliberate contrast: every tool is built through the guarded factory, not only path-based ones).
  Its `docstats` family prefix is deliberately one the shipped library does not publish (the
  library now owns the `markdown` prefix), so the custom pack never collides with a built-in family.

Each sample has its own README explaining how to run it and what to try.

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

## Engineering Practices

- **Multi-Platform Support**: Builds and runs on Windows, Linux, and macOS
- **Multi-Runtime Support**: Targets .NET 8, 9, and 10
- **xUnit v3**: Modern unit testing with xUnit framework version 3
- **Comprehensive CI/CD**: GitHub Actions workflows with quality checks and builds
- **Linting Enforcement**: LintMark, TextLint, and yamllint enforced on every CI run
- **Continuous Compliance**: Compliance evidence generated automatically on every CI run, following
  the [Continuous Compliance][link-continuous-compliance] methodology
- **SonarCloud Integration**: Quality gate and security analysis on every build
- **Documentation Generation**: Automated build notes, user guide, code quality reports,
  requirements, justifications, and trace matrix
- **Requirements Traceability**: Requirements linked to passing tests with auto-generated trace matrix

## Contributing

Contributions are welcome. See [CONTRIBUTING.md][link-contributing] for development setup, coding
standards, and the pull request process.

## License

Copyright (c) DEMA Consulting. Licensed under the MIT License. See [LICENSE][link-license] for details.

By contributing to this project, you agree that your contributions will be licensed under the MIT License.

<!-- Image References: absolute URLs, because this README is also the NuGet package readme -->
[hero]: https://raw.githubusercontent.com/demaconsulting/AgentKit/main/docs/images/hero.png
[img-architecture]: https://raw.githubusercontent.com/demaconsulting/AgentKit/main/docs/images/architecture.png

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
[link-sample-document]: https://github.com/demaconsulting/AgentKit/tree/main/samples/document-assistant
[link-sample-research]: https://github.com/demaconsulting/AgentKit/tree/main/samples/research-assistant
[link-sample-custom]: https://github.com/demaconsulting/AgentKit/tree/main/samples/custom-tools
