# Introduction

## Purpose

This document is the user guide for AgentKit, a family of .NET libraries providing guarded,
provider-neutral agent tools. It exists so that a developer can hand an AI agent a set of
capabilities confined to what the application grants: every tool is guarded by the application's
path grants and limits, and a call that names a path outside those grants is refused with a reason
the model can act on.

## Scope

This user guide covers:

- Installation of the library
- What the library provides today, and how each part is used
- The path policy, tool limits, naming, result and tool pack contracts
- Opt-in retention of tool outputs too large to return, paged through by reference
- Using the tools and their controls in an agent runtime the application builds itself
- The session engine: how a conversation outlives the provider's context window
- The image and diagram tools in `DemaConsulting.AgentKit.Tools.Visual`, and migrating to that
  package from the image family's earlier home
- Three runnable samples: consuming the shipped tools (*document assistant*), an agent whose work
  spans turns and compacts its own context (*research assistant*), and writing your own guarded
  tools (*custom tools*)

# Continuous Compliance

AgentKit follows the
[Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance) methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: LintMark, TextLint, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated build notes, this user guide,
  requirements, justifications, trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build
- **Multi-Platform and Multi-Runtime**: The libraries build and run on Windows, Linux, and macOS,
  and target .NET 8, 9, and 10
- **Automated Testing**: xUnit v3 tests run on every platform and runtime in the GitHub Actions
  workflows that gate every change

# Installation

Install the libraries using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
dotnet add package DemaConsulting.AgentKit.Tools.Visual   # optional: the image and diagram tools
```

The image and diagram families ship in their own package, `DemaConsulting.AgentKit.Tools.Visual`,
because they need an imaging library; an application that attaches neither leaves it out, and
`DemaConsulting.AgentKit.Tools` then brings no imaging dependency. An application that used the
image family from `DemaConsulting.AgentKit.Tools` should read *Migrating from
`DemaConsulting.AgentKit.Tools`* in the *Images, Documents and Diagrams* chapter.

To build an agent from a provider, add the adapter for the provider you target:

```bash
dotnet add package DemaConsulting.AgentKit.Agents.ChatClient   # any IChatClient provider
dotnet add package DemaConsulting.AgentKit.Agents.Copilot      # the GitHub Copilot runtime
```

An application on the GitHub Copilot runtime needs the GitHub Copilot CLI, version 1.0.113 or later,
installed on the machine it runs on — AgentKit finds it and runs it as a confined runtime, so the
application ships no runtime of its own:

```bash
winget install GitHub.Copilot              # Windows
brew install --cask copilot-cli            # macOS
npm install -g @github/copilot             # any platform with Node.js
copilot --no-auto-update --version         # the version AgentKit will run
```

*Installing the Copilot CLI* under *Building an Agent* covers the version check and the install
script. To bundle the runtime with the application instead, see *Bundling the Runtime Instead*.

If you target Ollama, add one more. Ollama is an ordinary `IChatClient` provider in every other
respect, but it is the one that will tell you the context window it will enforce:

```bash
dotnet add package DemaConsulting.AgentKit.Agents.Ollama       # reads Ollama's context window
```

The Ollama package is optional: add it only if you target Ollama. It carries the `OllamaSharp`
dependency that the `IChatClient` adapter deliberately does not, so an application on any other
provider never takes it on.

## API Documentation

Detailed API documentation for all public types and members is distributed in the `api/` folder
of the NuGet package.

This guide explains the safety model and how the pieces compose; the `api/` reference is the
signature-level companion to it. It is generated from the XML doc comments by the build — so it
cannot drift from the code it describes — and is organized for gradual disclosure: an index, then
a page per namespace, type, and member. That shape is deliberate, because the intended reader is
often a coding agent assembling an application against AgentKit, which works far better from one
small targeted page than from a single large document. The build fails on any public member that
lacks a documentation summary, so the reference is complete by construction, and it compiles every
`<example>` the reference contains against the real API, so a documented example cannot describe an
API the code does not have.

# What the Library Provides Today

AgentKit does not provide an agent runtime or a provider abstraction: Microsoft Agent Framework
supplies those, and AgentKit's tools work with it, with the GitHub Copilot SDK, and with any
`IChatClient` alike. What AgentKit provides is the part those runtimes leave to the application:
tools guarded by the application's path grants and limits, and a session that manages its own
context window so a long-running agent keeps working within it on each supported provider.

AgentKit Core is the contract package, and it carries two things. It defines the safety model that
every AgentKit tool, and every tool an application writes for itself, is built against. It also
carries the **session engine** — the part that keeps a long-running agent alive by compacting its
own context, described under *Sessions* below. Neither is an add-on to the other: an agent whose
tools are guarded but which runs out of context after twenty turns is not much use, and the two ship
in one package for that reason.
Core also describes the models a provider offers in one shared form, and validates a chosen
model's settings before a session starts; see *Choosing a Model* below.

Ready-made guarded tool families ship in
`DemaConsulting.AgentKit.Tools` — the text file, file, markdown, todo, memory and agent families —
and in `DemaConsulting.AgentKit.Tools.Visual` — the image and diagram families — all described under *Tool
Families* below, so a consumer can attach shipped tools
directly, or write its own tools against this contract. Capability packs that adapt other
libraries, such as document extraction and speech, into guarded agent tools are planned.

## Path Policy

A path policy separates two orthogonal ideas. The **working directory** is the single location a
relative path is anchored to, and nothing else — it carries no permission of its own. A **grant**
is a permitted location carrying an access level, and nothing else — it says a location may be read
(`PathRule.ReadOnly`) or read and written (`PathRule.ReadWrite`), or is unrestricted at a stated
level (`PathRule.Unrestricted`), and it carries its own denied patterns. A policy holds one working
directory and zero or more grants.

Most applications want one folder that is both the anchor and the permitted location, written
explicitly as a single read-write grant over the working directory:

```csharp
var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);
```

The working directory and the grants are independent. The working directory may be granted
read-write, granted read-only, or granted nothing at all: an application folder can anchor relative
paths while granting nothing, and a bare name then resolves under it correctly and is then correctly
denied — coherent, not a special case. The application decides what access the working directory
should have by granting it, exactly as it grants any other location; it receives none implicitly.
Add more grants when an agent needs several locations, for example a read-only source folder and a
read-write output folder.

**A path a model supplies is read relative to the working directory.** A model asks for `notes.txt`,
not for its absolute location, so that is the request the policy answers. A path resolved against the
location the host process happened to be started from would refuse every legitimate request while
looking, from the outside, like a containment decision — so the working directory is required, not
defaulted, and a missing one is a programming error the constructor rejects. Absolute paths remain
expressible and remain subject to the same containment decision. A request naming no path at all —
an omitted, empty or whitespace argument, or the literal word a model's runtime prints for absence —
means the working directory itself. A bare segment resolves against the working directory first, and
falls back to a same-named grant only when nothing by that name exists in the working directory: a
segment equal to the final folder name of exactly one grant then resolves to that grant, while a
segment matching two or more is left un-aliased and denied with both locations named.

A read is permitted when any grant permits it, and a write only when a read-write grant permits it,
so a read-only grant never authorizes a write. Access is requested through
`PathPolicy.TryResolveRead` and `PathPolicy.TryResolveWrite`, which return whether the access is
permitted, the real location on success, and a reason on refusal. Directory listings are obtained
through `PathPolicy.EnumerateFiles`, which applies the same decision, so a listing can never
advertise a file that access would refuse; at this level, a listing that names no directory
enumerates the working directory — a tool is free to build a broader listing on top of that
decision, and `file_list` does, as described under *Composing a Tool List* below.

Every containment decision is made on the normalized absolute location a path denotes, with `.`
and `..` segments collapsed, so a path that merely spells its way out of the granted location is
refused. Symbolic links and other reparse points are not followed and not detected, with one
deliberate exception: `file_delete_directory` and `file_move_directory` refuse a path they are
asked to reach *through* a link that leaves the permitted location, because there the content at
stake is a whole tree rather than a single entry.

**The policy is a guardrail, not a sandbox.** A tool cannot express an operation the policy
forbids, but AgentKit does not replace operating-system isolation for untrusted code, and because
path resolution is lexical, a link is not a general protection boundary. The link refusal of the
two destructive directory tools is a **pre-flight check over paths**: the path a caller names is
classified, and the recursive delete additionally refuses any link its walk discovers, both before
anything is touched. It is not a race-resistant control. A process able to write inside a granted
location can replace a component after the check, and no path-based API can close that window,
though no removal these tools issue follows a link, so what a race costs is bounded by a single
entry rather than a tree.

**A directory request is judged at every level it would create.** `file_create_directory` asks the
write decision about every missing directory it would create, not only the one named, so a request
whose missing parents would reach above every grant is refused with nothing created.

**Denied patterns reach the whole tree a directory operation touches.** A grant's denied patterns
exclude names inside a permitted location, and a recursive removal or a directory move reaches
every entry beneath the path it was given. Both tools therefore ask the write decision about every
entry the operation would take — and, for a move, about the place each entry would land as well —
so a grant such as `PathRule.ReadWrite("/workspace", ["*.key"])` refuses the removal of any
directory holding key material rather than destroying it as part of a tree. The refusal names the
offending entry, and nothing is removed or moved: the permitted subset is deliberately left alone,
because a partial result nobody asked for is worse than a refusal a model can act on.
A refusal is a returned value, never an exception, so a refused tool call does not end an agent's
turn — and no path a caller supplies, including none at all, is reported as an exception. A refusal
states what was requested, how a relative request was interpreted, and which locations are permitted
with their access levels, so a confined agent learns where it may work instead of retrying against a
bare "no". The interpretation and the permitted locations are stated relative to the working
directory — the working directory itself as `.`, a sibling folder as `../session` — which is exactly
how the model can address them, so a refusal never reveals where on the host they live:

```text
Denied (PathNotPermitted): Denied: outside every permitted location.
Requested: "../outside/secret.txt"
Interpreted as: ../outside/secret.txt (relative to the working directory)
Permitted locations (relative to the working directory):
  - . (read-only)
  - ../session (read-write)
```

Only a granted location with no relative spelling at all, such as one on another drive, is named by
its absolute path.

> **Transition hazard.** With a single granted working directory, tool output uses the relative
> dialect — a listing reports bare relative names and the model imitates them. Adding a second
> granted location moves paths under it to the absolute dialect (reported under an absolute header),
> and nothing else warns you the switch happened.

## Tool Limits

`ToolLimits` carries the ceilings a tool observes: the bytes it may read, the characters its
result may return to the model, the bytes of binary content it may return, how deep a chain
of delegated agents may run, how many pixels one image may be decoded into, and how many entries
one recursive directory removal may take. Limits are carried with
the policy, through `PathPolicy.Limits`, so every
tool an application attaches observes one budget rather than each inventing its own. A host that
configures nothing still operates within the published defaults.

**Delegation depth is bounded.** `MaxAgentDepth` counts the delegated agents stacked beneath the
root agent an application starts, which is at depth zero; its default is two, so a root agent may
start a child, that child may start one more, and the grandchild's own attempt to delegate is
refused. The refusal states the ceiling and the level the calling agent is already at, and is
returned before any child is composed or started, so a refused delegation costs nothing to reach.
Setting the ceiling to zero forbids delegation entirely, which is the expressible way for a host
to attach the agent family and then withhold its use. Every ceiling accepts zero for the same
reason; a negative ceiling has no meaning and is rejected.

**Recursive removal is bounded — and the bound is modest about itself.** `MaxDeleteEntries`
counts every entry `file_delete_directory` would remove, including the named directory itself, so
an empty directory costs one entry and a ceiling of zero forbids the operation entirely. Its
default is a thousand, chosen so that ordinary housekeeping succeeds while a mistaken request
naming a source tree, a test tree or an installed package directory is refused. The refusal names
the real entry count alongside the ceiling, because a model told "more than the limit" learns
nothing about whether subdividing the request would help.

**This ceiling bounds a mistake, not an intention.** An agent that means to destroy a tree can
remove it a file at a time with `file_delete`, and the ceiling will not stop it. What it buys is
that a wrong path or a model confusion is survivable and observable rather than total in one call.
Treat it as a blast-radius limit, not as a security boundary against a hostile agent.

## Tool Names and Guarded Construction

`ToolName.Create` composes a name from a family and a verb, and `ToolName.Validate` checks a
name against the convention. Every tool name carries a family prefix, which is what stops an
application combining AgentKit with another tool provider from presenting the model with two
identically named tools.

`GuardedToolFactory.Create` is the only supported way to construct a tool. It validates the name
and applies the result-delivery guard to every tool it creates, so a tool author cannot omit either
by forgetting it.

## Tool Results

`ToolResult` constructs what a tool returns: `Text` for text, `Structured` for data that is neither text nor content,
`Binary` and `Image` for content carrying a media type and an optional caption, and `Denied` for a refusal. A refusal
names its reason and states the fact that caused it; **a denial states a fact and never prescribes a remedy**, because a
denial that helpfully suggested another tool was measured pushing a model into a destructive workaround. The optional
redirect survives that rule in exactly two places, where naming the tool *is* the fact being stated rather than a route
around what was withheld: binary content is offered to `image_read`, and an `.svg`, which is vector content rather than
a raster image, is offered to `image_view`, which draws it. Every other refusal — including every policy refusal —
carries no redirect. Text and content reach the provider in the form the tool produced them rather than as serialized
JSON, which is what allows a returned image to be recognized as an image; a structured result is serialized to JSON on
the way out, because JSON is the form in which a provider can read structured data.

`ToolResult.Oversized` is the one result that is not delivered as constructed. A tool whose whole
output exceeds the result ceiling returns it together with the refusal it would give, and the
composition decides which the model sees; see *Large Tool Outputs* below.

## Large Tool Outputs

Every tool result is bounded by `ToolLimits.MaxResultCharacters` (32,000 characters by default),
because everything a tool returns is spent from the same context window the conversation needs.
Without anything further, a result over that ceiling is **refused**, never truncated — a truncated
listing or search would silently hide what the model never knew to ask for. That is safe, but it
loses work: a broad search, a full listing of a large tree, the outline of a long document, or a
delegated agent's whole report becomes a refusal the model can only answer by guessing how to
narrow the request. And a custom tool that measures nothing hands its whole output to the provider,
unless the application bounds it with `AsGuarded` (see *Bounding a Tool You Did Not Build*).

An application can opt in to **retaining** such outputs instead:

```csharp
IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .Add(new TextFilePack())
    .Add(new FilePack())
    .WithToolOutputSpill()
    .Build();
```

With retention composed, a result over the ceiling is kept whole in memory and the model receives
a bounded preview in its place — a header naming an opaque reference, the output's size and the two
reader tools, then the first and last lines numbered exactly as `text_file_read` numbers a file:

```text
[Output output-3 from text_file_search: 184233 characters in 3012 lines, larger than the
32000-character result limit. Showing lines 1-58 and 2990-3012. The full output is retained:
read it with tool_output_read (id 'output-3', startLine, lineCount) or find text in it with
tool_output_search.]
   1| notes/a.md:12:...
[... lines 59-2989 omitted ...]
3012| notes/z.md:4:...
```

The model then reads exactly what it needs:

- **`tool_output_read`** takes the reference and an optional `startLine` and `lineCount`, and
  returns that range under an `output-3 lines A-B of N` header. Omit `lineCount` to receive as many
  whole lines as fit the result ceiling.
- **`tool_output_search`** takes the reference and a pattern of at most 1,000 characters — literal by default, a regular
  expression with `literal: false` — with optional `ignoreCase`, `contextLines` and `maxMatches`, and reports matching
  lines as `line:content` using the same numbers.

**The readers cannot reach the file system.** They accept a reference and paging or search
arguments, and no path, and they read only the outputs of the composition that offered them.
Retention adds no reach to an agent, which is why it is held in memory rather than written to a
temporary file the confined tools could not read anyway.

**What is retained.** Every tool in the composition is covered, including the application's own
tools — even an `AIFunction` written by hand rather than built with `GuardedToolFactory`. Text, JSON
and text content longer than the ceiling are retained, and JSON is indented so it can be paged one
value per line. Any other value a tool returns that is not content — a record, an anonymous object,
a collection, a number, a byte array — is first serialized to JSON with the tool's own serializer
options, exactly as `GuardedToolFactory` serializes it, and is then delivered, or retained, as that
JSON; a value that cannot be serialized fails the call with the serializer's exception, as it would
from the factory. The listing, searching, outlining and delegating tools — `file_list`,
`text_file_search`, `markdown_outline` and `agent_run` — are retained exactly where they would
otherwise refuse. Images, other binary content and any other content that is not text are never
retained and are delivered as the tool returned them; AgentKit's own tools bound binary content by
its own ceiling. `text_file_read` keeps refusing an oversized read, because the file is already
pageable with `startLine` and `lineCount`.

**Retention is bounded.** `ToolOutputOptions` sets the preview size (4,000 characters), the largest
single output retained (4,194,304 characters), the total retained (16,777,216 characters, about
32 MiB) and the number of outputs retained (256); pass one to `WithToolOutputSpill` to change any of
them. When a new output would exceed a ceiling the **oldest** outputs are released first, and a
reference to a released output is answered with the fact that it is no longer retained, so the model
re-runs the tool rather than guessing. A single output larger than the per-output ceiling is never
truncated; it is refused. A tool that reports its output as oversized gives its usual refusal: the
built-in `file_list`, `markdown_outline`, `text_file_search` and `agent_run` do this, as does your
own tool if it returns `ToolResult.Oversized`. Any other tool, including your own tools returning
text, JSON or a structured value, gets a refusal stating the output's size and the retention limit.

**Retention needs a result ceiling of at least 1,000 characters**
(`ToolOutputSpill.MinimumResultCharacters`). The preview's header names the reference, and the
reference is the result, so the ceiling must leave room for it. `Build` throws
`InvalidOperationException` for a smaller ceiling, before any pack is asked for its tools;
`ToolOutputSpill.Apply` throws `ArgumentException`; and an `AgentPack` given `childToolOutput`
throws `InvalidOperationException` from its `CreateTools` — that is, from the `Build` that composes
it — because every delegated agent inherits the ceiling. Tool names must also be no longer than 64
characters, which every guarded tool already is; only a hand-built `AIFunction` can be longer.
For a delegated agent composed with `childToolOutput`, such a name — like a child tool under the
reserved `tool_output_` prefix, or one name published by two child packs with overlapping
prefixes — makes `agent_run` refuse that delegation as a composition failure rather than start
the child. With that, every result a retaining composition writes itself — a preview, a page, a
search listing — fits within the ceiling. A composition without retention accepts any ceiling, zero
included.

**Lifetime and isolation.** Retained outputs live exactly as long as the tool list they were
composed into. A session carries that list unchanged across compaction, so a reference survives a
rotation. Each `Build` has its own store, and so does each delegated agent: pass `childToolOutput`
to `AgentPack`, after `hostCapabilities`, to compose children (and their children) with retention,
each with its own store and its own two readers. A child whose profile admits no tools stays
tool-less. A child's final answer that is too large for the *parent's* ceiling is retained by the
parent whenever the parent's composition retains outputs. A host that hands one composed tool list
to several conversations shares the retained outputs between them, exactly as it would share a task
list; compose per conversation to keep them apart. A host that adds tools to a conversation over
time, or persists conversations, owns a `ToolOutputRetention` per conversation instead — see
*Retaining Outputs per Conversation*.

**Without the opt-in nothing changes.** Every refusal is the same text it always was, no reader is offered, and a custom
tool's oversized result still reaches the provider unbounded unless it is bounded with `AsGuarded`. Retention runs
inside the tool wrappers rather than in the provider, so it behaves the same with each supported adapter (`IChatClient`
agents and GitHub Copilot); on GitHub Copilot the readers are published, allow-listed and approved like any other
supplied tool. A list assembled without a builder can be composed the same way with
`ToolOutputSpill.Apply(tools, limits)`.

## Delivering Tool Images to the Model

Providers differ in where they accept image content, and the difference is silent. Some deliver an
image a tool returned straight to the model. Others accept images on messages but not in tool
responses: the content is preserved all the way through the framework and then discarded at the
wire, after which the model describes a picture it never received and nothing reports an error.

`ImagePromotingChatClient` delivers such an image on an `IChatClient` provider that would otherwise
drop it. It must sit **beneath** the
function-invocation loop, so that it observes the conversation after tool results have been
appended. It then carries any image a tool result holds onto a following user message, announced as
coming from the tool, and leaves a short placeholder in the tool result in its place —
`[Image promoted to the following user message: image/png, 316,404 bytes.]` — so the image is sent
once. A conversation whose tool results carry no image is forwarded unchanged, the caller's own
messages are never modified, and the wrapper can be left installed.

**When it is needed:**

- **Ollama through OllamaSharp — required.** OllamaSharp writes an image found in a tool result into
  the tool message as base64 text rather than into the message's `images`, so the model never sees
  pixels; and the text can flood the window — a 316 KB image became a 421,629-character tool message
  that truncated the user's question. With the wrapper the tool message is a one-line placeholder and
  the image arrives where Ollama reads it.
- **The GitHub Copilot SDK — not needed.** The Copilot runtime delivers a tool's binary result to the
  model itself; `CopilotAgentFactory` does not install the wrapper and you should not either.
- **`ChatClientAgentFactory` — already installed.** Every agent and session the ChatClient adapter
  builds carries it.

**Installing it in a pipeline you build.** The ChatClient adapter package adds a
`ChatClientBuilder` extension:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;

// A host-built ChatClientAgent adds its function-invocation loop above the client it is given.
IChatClient forAgent = providerClient.AsBuilder().UseImagePromotion().Build();

// A raw pipeline: function invocation first, then image promotion beneath it.
IChatClient pipeline = providerClient.AsBuilder()
    .UseFunctionInvocation()
    .UseImagePromotion()
    .Build();
```

Calling `UseImagePromotion()` before `UseFunctionInvocation()` would place the wrapper above the loop,
where it never sees a tool result; `Build()` refuses that pipeline with an `InvalidOperationException`
naming the correct order. A host that references only Core can install the wrapper with
`builder.Use(inner => new ImagePromotingChatClient(inner))`, beneath the loop in the same way.

## Tool Packs

A package publishes its tools as a pack by implementing `IToolPack`, declaring the family prefix
its tools carry and the `HostCapabilities` the host must provide. An application composes packs
through `ToolPackBuilder`, declaring what its host supports and adding one pack per capability it
wishes to attach. A pack whose required capabilities the host does not provide is never asked to
create its tools at all, so the model is never offered a tool it cannot use. With a model catalog,
derive `Vision` from the selected model through `selection.HostCapabilities` rather than asserting it;
the declaration stays the host's. See *Choosing a Model* below.

The same reasoning applies one level finer, to individual tools. A pack is handed the access policy
before it has any path to test, and may withhold those of its tools whose writes the policy
governs — so a tool that can act only by writing, under a policy holding no read-write grant
anywhere, is simply not offered rather than offered and refused on every use. The two gates divide
cleanly: **the host capability decides whether a pack is asked for its tools; the policy decides
which of its tools it returns.** Only writing is gated, so the published set is not a promise that
every tool in it can succeed: a policy with **no grants at all** still publishes the reading tools,
and each of them then refuses every path. That one case is deliberately not gated — a grant is
read-only or read-write, so permission to write always carries permission to read, and a reading
tool is therefore unusable only when nothing whatever is granted, which yields an agent that can
touch no file however its tool list is trimmed. Nothing reports which tools
were withheld, for the same reason nothing reports which packs were skipped — the application holds
both the capability declaration and the policy, so it can already answer the question, and a list
of excluded tools is an invitation to add them back by another route.

# Tool Families

AgentKit ships eight ready-made guarded tool families: six in `DemaConsulting.AgentKit.Tools`, and
the image and diagram families in `DemaConsulting.AgentKit.Tools.Visual`, whose namespaces are
`DemaConsulting.AgentKit.Tools.Visual.Image` and `DemaConsulting.AgentKit.Tools.Visual.Diagram`.
Each family is a pack
an application adds to a `ToolPackBuilder`; the builder gates each pack on the host capabilities it
requires and returns the `AIFunction` list to hand to an agent framework. The table below is the
single place in the user-facing documentation that names every shipped tool; `lint.ps1` reads the
table itself and checks it against the tool-name and family-prefix constants in the source, in both
directions, so a tool the library ships but this table omits — and a tool this table names but the
library does not ship — fails the build. No other passage of this guide can stand in for a missing
row.

## Available Tools

| Family | Tool | Purpose | Capability | Write grant |
| --- | --- | --- | --- | --- |
| Text file | `text_file_search` | Searches permitted text files, or one named file, for a pattern | None | No |
| Text file | `text_file_read` | Reads a paged, line-numbered file window | None | No |
| Text file | `text_file_create` | Creates a new text file within the policy | None | Yes |
| Text file | `text_file_write` | Sets a file's whole content, capturing the old | None | Yes |
| Text file | `text_file_replace` | Replaces an exact span of text in a file | None | Yes |
| Text file | `text_file_cut_lines` | Removes a line range into a named buffer | None | Yes |
| Text file | `text_file_copy_lines` | Copies a line range into a buffer, source kept | None | No |
| Text file | `text_file_paste_lines` | Pastes lines held in a buffer into a file | None | Yes |
| File | `file_list` | Lists files of any type within the policy | None | No |
| File | `file_copy` | Copies a file within the policy | None | Yes |
| File | `file_move` | Moves a file within the policy | None | Yes |
| File | `file_delete` | Deletes a single file within the policy | None | Yes |
| File | `file_create_directory` | Creates a directory, with missing parents | None | Yes |
| File | `file_move_directory` | Moves or renames a directory within the policy | None | Yes |
| File | `file_delete_directory` | Deletes a directory and all it contains | None | Yes |
| Markdown | `markdown_outline` | Reports the heading outline of a Markdown file | None | No |
| Image | `image_read` | Reads an image or PDF, reporting pixel size | `Vision` | No |
| Image | `image_crop` | Returns or writes a pixel region of a PNG/JPEG | `Vision` | No |
| Image | `image_auto_crop` | Trims an image to its content, with padding | `Vision` | No |
| Image | `image_view` | Draws a document, drawing or diagram file as a picture | `Vision` | No |
| Diagram | `diagram_help` | Returns drawing help for a diagram language, by topic | None | No |
| Diagram | `diagram_render` | Draws diagram source; shows, describes or saves it | `Vision`/None | Text preview |
| Todo | `todo_list` | Reports the recorded steps, in recorded order | None | No |
| Todo | `todo_set` | Records a step, or updates the step with that id | None | No |
| Todo | `todo_remove` | Drops a step by id from the task list | None | No |
| Memory | `memory_file` | Stores one memory under a short descriptor | None | No |
| Memory | `memory_recall` | Returns the memories nearest a stated question | None | No |
| Memory | `memory_update` | Replaces a memory's details, keeping its subject | None | No |
| Memory | `memory_revise` | Replaces a memory's subject, details and source | None | No |
| Memory | `memory_forget` | Removes one memory permanently | None | No |
| Agent | `agent_run` | Delegates a task to a named child agent profile | `Delegation` | No |
| Tool output | `tool_output_read` | Reads a line range of a retained large tool output | None | No |
| Tool output | `tool_output_search` | Finds a pattern in a retained large tool output | None | No |

The two **Tool output** rows are not a family a pack contributes. They ship in
`DemaConsulting.AgentKit.Core` and are appended to a composition only when it retains large tool
outputs; see *Large Tool Outputs* above. Everything below about the eight families' tools excludes
them.

The **Capability** column is the host capability the family requires; the **Write grant** column
says whether the tool is published only when the access policy permits writing somewhere.

**A tool marked `Yes` is not published at all unless the policy permits writing somewhere.** Eleven
of the thirty-one family tools are marked `Yes`: five in the text file family and six in the file
family. `diagram_render` is marked `Text preview`: it is treated the same way only when the diagram
family is in text preview, where it must save the picture it describes. If every grant an
application configures is read-only — or it configures no grants at all — those eleven are absent
from the composed list, and the eight families together publish twenty tools rather than
thirty-one, or nineteen when the diagram family is in text preview. The text file family offers
`text_file_search`, `text_file_read` and `text_file_copy_lines`; the file family offers
`file_list`; the diagram family in text preview offers `diagram_help`; the markdown, image, todo,
memory and agent families, and the diagram family in image preview, are unaffected.

The question is asked once, of the **whole policy**, not of any particular path. A policy granting
a read-only workspace and a read-write session folder can genuinely write, so it publishes every
tool — even though a relative name still lands in the read-only workspace and a write there is
still refused. Only a policy with no read-write grant anywhere withholds them.

**A policy with no grants at all is the one case where a published tool cannot work.** The eleven
write-performing tools are withheld as above, and the survivors are published as usual —
but `text_file_read`, `text_file_search`, `text_file_copy_lines`, `file_list`, `image_read` and
`image_view` then
refuse every path they are given, because no path resolves under a policy that grants nothing. That
is not gated, and the reason is that it cannot arise any other way: a grant is either read-only or
read-write, so permission to write always carries permission to read. The configuration is a
non-functional agent whatever list it is handed, so trimming the list would not rescue it.

Three families' tools are marked `No` for reasons worth stating. `text_file_copy_lines` consults
only the read decision, so it works from a read-only location; note that under a read-only policy
the buffer it fills has no drain, because `text_file_paste_lines` is the buffer's only reader.
`file_list` needs only the read decision to report what exists, consulting the write decision
solely to mark a listed location as writable. The todo and memory families keep their state in the
composition's own store rather than on disk, so a read-only *path* policy governs nothing they do.

**What `text_file_write` captures, and for how long.** Setting a file's whole content puts the
content it displaced into the line buffer named `overwritten`, and `text_file_paste_lines` reading
that buffer is the route back. The capture holds **only the most recent overwrite**: a second
`text_file_write`, to the same file or any other, replaces it, and nothing earlier can be
recovered. The capture is also a **sequential** guarantee: the tool reads the file, captures what
it read and then writes, and those steps are not serialized against another writer. If two callers
write the same file concurrently, the buffer holds the content that was there before whichever
write read it, and the content an interleaved write produced can be lost without ever having been
captured. AgentKit takes no cross-tool lock to prevent that — an agent's tool calls are sequential
and a delegated sub-agent composes its own buffers — so an application that drives one composition
from several threads at once should not rely on the capture. The buffers live in the composition
rather than on disk, so they are also gone when the
composition ends. An agent that means to keep a version must copy the file before writing it.

`image_crop` and `image_auto_crop` are marked `No` and are offered under a read-only policy,
because each one's primary mode
returns the region as image content and writes nothing. The optional `destination` they accept does
need a
write grant, and naming one under a read-only policy earns the ordinary refusal, which enumerates
the locations that are writable. Neither tool's description changes with the policy: the
`destination` parameter's own description is fixed at compile time, so a policy-varying tool
description would contradict it within a single declaration.

The text file (`TextFilePack`), file (`FilePack`), Markdown (`MarkdownPack`), todo (`TodoPack`) and
memory (`MemoryPack`) families require no host capability. Two families are gated. The image family
(`ImagePack`) requires the `Vision` host capability, and the agent family (`AgentPack`) requires
`Delegation`: unless the host declares the capability, the builder never asks the pack to create
its tools, so a model is never offered a tool its host cannot use. The diagram family
(`DiagramPack`) requires `Vision` only in image preview; `DiagramPack.For(host)` chooses image
preview for a host that declares `Vision` and text preview otherwise, so a host that cannot see
still receives it. Where the model was chosen from a
catalog, take the `Vision` declaration from the selected model; see *A Vision-Only Application*.

The image family's region tools and its read tool are one capability. `image_read` states the image's
pixel dimensions alongside its content, and `image_crop` takes a region stated in pixels from the
top-left corner of that same coordinate space — because a model can see a picture but cannot
measure one, and a region request it cannot aim is a region request it will aim wrongly. A region
that does not lie wholly inside the image is **refused, naming the image's real dimensions**, never
quietly reduced to one that would have fitted: a reduced region answers a different question while
reporting success, and the model has no way to detect the substitution.

`image_auto_crop` answers the region question from the other side: the one a model cannot state in
that coordinate space at all. It takes **no region** — it finds the rectangle the image's content
occupies, expands it by a `padding` margin (8 pixels by default, 0 to 256) and returns or writes
that. The need is ordinary: a screenshot, a slide export or a rendered chart arrives surrounded by
margin that carries no information and costs the same resolution budget the content does, and a
model can see that a picture is mostly empty without being able to measure where the emptiness
stops.

Three properties make that trim predictable rather than a guess. **The background is taken from the
image's own one-pixel border, never assumed** — so a dark-themed screenshot, a colored slide and an
export with a transparent margin all trim correctly, where a tool comparing against white would
return the picture unchanged while reporting success. **A pixel within a small fixed tolerance of
that background counts as background**, so anti-aliased edges and compression artifacts do not
defeat the trim; that tolerance is 8 per channel out of 255, compared on red, green, blue and
alpha independently, and it is deliberately *not* a parameter, because a caller that cannot see
the image cannot choose one better than the default, and the result stays a pure function of the
file and the padding. **Padding that would run past an edge is clamped, never refused** — content
flush to an edge is ordinary, and a padding larger than every margin simply yields the whole image.
An image in which nothing differs from the background is **refused, naming its dimensions**, and
nothing is written: there is no honest content region, and returning the whole picture would answer
a different question while reporting success.

`image_crop` has two outcomes, and the request says which. **Omit `destination` and the region
comes back inline as image content to look at**, which is what it has always done and what it still
does unchanged. **Name a `destination` and the region is written there as a new `.png` file**, and
the model receives a text confirmation naming the file, the region and the source's dimensions
rather than the image itself. That second outcome exists because a region a model can only look at
cannot become a figure: an agent preparing a document needs the region it identified to exist as a
file it can point at. `image_auto_crop` offers the same two outcomes on the same terms, so an agent
meets one rule whichever region tool produced the figure.

For an application author, the destination is where the two grants meet. **It is resolved through
the policy's write decision, independently of the read that admitted the image** — so a path an
agent may read is refused as a destination unless a read-write grant permits it too. One
`image_crop` call therefore exercises both of the grants you configured, which is what lets a
read-wide, write-narrow policy be expressed rather than approximated: point the workspace grant at
a folder you only want read, point a read-write grant at the folder you want artifacts produced in,
and the agent can crop from the first into the second and nowhere else. The refusal for a
destination outside the write grant enumerates the locations that are writable, with their access
levels, so the agent recovers rather than guesses. **The destination must name a `.png` file** —
matched case-insensitively, as every extension this family reads is — because a region is always
encoded as PNG and a file named otherwise would be one every later reader is entitled to misread,
and **an existing file is never replaced** — the same guarantee
`text_file_create` makes. No directory is created for a destination whose parent does not exist.

`image_view` is how a model looks at visual content a provider cannot render itself. `image_read`
hands a file's bytes to the provider unchanged, which is right for PNG, JPEG, GIF, WebP and PDF;
`image_view` instead **draws** a file — an SVG, a PDF page, a PPTX slide, a VSDX page, a Mermaid
(`.mmd`), PlantUML (`.puml`), Vega-Lite (`.vl.json`) or OpenSCAD (`.scad`) source, or a raster
image — and returns the drawing as a PNG on an opaque white background, with a caption naming the
format, the page of the page count for a multi-page document, the size and anything the drawing
left out. It takes an optional `page` (from 1) and an optional `width` in pixels.

- **The file is checked like any other read**, before anything is learned about it, and every file
  the document references — an embedded picture, an included file, a chart's data file — must lie
  inside the locations the agent may read, deny patterns included. A reference that escapes, by
  `..`, an absolute path, a `file:` URI or a link, is refused, and nothing is ever fetched from the
  network.
- **A file it cannot draw is reported, not refused**: the result names an error code, the line and
  column, the offending line with a caret under the column, and advice, so a model that wrote the
  file can fix it. A drawing that came out empty, or whose embedded picture was refused, is
  reported the same way rather than shown.
- **Every drawing is bounded** by the policy's pixel budget and binary ceiling and by the pack's
  `VisualOptions`: a render timeout (30 seconds by default, including any wait for another drawing
  to finish), a largest side (2048 pixels), a smallest side a small diagram is enlarged to so its
  labels can be read (768 pixels), a triangle limit for OpenSCAD models (500,000) and the number of
  drawings a pack runs at once (one). Pass options to `new ImagePack(options)`; each value outside
  its range is refused where it is set. `ImagePack.WarmUpAsync` optionally draws a sample of each
  format once, so the first drawing a model asks for does not pay the libraries' start-up cost.

`diagram_render` and `diagram_help` let a model draw the figures it can write as source — Mermaid,
PlantUML, SVG, Vega-Lite and OpenSCAD — check them and fix them. `diagram_render` takes the source
inline, never a path, and draws it under the same confinement and bounds as `image_view`; a
relative reference in the source resolves against the working directory when the agent may read
it. In image preview it returns the picture; in text preview it returns a description — the
picture's size, the labels and features it contains, and its dominant colors — and requires
`save_as`. A source it cannot draw is reported with its line and column, the offending line with a
caret, and the `diagram_help` topic of the language's diagnostics. `save_as` writes the picture as
a `.png` under the write grant and replaces an earlier picture of the same name, so a model can save
on every attempt. `diagram_help` returns an index of topics, then one topic's card with its example
sources; the cards written for the application rather than the model are not served.
`DiagramPackOptions` chooses the languages and the `VisualOptions` bounds, and
`DiagramPack.Instructions` is a short paragraph an application may add to its agent's instructions;
the pack never adds it itself. The *Images, Documents and Diagrams* chapter covers both packs in
depth: choosing between `image_read` and `image_view`, the preview modes, the help filtering,
saving, referenced files, the `VisualOptions` bounds, warming up and the known limitations.

Three of the families carry state or collaborators beyond the path policy, and an application
should know what it is attaching:

- **Todo** keeps a flat task list — an ordered sequence of steps, with no nesting and no
  dependencies. `TodoPack` allocates a fresh list per `CreateTools` call, so a delegated agent
  composed through `AgentPack` keeps its own list and cannot write into its parent's.
  `TodoPack.SuggestedInstruction` publishes wording an application can append rather than
  transcribe.
- **Memory** requires an `IEmbeddingGenerator<string, Embedding<float>>` that the application
  supplies; `MemoryPack` never inspects which backend it wraps. Only a memory's one-sentence
  descriptor is embedded and searched — never its details. Supplying no store gives each
  composition a fresh `InMemoryMemoryStore`, which **does not persist**: its memories live exactly
  as long as the composition. An application that supplies a store gets exactly that store, shared
  by every composition it is handed to. `MemoryPack.SuggestedInstruction` publishes the wording
  that the measurements in the design documentation were taken against.
- **Agent** takes the profiles a child may be run under, a runner, and the *packs* a child may draw
  on — never built tools — so a child's tools are composed afresh against the child's own policy
  and per-composition state stays with the child that owns it. How far delegation may chain is not
  the pack's decision: it is bounded by the `MaxAgentDepth` ceiling described under *Tool Limits*
  above, which a host may set to zero to attach the family and forbid its use.

## Composing a Tool List

An application names its working directory, adds the packs it wants to a
`ToolPackBuilder`, declares the capabilities its host supports, and builds the tool list:

```csharp
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.File;
using DemaConsulting.AgentKit.Tools.Markdown;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using Microsoft.Extensions.AI;

var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);

IList<AIFunction> tools =
[
    .. new ToolPackBuilder(policy)
        .WithHostCapabilities(HostCapabilities.Vision)
        .Add(new TextFilePack())
        .Add(new FilePack())
        .Add(new MarkdownPack())
        .Add(new ImagePack())
        .Build()
];

// Hand `tools` to ChatOptions.Tools, an IChatClient, or Microsoft Agent Framework.
```

Every tool returned observes the same policy and limits: a `text_file_read` that steps outside the
granted location, or exceeds the byte ceiling, returns a refusal rather than the file. Every tool
also reads a path the same way, so a name `file_list` reported can be handed straight back to
`text_file_read` or `image_read`. Called with no directory, `file_list` does not forward the absent
argument to a single enumeration: it makes one policy-governed enumeration per permitted location
and reports them all, so a listing with no argument is a discovery listing over every granted
location rather than a listing of the working directory alone. The tool
`Create` factories are internal, so composing through the packs is the only supported way to obtain
these tools.

The grant above is `PathRule.ReadWrite`, which is what makes this composition publish all twenty
tools of those four families. The same builder, the same packs and the same capability declaration
over a policy whose grants are all read-only yields a shorter list — nine tools, because the five
write-performing text file tools and the six file management tools are withheld. Nothing else
about the composition changes, and nothing announces the difference, so an application that means
its agent to edit must grant it somewhere to write.

The todo, memory and agent packs are added the same way, but take constructor arguments of their
own — a supplied store, an embedding generator, or the profiles, runner and child packs a delegated
agent is built from. *Sample: Research Assistant* below shows all three composed onto one policy.

Adding `.WithToolOutputSpill()` to the builder composes retention of large tool outputs over the
same list and appends `tool_output_read` and `tool_output_search` after every pack's tools; see
*Large Tool Outputs* above. Without it the list is exactly the packs' tools.

# Choosing a Model

An application that lets its user choose the model needs three things from the provider: the list of
models, what each one can do, and a check that a saved choice still holds. AgentKit Core describes
models in one shape for each supported provider, and each adapter package reads its provider's own list.

## Listing Models

A model catalog lists the models a provider offers. Each adapter ships one:

```csharp
using DemaConsulting.AgentKit.Agents.Ollama;
using DemaConsulting.AgentKit.Core;
using OllamaSharp;

var ollama = new OllamaApiClient(new Uri("http://localhost:11434"));
IModelCatalog catalog = new OllamaModelCatalog(ollama);

// Every model AgentKit can run, in the provider's order.
var models = await catalog.ListAsync();

// Only models that can see and call tools, with the ones AgentKit cannot run included too.
var visionModels = await catalog.ListAsync(new ModelQuery
{
    Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls,
    IncludeUnusable = true,
});
```

On Copilot, `new CopilotModelCatalog(runtime)` takes the `CopilotRuntime` AgentKit started (see
*Creating the Agent*; it runs the installed Copilot CLI by default), and lists the models the service
offers the user the runtime is signed in as. A runtime that is not signed in never gets this far:
`CopilotRuntime.StartAsync` refuses it. If the client later cannot list models — its connection was
lost or its sign-in lapsed — the catalog fails with an `InvalidOperationException` that says so and
names the ways to sign the runtime in; a runtime disposed by its host fails with
`ObjectDisposedException`. Other failures propagate as they occur. An Ollama catalog that cannot
reach its server fails with the client's `HttpRequestException`, or with an `OperationCanceledException`
when the client times out even though your token was not canceled.

A catalog reads its provider once and keeps the list for five minutes, so a picker that lists on every
keystroke costs one read. It is safe for concurrent use: callers that arrive during a read share it.
A read that fails is not kept, so the next call reads again. `RefreshAsync` reads again at once, for
example after the user was granted a new model.

What a listed model carries:

- **`IsUsable` and `UnusableReason`.** A model AgentKit cannot run is listed only when the query sets
  `IncludeUnusable`, so a picker can show it grayed out with the reason.
- **`Notices`.** Messages the provider attached. A notice of kind `Required`, such as a
  data-retention statement, must be shown to the user before the model is chosen.
- **`Category` and `BillingMultiplier`.** Copilot groups its models (for example `lightweight`,
  `versatile` and `powerful`) and states each model's cost as a multiplier. Ollama sets neither.
- **`ContextWindowTokens`.** The largest window the model provides: on Ollama, the length it was
  trained for; on Copilot, its largest window. It is a **ceiling**, not the window a session runs at.

Copilot also lists an `auto` entry, which lets the service choose a model per request. AgentKit does
not list it, because it cannot promise a capability the application requires.

## What a Model Description Means

A capability flag is set only when the provider reports it. A provider that says nothing about vision
has not said the model can see, so `Vision` stays clear and a vision application does not offer the
model. Both catalogs follow this rule for vision.

Each provider has one rule of its own:

- **Copilot tool calling** is treated as supported unless the service says otherwise. The service
  reports no tool-calling flag for any model, and its runtime is itself a tool-calling agent runtime.
  A model the service reports as unable to call tools is unusable.
- **Ollama capabilities** come from the server's own report, which Ollama 0.6.4 and later provide. A
  model is usable when it reports both completion and tools. On an older server, vision is inferred
  from the model file and the model is unusable, because tool support cannot be confirmed.

## Choosing Effort, Tier and Window

`ModelSettings` holds what the user chose: the model, and optionally a reasoning effort, a context
tier, a context window and an output limit. It is plain data, so an application saves it as it is.
`ModelSettings.DefaultsFor(model)` gives the starting point a picker shows.

- **Reasoning effort.** The identifiers are the provider's own, from `model.ReasoningEfforts`.
  `DefaultReasoningEffort` is `null` when the provider names none, which means the provider's own
  default applies; AgentKit invents none. Copilot efforts such as `max` and `minimal` have no
  provider-neutral equivalent, so they apply on Copilot only. An Ollama thinking model offers `none`,
  `low`, `medium` and `high`; the level is a hint the model interprets.
- **Context tier (Copilot).** A Copilot model offers `default`, labeled "Standard", and often
  `long_context`, labeled "Long context". AgentKit chose those labels; the service publishes the
  tiers through billing. A tier's `WindowTokens` is its **total** window. A live session reports the
  smaller prompt budget it enforces — for example 100,000 for a Standard tier described as 228,000 —
  and the session accounts against what the runtime reports.
- **Context window on Copilot.** A window you state is a ceiling only, applied by
  `CopilotProviderSessionFactory`. It lowers the window a session accounts against and is never sent
  to the runtime. The plain Copilot agent and the summarizer account against no window, so there is
  nothing for a ceiling to lower; they refuse a selection carrying one.
- **Context window on Ollama.** A window you state is the exact `num_ctx` the server is asked to load
  the model at, by an `OllamaContextSizingChatClient` you compose at that size. It must not exceed the
  length the model was trained for, and a larger value is refused.
  A reasonable choice is the smaller of the trained length and 32,768 — a long window costs
  memory on the server. AgentKit sets no default: with none stated, the server's own choice is read
  back, as *Where the Window Comes From* describes.

## Validating at Session Start

`settings.Validate(model, required)` lists every problem without throwing, so a picker can explain
them as the user chooses. Each message is written to be shown as it is.

Every time a session starts from saved settings, pass them through the catalog. `SelectAsync` looks the
model up as the provider describes it **now** and validates the settings against it:

```csharp
const ModelCapabilities required = ModelCapabilities.Vision | ModelCapabilities.ToolCalls;

try
{
    var selection = await catalog.SelectAsync(persisted, required, cancellationToken);
    // Start the session on the selection.
}
catch (ModelSettingsException exception)
{
    foreach (var problem in exception.Problems)
    {
        Console.WriteLine(problem);
    }
}
```

A model may have been withdrawn, lost a capability, or stopped accepting an effort, a tier or a
window. Each of those fails here, with every problem listed in `ModelSettingsException.Problems`,
rather than as a provider error part-way through a conversation. For example:

> Model 'text-small' does not support vision, which this application requires.

`ModelSettingsException` derives from `InvalidOperationException`, so if you also handle
`InvalidOperationException` — for example the Copilot catalog's sign-in failure — catch
`ModelSettingsException` first. A failure to read the catalog reaches you as the catalog raises it.

The result is a `ModelSelection`. Only validation creates one, so holding one shows the check ran.
The factories take model settings as a `ModelSelection` rather than as raw settings, and apply it once
when a session starts; the settings do not change for the life of that session. A factory that cannot
apply a setting the selection carries refuses it with an `ArgumentException` naming the setting,
rather than dropping it.

## A Vision-Only Application

An application that offers only models that can see follows four steps:

1. **List** with `ModelQuery.Require` set to `Vision | ToolCalls`, so the picker shows only models
   that can see and call tools.
2. **Select** with `SelectAsync(settings, required)` at every session start, with the same
   requirement, so a model that lost the capability since is refused rather than run.
3. **Declare** the host's capabilities from the selection, by passing `selection.HostCapabilities` to
   `WithHostCapabilities`. The image tools are then offered because the chosen model can see.
4. **Override** where needed. The declaration is still the host's, so a host can narrow it — for
   example to switch the image tools off — or declare `HostCapabilities.Vision` itself where no
   catalog describes the model.

```csharp
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Visual.Image;
using Microsoft.Extensions.AI;

var offered = await catalog.ListAsync(new ModelQuery { Require = required }, cancellationToken);
var selection = await catalog.SelectAsync(persisted, required, cancellationToken);

IList<AIFunction> tools =
[
    .. new ToolPackBuilder(policy)
        .WithHostCapabilities(selection.HostCapabilities)
        .Add(new TextFilePack())
        .Add(new ImagePack())
        .Build()
];
```

Then hand the selection to the factory that builds the agent or session. On any `IChatClient`
provider, including Ollama:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Agents.Ollama;

// Ollama: the selection's window is the stated window, asked for on every request.
var window = await OllamaContextWindow.ReadAsync(ollama, selection, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollama, window.Tokens);

// The model, effort and output limit ride on every request.
var agent = ChatClientAgentFactory.Create(sized, tools, selection: selection);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens, selection);

// A pipeline you build yourself.
IChatClient pipeline = sized.AsBuilder()
    .UseFunctionInvocation()
    .UseImagePromotion()
    .UseModelSettings(selection)
    .Build();
```

An `IChatClient` cannot apply a context tier, or an effort with no provider-neutral equivalent, so a
selection carrying either is refused with an `ArgumentException` rather than dropped. It has no
provider-neutral context window either, so a selection carrying `ContextWindowTokens` is accepted
only when a client beneath the settings decorator applies that same window — here
`OllamaContextSizingChatClient`, which reports it as an `IContextWindowChatClient` — and refused
otherwise with an `ArgumentException` naming `ContextWindowTokens`. In a pipeline you build, keep
`UseModelSettings` above the client that applies the window. `ChatClientProviderSessionFactory` also
refuses a selection whose window differs from its `windowTokens`, so a session accounts against the
window the provider runs at.

On Copilot, from a `CopilotModelCatalog`:

```csharp
using DemaConsulting.AgentKit.Agents.Copilot;

// The plain agent and the summarizer: model, effort and tier, with no window.
var copilotAgent = CopilotAgentFactory.Create(runtime, tools, selection: selection);
var summarizer = new CopilotSummarizer(runtime, selection: summarizerSelection);

// A compacting session: the selection may also carry a window, applied as a ceiling.
var copilotSessions = new CopilotProviderSessionFactory(runtime, selection: windowedSelection);
```

The effort and tier are set on every session the agent creates or resumes, and only when chosen. A
summarizer may run on a cheaper model, chosen and validated the same way. Pass either a model name or
a selection, never both; each factory refuses both with an `ArgumentException`, and also refuses a
selection made from another provider's catalog. A window is applied only by
`CopilotProviderSessionFactory`, as a ceiling on the window a session accounts against; the agent
and the summarizer account against no window and refuse a selection carrying one, naming
`ContextWindowTokens`. The runtime takes no output limit, so every Copilot factory refuses a
selection carrying `MaxOutputTokens` — the Copilot catalog never offers one, so only a model
description you built yourself can carry it.

# Building an Agent

A tool list is provider-neutral, but attaching it to a provider is not. Two adapter packages turn a
provider into a tool-using Microsoft Agent Framework agent in one call, each absorbing the one thing
that provider gets wrong. An application that builds its own agent or loop uses the tools directly;
see *Using AgentKit Tools in Any Agent*.

## Any IChatClient

`DemaConsulting.AgentKit.Agents.ChatClient` builds an agent from any `IChatClient`:

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;

var agent = ChatClientAgentFactory.Create(
    chatClient,          // any IChatClient provider
    tools,               // the AIFunction list composed above
    instructions: "You are a document assistant. Use the tools provided.",
    name: "assistant");
```

The factory installs `ImagePromotingChatClient` on **every** agent it builds, beneath the
function-invocation loop, and offers no option to disable it. Some providers reached through an
`IChatClient` preserve an image a tool returns and then drop it at the wire, after which the model
describes a picture it never received. The adapter does not leave that step to you: the decorator
cannot be turned off, so it cannot be forgotten. You do not wrap the client yourself.

This package also carries the provider side of the session engine for the whole `IChatClient`
family — `ChatClientProviderSession`, `ChatClientProviderSessionFactory` and `ChatClientSummarizer`.
See *Sessions* below.

## The GitHub Copilot SDK

`DemaConsulting.AgentKit.Agents.Copilot` builds an agent on the GitHub Copilot runtime through
`Microsoft.Copilot.SDK`. The SDK ships no Microsoft Agent Framework agent, so the package carries
its own: `CopilotAgentFactory.Create` returns an `AIAgent` whose every session AgentKit configures,
and the same package supplies the provider side of the session engine for Copilot.

### Installing the Copilot CLI

By default AgentKit runs the **GitHub Copilot CLI installed on the machine** as its runtime. The
package ships no runtime and the host application needs no reference to `Microsoft.Copilot.SDK`, so
its output stays the size of its own assemblies. Install the CLI with any of its installers:

```bash
winget install GitHub.Copilot                       # Windows (WinGet)
brew install --cask copilot-cli                     # macOS (Homebrew)
npm install -g @github/copilot                      # any platform with Node.js
curl -fsSL https://gh.io/copilot-install | bash     # macOS and Linux (install script)
```

**The CLI must be version 1.0.113 or later** (`CopilotCli.MinimumVersion`): the first CLI that speaks
the protocol of the `Microsoft.Copilot.SDK` this release of AgentKit is built on. AgentKit starts
the CLI with `--no-auto-update`, which makes it run the version **built into the installed
executable**, not a newer one that auto-update or `copilot update` downloaded. Check the version
AgentKit will run with:

```bash
copilot --no-auto-update --version
```

`copilot --version` can report a different version. With an installed 1.0.106 executable and 1.0.114 already in the
CLI's package cache, `copilot --version` reported 1.0.114, while `copilot --no-auto-update --version` reported 1.0.106 —
the version built into the executable, which is the one AgentKit runs. If the built-in version is too old, upgrade the
installation itself: `winget upgrade GitHub.Copilot`, `brew upgrade --cask copilot-cli`,
`npm install -g @github/copilot@latest`, or run the install script again. `CopilotRuntime.StartAsync` checks the version
before it starts anything and refuses an older CLI with a message that says all of this.

**Which CLI AgentKit runs.** `CopilotCli.FindInstalled()` is what `StartAsync` uses when the options
name no CLI, and a host can call it to show a user the result:

1. `PATH`, in order — on Windows only `copilot.exe`, because npm's `copilot.cmd` and `copilot.ps1`
   shims cannot be started without a shell; elsewhere a `copilot` file with an execute bit. Relative
   `PATH` entries — and on Windows drive-relative ones such as `C:bin` — are skipped.
2. The installers' own locations: on Windows the WinGet links and `GitHub.Copilot_*` package folders
   under `%LOCALAPPDATA%\Microsoft\WinGet` and npm's global package under `%APPDATA%\npm`; on macOS
   Homebrew (`/opt/homebrew/bin`, `/usr/local/bin`), `~/.local/bin` and npm's global package under
   Homebrew; on Linux `~/.local/bin`, `/usr/local/bin`, Linuxbrew and npm's global package.

The Copilot launcher Visual Studio Code puts on `PATH` (in a `copilot-shim` folder) is passed over and
reported, because it forwards to another CLI and can prompt to install one. **`COPILOT_CLI_PATH` is
never consulted**: the shell's environment does not choose the executable a confined runtime runs.
To run a specific CLI, name it:

```csharp
var options = new CopilotRuntimeOptions(dataDirectory, cliPath: @"C:\Tools\copilot\copilot.exe");
```

A path must be fully qualified and must not be a batch file: a `copilot.cmd` or `.bat` would be run by
Windows through `cmd.exe`, so it is refused — name `copilot.exe`, or for an npm installation its
`npm-loader.js`, which is run with `node` from `PATH`. A CLI named this way is version-checked like a
discovered one.

**An npm installation under a custom prefix is not found.** Discovery looks for npm's global package
only under npm's default prefixes (`%APPDATA%\npm` on Windows), and the `copilot.cmd` npm puts on `PATH`
is not a candidate. If `npm config get prefix` names another folder, name that prefix's entry point:
`cliPath: @"D:\npm-global\node_modules\@github\copilot\npm-loader.js"`.

**What else to expect from the installed CLI.** The first start of a CLI version takes several seconds
— well over half a minute under antivirus scanning — while the CLI unpacks its package into the user's
cache (`%LOCALAPPDATA%\copilot\pkg` on Windows);
later starts take a second or two. That cache, and any organization policy in its managed-settings
folder, is shared with the user's own `copilot`. The CLI's own configuration, MCP servers and plugins
are **not** used: the runtime's home is AgentKit's data directory. Its sign-in can be: see *Signing
In*.

### Bundling the Runtime Instead

A host that cannot rely on an installed CLI runs the runtime `Microsoft.Copilot.SDK` bundles, by
opting in and referencing the SDK directly:

```csharp
var options = new CopilotRuntimeOptions(dataDirectory, runtimeSource: CopilotRuntimeSource.Bundled);
```

```bash
dotnet add package Microsoft.Copilot.SDK --version 2.0.1
```

The SDK downloads its native runtime when the project that references it builds — about 100 MB from
the Copilot CLI releases on GitHub, verified against the release's published checksums — but only for
a project that references the package **directly**; reaching it through AgentKit provisions nothing.
Plan for the size: the runtime adds about 356 MB to every output directory of the host application,
in `runtimes/<rid>/native`, and about 270 MB to the project's intermediate folder per target
framework. It is published for `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`linux-musl-x64`, `linux-musl-arm64`, `osx-x64` and `osx-arm64`. Its version is pinned by the SDK's,
so no version check runs. The confinement is the same as the installed CLI's. A build that cannot
reach GitHub sets the SDK's own MSBuild properties:

- `CopilotCliReleaseBaseUrl`, or the `COPILOT_CLI_DOWNLOAD_BASE_URL` environment variable, downloads
  from a mirror.
- `CopilotCliBinaryPath` uses a runtime already on disk, and skips the download. Pointed at a
  standalone Copilot CLI executable (about 190 MB), the SDK copies only that one file.
- `CopilotCliDownloadTimeout` gives a slow link longer than the default 600 seconds.
- `CopilotSkipCliDownload` set to `true` skips the download entirely, for a project that never starts
  a bundled runtime, such as a unit-test project that references the host.

### Creating the Agent

**AgentKit starts the Copilot runtime itself.** A host calls `CopilotRuntime.StartAsync` once, hands
the runtime to the factory, and disposes the runtime when it is finished; nothing AgentKit builds on
it ever disposes it. There is no way to hand AgentKit a `CopilotClient` of your own: what reaches a
session depends on the client's mode, its data directory and its runtime's environment as much as on
the session, and none of those can be read back from a client.

```csharp
using DemaConsulting.AgentKit.Agents.Copilot;

var dataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyApp", "copilot-runtime");

await using var runtime = await CopilotRuntime.StartAsync(new CopilotRuntimeOptions(dataDirectory));

var agent = CopilotAgentFactory.Create(
    runtime,             // the host disposes the runtime; the agent never does
    tools,               // the AIFunction list composed above
    instructions: "You are a document assistant. Use the tools provided.",
    workingDirectory: "/workspace");
```

The runtime is always:

- **In Empty mode**, so the SDK supplies none of its coding-agent defaults and keeps credentials in
  the data directory rather than the system keychain.
- **A child process** running the installed Copilot CLI, or the runtime `Microsoft.Copilot.SDK`
  provisioned into the application's output when the host opts into it — the same confinement either
  way. Its environment is the host's minus every `COPILOT_` and `GITHUB_COPILOT_` variable, apart
  from `COPILOT_GITHUB_TOKEN`, `COPILOT_GH_HOST`, `COPILOT_SYSTEM_PROXY`, `COPILOT_PROXY_KERBEROS`,
  `COPILOT_PROXY_KERBEROS_SPN`, `COPILOT_TELEMETRY_DISABLED` and `COPILOT_GITHUB_TELEMETRY_DISABLED`,
  which carry only credentials, network settings or a switch that turns telemetry off. Everything else
  — `PATH`, the home and temporary folders, proxy and certificate settings, `GH_TOKEN`,
  `GITHUB_TOKEN` — passes through.
- **On a data directory AgentKit owns.** A missing or empty directory is created and marked with a
  versioned `.agentkit-copilot-runtime` file; a directory is reused, so conversations survive a
  restart, only when that file is a regular file holding exactly what AgentKit wrote; any other
  non-empty directory — a Copilot CLI home, a shared folder, the data directory an earlier release's
  client used, or one whose marker is a link, a directory or was edited — is refused with an
  `InvalidOperationException` naming it. A new or empty directory that something else writes into
  while AgentKit is claiming it is refused the same way. The marker guards against reusing another
  tool's directory by mistake; it is not a security boundary against a process that can already
  write to the directory. Keep the directory private to the application, never point the Copilot
  CLI at it, and give each runtime running at the same time a directory of its own. The runtime's
  caches are not kept there: they go to the platform's cache folder (`%LOCALAPPDATA%` on Windows,
  `XDG_CACHE_HOME` or its default elsewhere).

`CopilotRuntimeOptions` takes only the data directory and, optionally, how the runtime signs in (a
GitHub token or token provider, a GitHub host, and whether the Copilot CLI sign-in may be used), a log
level, a logger, the runtime source and a CLI path. *Signing In* below covers the sign-in options.
Once started, the runtime reports what runs: `runtime.CliPath` (the installed CLI, or `null` for the
bundled runtime), `runtime.RuntimeVersion` and `runtime.ProtocolVersion`, and who it runs as:
`runtime.SignInSource`, `runtime.GitHubHost` and `runtime.GitHubLogin`.

Copilot arrives as a complete runtime carrying its own tools — shell, fetch, file editing and more.
The factory suppresses them by deriving the session allow-list from the same tools you supplied, so
a confined agent is offered only the tools you gave it. Every entry is source-qualified as a custom
tool: a bare name would also admit a built-in tool of the same name. The factory also withholds the
runtime's other channels of injected capability on every session — skills, custom instructions,
repository hooks, host git operations and commit co-author trailers, extensions, built-in
sub-agents, memory, experimental features, embedding retrieval, and the host environment context the
runtime would write into the system prompt. It does **not** install the image-promoting decorator,
because the Copilot runtime already delivers a tool-returned image to the model.

**What AgentKit guarantees about MCP servers, per source.** On the runtime AgentKit starts, before
any session exists:

- **The built-in GitHub MCP server** is named disabled, so it is not started or authenticated. Empty
  mode alone would start it on the first turn.
- **User-configured servers** cannot exist in a fresh data directory, and any added later are found by
  the runtime's discovery and disabled before the session is created. Empty mode alone would run them.
- **Workspace MCP configuration** is not loaded, because configuration discovery is off and the data
  directory trusts no folder. A workspace server that discovery reports is disabled as well.
- **Plugins**: none is installed in a fresh data directory, and plugin directories cannot be
  configured.

These sources are prevented: the opt-in live suite shows the built-in server never starting, probe
servers in the data directory and in a workspace never executing, and no plugin installed, while each
of those servers does start without its protection.
Servers from a managed catalog or a signed-in account follow organization policy and the account.
The SDK cannot enumerate them before a session exists, so one may start, and authenticate, while the
session is created. AgentKit then disables every server the session's MCP configuration lists, before
any turn runs, and refuses the session if one stays enabled. This contains such a server; it does not
prevent it. The session's configuration listing is not complete — it does not report the built-in
GitHub server even while that server runs — so that check is defense in depth, not proof of absence.
In every case, no MCP tool is on a session's allow-list.

**The working directory**: AgentKit sets no working directory on the runtime process, so pass
`workingDirectory:` to the factory — typically the root of the workspace the tools are confined to.
Omit it and the runtime uses the host process's working directory. It grants nothing: what the agent
may read or write is decided by the tools' own path policy.
**Choosing the model**: pass `model:` to name the Copilot model backing the session:

```csharp
var agent = CopilotAgentFactory.Create(
    runtime,
    tools,
    instructions: "You are a document assistant. Use the tools provided.",
    model: "gpt-5.4-mini");
```

Omit it and the Copilot runtime applies its own default. The choice is worth making deliberately:
model capability drives how reliably an agent uses its tools and how accurately it reads an image,
the same way it does when you pick an Ollama model for an `IChatClient` agent. Select the model and
the working directory *through* the factory rather than by building a session configuration
yourself — a hand-built session forgoes the built-in tool suppression and the default-safe
permission handler.

### Signing In

The runtime signs in to GitHub from the first of these sources that applies:

1. **A token from the application**: `gitHubToken:`, or `gitHubTokenProvider:` for an application
   that refreshes its own tokens.
2. **A token in the environment**: `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN`.
3. **The user's Copilot CLI sign-in** (`copilot login`), unless the application passes
   `useCopilotCliSignIn: false`.
4. **The runtime's own fallbacks**: a sign-in stored in AgentKit's data directory, then the GitHub CLI
   (`gh auth login`; the runtime runs `gh auth token`, so `gh` must be on `PATH`).

This is the order the Copilot CLI itself uses, so the runtime signs in as `copilot` would in the same
shell. A token in the environment comes before the stored sign-ins on purpose: it is set for one
process or job — CI, a container, a service account — and a sign-in stored for the user should not
quietly replace it. If a stray `GITHUB_TOKEN` is set for another tool, unset it for the application,
or pass `gitHubToken:` explicitly.

**What the Copilot CLI sign-in shares.** Only the login. AgentKit reads the signed-in account's host and login from the
Copilot CLI's `config.json` (in `COPILOT_HOME`, or `.copilot` in the user's profile; a `COPILOT_HOME` that is not a
valid path is reported, and the next source applies) and asks the Copilot CLI itself for that account's token: it runs
the same CLI once more, for a moment, on a new temporary folder holding nothing but that host and login, with no
session, and deletes the folder afterwards. The CLI reads the token from the system credential store, where
`copilot login` put it; AgentKit never reads the credential store itself. The runtime then receives the token and the
host, nothing else: none of the user's MCP servers, hooks, agents, skills, plugins, instructions, settings or history,
and its data directory stays private. The token is kept in memory and handed only to the runtime process, in its
environment; AgentKit never writes or logs it. This adds a second or two to the start. Any token AgentKit was given or
obtained, or that your token provider returns, is also removed from the runtime and SDK text AgentKit quotes in its own
messages — a failed start, a session error that fails an agent's run, a provider session's refusal — along with any text
shaped like a GitHub token. When AgentKit keeps a runtime or SDK failure as an inner exception, it keeps a redacted
copy: the original's type name, message and stack trace, with the same text removed, so no exception in the chain
carries a token. When a runtime or SDK failure reaches you — a turn that fails on an agent or a provider session, or a
consolidation that fails — it arrives as a new exception of the same kind (`IOException`, `TimeoutException` or
`InvalidOperationException`; any other type as `InvalidOperationException`), with the message redacted and that
redacted copy as its inner exception. A cancellation or disposal that carries nothing to redact reaches you unchanged.

**GitHub Enterprise Cloud hosts.** Each source is used with its own host. The Copilot CLI sign-in
uses its account's host. A token needs its host too: GitHub refuses a token for a `*.ghe.com` account
used against `github.com` with 401 Bad credentials. Pass the host as `gitHubHost:`, or set
`COPILOT_GH_HOST`:

```csharp
var options = new CopilotRuntimeOptions(
    dataDirectory,
    gitHubToken: token,
    gitHubHost: "example.ghe.com");
```

When a host is chosen this way, the Copilot CLI sign-in is used only if its account is on that host.
`COPILOT_GH_HOST` must be a host name or an https URL without a path, query, user name or port, as
`gitHubHost:` must: any other value stops the start with an explanation, unless `gitHubHost:` is
passed, which takes precedence over it. A valid value is handed to the runtime as a bare host name,
whatever form it was written in, so the runtime always signs in to the host the token is for.

**A token provider** is asked once when the runtime starts, and again by each session the runtime
opens, so the runtime can refresh a token before it expires. It receives the host and whether the
token replaces one about to expire, may be called from any thread, and returns a
`CopilotGitHubToken` with the token's remaining lifetime when known:

```csharp
var options = new CopilotRuntimeOptions(
    dataDirectory,
    gitHubHost: "example.ghe.com",
    gitHubTokenProvider: async (request, cancellationToken) =>
        new CopilotGitHubToken(await tokenStore.GetTokenAsync(request.Host, cancellationToken)));
```

Pass either a token or a provider, not both. A provider that fails or returns no token stops the
start, with the provider's error as the inner exception; a session whose provider fails is refused.

**Which account it is.** `runtime.SignInSource` says which source was used, and `runtime.GitHubHost`
and `runtime.GitHubLogin` which account; with a logger, the start logs the same, and warns when the
runtime signed in on another account than the Copilot CLI's. The account decides which models are
offered: a GitHub Enterprise Cloud account may offer different models from a `github.com` one.

**Platforms.** The Copilot CLI sign-in has been tested on Windows with the installed CLI. It has not
been tested on macOS, where the Copilot CLI may ask the user to allow access to its keychain item (an
unanswered prompt ends the attempt after 60 seconds), on Linux, or with the bundled runtime. Wherever
it does not work, AgentKit moves on to the next source, and the reason appears in the start's log and
in the message if no source signs the runtime in.

**Signing in from the application, later.** The runtime can also run GitHub's device-code sign-in and
keep the result in AgentKit's data directory. AgentKit does not offer that yet: the runtime would store
the token in plain text in the data directory, and each sign-in creates a new token. It may be added
later, with that caveat documented.

### Permissions Are Default-Deny

The Copilot runtime asks a permission handler before it runs anything, including every call to one of
your own tools. When you supply no handler, the factory installs one that approves exactly the tools
you supplied, by name, **one call at a time**, and rejects every other request — every built-in
shell, read, write and fetch request, and every tool you did not supply. Your tools still enforce
their own path policy; the permission handler is a second, independent gate in front of everything
else.

Every session's permission mode is pinned to `Manual`, so every request reaches the handler. The
runtime's other modes would approve requests without asking it.

A handler you supply **replaces** the default; it is not composed with it. A host that supplies one
therefore forfeits default-deny unless its handler reproduces it — for example, to log each decision:

```csharp
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;

var allowed = tools.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

#pragma warning disable MCPSDK001 // the SDK marks the permission-decision types evaluation-only
var agent = CopilotAgentFactory.Create(
    runtime,
    tools,
    onPermissionRequest: (request, invocation) =>
    {
        var approved = request is PermissionRequestCustomTool custom && allowed.Contains(custom.ToolName);
        Console.WriteLine($"{request.Kind}: {(approved ? "approved" : "rejected")}");
        return Task.FromResult(approved
            ? PermissionDecision.ApproveOnce()
            : PermissionDecision.Reject("Not an allowed tool."));
    });
#pragma warning restore MCPSDK001
```

With retention composed, `tools` includes the two readers, so a handler built from it approves them
too.

A session the session engine drives through `CopilotProviderSessionFactory` always takes the
default handler; a host that wants a policy of its own composes an agent, where that choice is
offered.

### Tool Names the Runtime Reserves

The runtime refuses to create a session that registers a custom tool under the name of one of its
built-in tools. The factory therefore refuses such a tool up front, with an `ArgumentException`
naming it, before any session exists: `view`, `create`, `edit`, `glob`, `grep`, `powershell`,
`bash`, `task`, `skill`, `web_fetch`, `web_search` and the rest of the runtime's built-in set,
compared case-insensitively. The set is the union across platforms, so an application composed on
Windows does not fail later on Linux because of `bash`. Rename the tool. AgentKit's own tool
families are prefixed by family and never collide; the rule matters for tools you write yourself.

### Large Tool Results

The runtime would normally divert a tool result larger than about 20 KB into a temporary file and
hand the model the file's path instead — a file no confined tool can read. The factory disables that
diversion, so every tool result reaches the model inline. AgentKit's tools bound their own output
through the policy's tool limits, which is where the size of a result belongs; a tool you write
yourself should bound its output the same way.

To keep a result too large for the limit instead of having it refused, compose the tool list with
retention (see *Large Tool Outputs*). AgentKit's retention is the large-output mechanism on Copilot
as on every other provider: the output stays in memory, the model receives a bounded preview inline,
and `tool_output_read` and `tool_output_search` are published, allow-listed and approved like your
other tools. The runtime's diversion stays off either way.

### Running a Conversation

A conversation is a `CopilotAgentSession`. The runtime holds the conversation's history, so the
session object only names it: the first run creates a runtime session and records its identifier in
`SessionId`, and every later run resumes that session, confined exactly as before. Runs on one
session are serialized, so two concurrent first runs cannot fork the conversation. Serializing a
session does not wait for a run in progress; serialize after a run returns to be sure the identifier
it recorded is included.

```csharp
var session = (CopilotAgentSession)await agent.CreateSessionAsync();

// RunAsync gathers the whole response; RunStreamingAsync yields it as it arrives.
await foreach (var update in agent.RunStreamingAsync("Summarize welcome.txt.", session))
{
    Console.Write(update.Text);
}
```

A streamed run yields the answer's text as it is generated, a `FunctionCallContent` and a
`FunctionResultContent` for every tool call, and a `UsageContent` reporting the turn's token usage.
An error the runtime reports for the session fails the run with an `InvalidOperationException`.

**Attachments**: an image or any other `DataContent` in the input is sent to the runtime as an
in-memory attachment — nothing is written to disk, and no file permission is needed:

```csharp
var bytes = await File.ReadAllBytesAsync("diagram.png");
var message = new ChatMessage(
    ChatRole.User,
    [new TextContent("Describe this diagram."), new DataContent(bytes, "image/png") { Name = "diagram.png" }]);

var response = await agent.RunAsync(message, session);
```

`UriContent` and `HostedFileContent` are not sent, and `AgentRunOptions` is ignored.

**Resuming later**: after every run — successful, failed or canceled — the agent releases the
runtime session without deleting it, so the conversation stays resumable under the runtime's data
directory. (A run made without a session has nothing that could resume it, so its runtime session is
deleted when the run ends.) Serialize the session to keep it, and deserialize it on any agent built over a runtime with
the same data directory, in this process or a later one:

```csharp
var saved = await agent.SerializeSessionAsync(session);   // { "sessionId": …, "stateBag": … }

// Later, possibly in another process:
var resumed = (CopilotAgentSession)await agent.DeserializeSessionAsync(saved);
await agent.RunAsync("Where did we leave off?", resumed);

// Finished with the conversation: deleting it is the host's decision.
if (resumed.SessionId is { } sessionId)
{
    await runtime.DeleteSessionAsync(sessionId);
}
```

There is no separate resume-by-identifier method; deserializing is the route. A session serialized
by the earlier Agent Framework Copilot adapter has the same shape and remains readable.

The runtime cannot resume a session that never took a turn: if a conversation's first run failed or
was canceled before its turn reached the runtime, a later run fails with the runtime's "Session not
found" error, so start a new `CopilotAgentSession` instead.

### Provider Sessions on Copilot

This package also supplies an `IProviderSession` for the Copilot runtime, so a Copilot conversation
can run on an AgentKit compacting session exactly as an `IChatClient` one does — see
`CopilotProviderSessionFactory` and `CopilotSummarizer`, and *Sessions* below. Every session they
create is confined exactly as an agent's is. `CopilotProviderSessionFactory` accepts the same
optional `workingDirectory`; `CopilotSummarizer` takes none, because a consolidation session is
tool-free. Because Copilot compacts its own sessions by default, at a threshold barely above the one
AgentKit rotates at, every session AgentKit drives is created with the runtime's compaction threshold
raised clear of that rotation point, so the two compactors never act on one conversation.

Copilot differs from the `IChatClient` family in two ways worth knowing. It reports **both** its
occupancy and its context window with every turn, so you never state a window; and because its
windows are large, `CopilotProviderSessionFactory` accepts an optional ceiling that lowers the
window a session accounts against. The ceiling only ever lowers — one above the runtime's own limit
is ignored — and setting one costs more rather than less, because a repeated prompt is served
almost entirely from cache while a rotation starts a fresh one and adds a summarizer call. Set it
for answer quality across a long conversation, not to save money.

### Troubleshooting the Copilot Runtime

- **`InvalidOperationException`: "AgentKit could not find an installed GitHub Copilot CLI".** No CLI
  is on `PATH` or where its installers put it, or the only one found was an editor's launcher. The
  message lists where AgentKit looked. An npm installation under a custom prefix is not searched; name
  its `npm-loader.js` with `cliPath:`. Otherwise install the CLI (see *Installing the Copilot CLI*),
  name one with `cliPath:`, or opt into the bundled runtime.
- **`InvalidOperationException`: the Copilot CLI "is version … which is too old for AgentKit".** The
  version built into the installed CLI is older than `CopilotCli.MinimumVersion`. Check it with
  `copilot --no-auto-update --version` — not `copilot --version`, which can report a newer version
  from the CLI's package cache that AgentKit does not run — and upgrade the installation itself
  (`winget upgrade GitHub.Copilot`, `brew upgrade --cask copilot-cli`,
  `npm install -g @github/copilot@latest`, or the install script again).
- **`InvalidOperationException` mentioning an "SDK protocol version mismatch".** The runtime speaks a
  protocol this release does not. When the CLI's protocol is older, upgrade it as above; when it is
  **newer**, the CLI is ahead of this release of AgentKit: update AgentKit, run a supported CLI
  through `cliPath:`, or opt into the bundled runtime.
- **`InvalidOperationException`: "AgentKit could not read the version of the Copilot CLI".** The CLI
  could not be started, exited with an error, or did not answer within two minutes; the message carries
  its error output. An npm installation also needs `node` on `PATH`.
- **`ArgumentException`: the Copilot CLI path "is a batch file".** The `cliPath` names a `copilot.cmd`
  or `.bat`, which Windows would run through `cmd.exe`. Name `copilot.exe` — such as the one
  `CopilotCli.FindInstalled()` returns — or, for an npm installation, the package's `npm-loader.js`.
- **`InvalidOperationException`: the runtime "started but is not signed in".** The message lists each
  sign-in source in order and what it found. Sign in with `copilot login` (add
  `--host https://<name>.ghe.com` for a GitHub Enterprise Cloud account) or `gh auth login` with `gh`
  on `PATH`, or pass `gitHubToken:` or `gitHubTokenProvider:`, with `gitHubHost:` for a `*.ghe.com`
  account. When a token was used and not accepted, the message says which: a GitHub Enterprise Cloud
  token without its host is the usual cause, and an environment token that is not meant for the
  application can be unset.
- **The runtime signed in as an unexpected account or host.** Check `runtime.SignInSource`,
  `runtime.GitHubHost` and `runtime.GitHubLogin`. A token in `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or
  `GITHUB_TOKEN` comes before the Copilot CLI sign-in; otherwise the Copilot CLI's account comes before
  the GitHub CLI's. Pass `gitHubHost:` to choose a host, or `useCopilotCliSignIn: false` to skip the
  Copilot CLI sign-in.
- **`InvalidOperationException`: "The COPILOT_GH_HOST environment variable is not a valid GitHub
  host".** `COPILOT_GH_HOST` is set to something other than a host name such as `example.ghe.com` or
  an https URL such as `https://example.ghe.com` — for example an `http` URL, or one with a path or
  port. The message does not repeat the value. Correct it, unset it to sign in to `github.com`, or
  pass `gitHubHost:`, which takes precedence over it.
- **The Copilot CLI account was found but its token could not be read.** The message names the account
  and the reason. Run `copilot` once to check that it is still signed in (`copilot login` again if
  not); on macOS, allow access to the keychain item if asked. AgentKit moves on to the GitHub CLI
  meanwhile.
- **`InvalidOperationException`: the runtime "could not list models".** The runtime's connection was
  lost or its sign-in lapsed; start a new runtime, signed in as above. The inner exception is a
  redacted copy of the SDK's error: its `ToString()` starts with the original type name.
- **The runtime fails to start.** `CopilotRuntime.StartAsync` raises an `InvalidOperationException`
  carrying the runtime's own error, redacted. The inner exception is a redacted copy of the runtime's
  failure — its type name, message and stack trace, with any token removed — so read its message or
  `ToString()` rather than its type. On the installed CLI the message names the CLI and its version.
  On the bundled runtime the runtime is most often missing from the application's output: the host
  project does not reference `Microsoft.Copilot.SDK` directly, `CopilotSkipCliDownload` is set for the
  host, or the platform is one the runtime does not support — it is published for `win-x64`,
  `win-arm64`, `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64`, `osx-x64` and
  `osx-arm64`. A Copilot CLI shell's `COPILOT_CLI_PATH` is no longer a cause, because the runtime
  never receives it and discovery ignores it. Of the host's `COPILOT_` variables, only the documented
  pass-through set reaches the runtime.
- **`InvalidOperationException`: the data directory "is not empty and was not created by AgentKit".**
  The directory holds something AgentKit did not put there — a Copilot CLI home, or the data directory
  an earlier release's client used — or something wrote into a new directory while AgentKit was
  claiming it. Give the runtime a new or empty directory of the application's own.
- **`InvalidOperationException`: the data directory's `.agentkit-copilot-runtime` entry "is not the
  marker AgentKit writes".** The directory has an entry with the marker's name, but it is a link, a
  directory, or a file whose content is not exactly what AgentKit writes — for example a marker that
  was edited, or one written by an earlier pre-release build. The message gives the reason. If the
  application created the directory, delete it and let AgentKit create it again; the conversations
  kept in it are lost. Otherwise give the runtime a new or empty directory of the application's own.
- **Build warning or error `MCPSDK001`.** The SDK marks its permission-decision types as being for
  evaluation purposes, and any code that references them — a permission handler of your own — raises
  that diagnostic. Suppress it around the handler, as above, or add it to the project's `NoWarn`.
- **`ArgumentException` naming a tool.** One of your tools carries the name of a runtime built-in;
  see *Tool Names the Runtime Reserves* above.
- **`InvalidOperationException`: the runtime "kept the MCP server(s) … enabled".** AgentKit disabled
  an MCP server for the session and the runtime still reported it enabled, so the session was refused
  rather than handed to the model with the server available. Check for MCP servers provided by a
  managed catalog or by the signed-in account or its organization. A resumed
  conversation that is refused this way is kept, not deleted.
- **The session is refused while AgentKit disables an MCP server — a `RemoteRpcException` from the
  runtime's disable call, or the "kept … enabled" error above.** The server may be an *owned* MCP
  installation, which the runtime's session-scoped disable may require an installation id to disable.
  The SDK's listing of a session's MCP servers does not expose that id, so AgentKit cannot supply it
  and refuses the session instead. Use a data directory with no MCP installations.
- **A rejected permission request in the runtime's log.** The model asked for something other than
  one of your tools — a built-in shell or file read, say — and default-deny refused it. That is the
  intended outcome.

### Migrating to the Installed Copilot CLI

This release changes the default runtime from the one the SDK bundles to the Copilot CLI installed on
the machine. These are breaking changes, accepted while the package is pre-1.0:

- **The default runtime is the installed CLI.** A machine running the application needs the Copilot
  CLI, version 1.0.113 or later; see *Installing the Copilot CLI*. To keep the bundled runtime, pass
  `runtimeSource: CopilotRuntimeSource.Bundled` to `CopilotRuntimeOptions` and keep the host's direct
  `Microsoft.Copilot.SDK` reference. Otherwise that reference can be removed, which removes the
  runtime from the application's output.
- **`CopilotRuntimeOptions`'s constructor gained two optional parameters**, `runtimeSource` and
  `cliPath`. Source that calls it compiles unchanged, but a host built against an earlier release must
  be recompiled.
- **`StartAsync` now refuses more.** It refuses an installed CLI that is missing or too old before
  anything starts, and a runtime that started but is not signed in; each refusal says what to do. On
  the installed CLI it also takes longer: it runs the CLI once to read its version, and the first
  start of a CLI version unpacks it.

### Migrating to the Copilot CLI Sign-In

This release lets the runtime sign in with the user's Copilot CLI sign-in, and against the right
GitHub host:

- **The account may change.** With no token from the application or the environment, the runtime
  now signs in as the Copilot CLI's account before trying the GitHub CLI. A machine whose GitHub CLI
  is signed in to `github.com` while the Copilot CLI is signed in to a GitHub Enterprise Cloud host
  now runs on the Copilot CLI's account and host, whose models and policies may differ. Check
  `runtime.SignInSource`, `runtime.GitHubHost` and `runtime.GitHubLogin`, and pass
  `useCopilotCliSignIn: false` to keep the earlier behavior.
- **`CopilotRuntimeOptions`'s constructor gained three optional parameters**, `gitHubHost`,
  `gitHubTokenProvider` and `useCopilotCliSignIn`. Source that calls it compiles unchanged, but a host
  built against an earlier release must be recompiled.
- **The start can take a second or two longer** when the Copilot CLI sign-in is used, while the CLI
  reads the account's token.
- **The "not signed in" message has changed.** It now lists what each source found. Code that matched
  its earlier text should match the exception type instead.
- **An invalid `COPILOT_GH_HOST` now stops the start.** It was ignored: AgentKit resolved the sign-in
  for `github.com` while the runtime still received the invalid value. Correct or unset it, or pass
  `gitHubHost:`. A valid `COPILOT_GH_HOST` written as a URL is now handed to the runtime as a bare
  host name.
- **A failed start's and a failed model listing's inner exception is a redacted copy.** It is no
  longer the runtime's or the SDK's own exception, but a copy carrying its type name, message and
  stack trace with any token removed; the outer exception is still an `InvalidOperationException`.
  Code that tested the inner exception's type, for example for a `FileNotFoundException`, should read
  its message or `ToString()`, which starts with the original type name.
- **A failed turn or consolidation raises a redacted copy, not the SDK's exception.** A turn that fails
  on an agent or a provider session, and a consolidation that fails, now raise a new exception of the
  same kind with the message redacted and a redacted copy of the SDK's exception as the inner
  exception. An `IOException`, `TimeoutException` or `InvalidOperationException` keeps its type; any
  other type — for example the RPC transport's `RemoteInvocationException` — now arrives as an
  `InvalidOperationException`. Code that caught such a type should catch `InvalidOperationException`
  and read the inner exception's `ToString()`, which starts with the original type name.

### Migrating from the Earlier Adapter

Earlier releases of this package wrapped the Agent Framework's `Microsoft.Agents.AI.GitHub.Copilot`
adapter over `GitHub.Copilot.SDK` 1.0.x. Moving to the current release:

- Replace `using GitHub.Copilot;` with `using Microsoft.Copilot;`, and `using Microsoft.Copilot.Rpc;`
  where you reference permission decisions.
- Install the Copilot CLI (see *Installing the Copilot CLI*), or, to bundle the runtime, add a direct
  `Microsoft.Copilot.SDK` package reference to the host project and opt into
  `CopilotRuntimeSource.Bundled`.
- Replace the client you constructed with `await CopilotRuntime.StartAsync(new
  CopilotRuntimeOptions(dataDirectory))`, passing a GitHub token through `gitHubToken:` if you set one
  on the client, and pass the runtime wherever you passed the client: `CopilotAgentFactory.Create`,
  `CopilotProviderSessionFactory` and `CopilotSummarizer`. Remove `CopilotClientStarter`; the runtime
  explains a failed start itself. Dispose the runtime where you disposed the client, and delete a
  finished conversation with `runtime.DeleteSessionAsync`.
- Give the runtime a new data directory: the one your client used is not empty and was not created by
  AgentKit, so it is refused with the `InvalidOperationException` described under *Troubleshooting
  the Copilot Runtime*. The same holds for a Copilot CLI home. This refusal is a deliberate breaking
  change, accepted while the package is pre-1.0. There is no migration path for a client's data
  directory, and conversations kept there are not carried over.
- Move a client-level working directory to the `workingDirectory:` parameter of
  `CopilotAgentFactory.Create` or `CopilotProviderSessionFactory`.
- Replace a `GHCP001` suppression with `MCPSDK001`.
- Rename any tool of your own that carries a built-in tool's name.
- Replace references to `GitHubCopilotAgent` and `GitHubCopilotAgentSession` with `AIAgent` and
  `CopilotAgentSession`. `SessionId` keeps its meaning, and serialized sessions remain readable.
- Expect every session's permission mode to be `Manual`, whatever ambient configuration would pick.

# Using AgentKit Tools in Any Agent

Every AgentKit tool is a plain `Microsoft.Extensions.AI` `AIFunction`. An application that already
owns its agent loop — a Microsoft Agent Framework agent it builds itself, a raw `IChatClient`
pipeline, or a GitHub Copilot SDK session — can use the tools there unchanged. The controls around the
tools are available as building blocks that such a runtime accepts: result ceilings for a tool you did
not build, a retention handle per conversation, a context provider that injects tools per session,
image promotion for a pipeline you build, and a chat reducer for a history you own.

## Tools Are Plain AIFunctions

Compose the tools exactly as before and hand the list to the runtime:

```csharp
IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .Add(new TextFilePack())
    .Add(new FilePack())
    .WithToolOutputSpill()
    .Build();
```

**A Microsoft Agent Framework agent you build.** `ChatClientAgent` takes `IList<AITool>`, so spread
the list. Install image promotion beneath the agent's own function-invocation loop when the provider
needs it (Ollama through OllamaSharp does; see *Delivering Tool Images to the Model*):

```csharp
var agent = new ChatClientAgent(
    ollamaClient.AsBuilder().UseImagePromotion().Build(),
    instructions: "Paths are relative to the workspace.",
    tools: [.. tools]);
```

**A raw `IChatClient` pipeline.** Add function invocation, then image promotion beneath it, and offer
the tools on each request:

```csharp
IChatClient client = providerClient.AsBuilder()
    .UseFunctionInvocation()
    .UseImagePromotion()
    .Build();

var response = await client.GetResponseAsync(history, new ChatOptions { Tools = [.. tools] });
```

**A raw GitHub Copilot SDK session.** `CreateSessionOptions.Tools.Definitions` accepts the functions
directly. In `CopilotClientMode.Empty` the session must also name every tool it may call in
`Tools.AvailableTools` as `custom:<name>`, and a manual permission handler must approve them:

```csharp
var options = new CreateSessionOptions { Model = model };
options.Tools.Definitions = [.. tools];
options.Tools.AvailableTools = [.. tools.Select(tool => "custom:" + tool.Name)];
options.Permissions.Mode = PermissionMode.Manual;
options.Permissions.OnRequest = (request, _) => Task.FromResult(
    request is PermissionRequestCustomTool custom && tools.Any(tool => tool.Name == custom.ToolName)
        ? PermissionDecision.ApproveOnce()
        : PermissionDecision.Reject("Not an application tool."));
```

That is enough for the tools to work, but not for the session to be confined: a raw session still
reaches whatever MCP servers, skills, instructions, hooks, memory and sub-agents the runtime finds, and
the runtime's environment and data directory are the host's. Prefer `CopilotAgentFactory` (see
*The GitHub Copilot SDK*), which confines all of them. The Copilot runtime delivers tool-returned
images itself, so no image promotion is needed there.

## Bounding a Tool You Did Not Build

AgentKit's own tools enforce `ToolLimits` internally. A tool you wrote with `AIFunctionFactory`, or
took from another library, does not — a 300-row report tool returned 6,350 characters against a
1,500-character limit. `AsGuarded` puts the same ceilings around any function:

```csharp
AIFunction report = AIFunctionFactory.Create(
    (int rows) => Enumerable.Range(1, rows).Select(n => new { row = n, square = n * n }),
    "report_rows",
    "Returns one row per number with its square.");

AIFunction bounded = report.AsGuarded(new ToolLimits(maxResultCharacters: 1_500));
```

The bounded function refuses, as `Denied (ResourceTooLarge)` naming the size and the limit, a result
whose text exceeds `MaxResultCharacters` or whose binary content exceeds `MaxBinaryBytes`. Structured
results are measured as the JSON the provider would receive. Everything else about the function — its
name, description, schema and an approval requirement — passes through unchanged. Only the result is
bounded: `MaxReadBytes`, `MaxImagePixels`, `MaxAgentDepth` and `MaxDeleteEntries` describe how a tool
does its work, which a wrapper cannot see. Wrapped by a retention (below), an oversized text result is
retained for paging instead of refused.

## Retaining Outputs per Conversation

`WithToolOutputSpill` and `ToolOutputSpill.Apply` retain outputs for one tool list. When tools are
added to a conversation over time, or the conversation is persisted, own the retention instead:

```csharp
var retention = new ToolOutputRetention(limits);   // one per conversation

List<AITool> agentTools = [.. retention.Wrap(tools), .. retention.ReaderTools];

// Later in the same conversation, a tool added on demand shares the same references.
agentTools.Add(retention.Wrap(bounded));
```

`Wrap` covers a tool at any time, and every tool one retention wraps numbers its references in one
sequence; `ReaderTools` are `tool_output_read` and `tool_output_search` over that retention. To keep
references valid across a restart, export the retention with the conversation and restore it:

```csharp
string json = JsonSerializer.Serialize(retention.Export(), ToolOutputRetentionSnapshot.JsonTypeInfo);
// ... later ...
var snapshot = JsonSerializer.Deserialize(json, ToolOutputRetentionSnapshot.JsonTypeInfo)!;
var restored = ToolOutputRetention.Restore(snapshot, limits);
```

A restored retention answers the same references with the same content, observes the ceilings it is
restored with — evicting the oldest outputs first if they are smaller — and continues numbering after
every reference ever issued, so an old reference is never re-issued to different content. A snapshot
that could not have been exported — a malformed or repeated reference, a reference above its counter,
a counter too large to continue numbering from, or lines that are not normalized — is rejected with
`ArgumentException` rather than partly restored,
and a snapshot keeps its own copy of what it was built from.
A snapshot holds the full text of the retained outputs, up to 16,777,216 characters by default: it has
the same privacy class as the chat history and should be stored and sized accordingly.

## Injecting Tools per Session

`ToolPackContextProvider` gives a `ChatClientAgent` you build AgentKit's tools per conversation, with
no tools on the agent itself:

```csharp
var provider = new ToolPackContextProvider(
    policy,
    [new TextFilePack(), new FilePack(), new TodoPack()],
    new ToolPackContextProviderOptions
    {
        Instructions = "Paths are relative to the workspace.",
        ToolOutput = ToolOutputOptions.Default
    });

var agent = new ChatClientAgent(
    providerClient.AsBuilder().UseImagePromotion().Build(),
    new ChatClientAgentOptions { Name = "assistant", AIContextProviders = [provider] });
```

The provider composes the packs once per `AgentSession` — each conversation gets its own retained
outputs, task list and memory — and contributes them, with the instructions, on every run. A second
constructor takes any function list instead of packs and bounds each function with `AsGuarded`.

**Retention survives the agent persisting the conversation.** With `ToolOutput` set, the provider
writes each conversation's retention to the session's state bag after every run, so
`SerializeSessionAsync` carries it and a session restored with `DeserializeSessionAsync` still reads
its earlier references. Set `PersistRetainedOutputs = false` to persist only the reference counter: a
reference made before a reload is then answered "no longer retained", never re-issued. Task lists and
in-memory memory stores are not persisted; a reloaded conversation starts them empty. A session
serialized while a run is in flight carries the state of the previous run. One provider serves any
number of conversations at once, but runs of one session must not overlap, as the Agent Framework
requires; if they do, the provider still persists the newest retention state. Give each provider its own
`StateKey` when an agent has two.

**Only `ChatClientAgent` uses contributed tools.** Any other agent would accept the provider and never
offer the tools to the model, so the provider refuses to run for it. For a Copilot agent, hand the
tools to `CopilotAgentFactory`; for a loop of your own, use `ToolOutputRetention` directly.

## Reducing a Host-Owned History

A host that owns its history can consolidate it with AgentKit's consolidation prompt through
`AgentKitChatReducer`, an `IChatReducer`. In the Agent Framework, plug it into the in-memory history
provider:

```csharp
var history = new InMemoryChatHistoryProvider(new InMemoryChatHistoryProviderOptions
{
    ChatReducer = new AgentKitChatReducer(
        new ChatClientSummarizer(summarizingClient),
        thresholdCharacters: 40_000,
        verbatimTurns: 4),
    ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.BeforeMessagesRetrieval
});

var agent = new ChatClientAgent(client, new ChatClientAgentOptions { ChatHistoryProvider = history });
```

Over the threshold, every message older than the last `verbatimTurns` turns is consolidated through the
summarizer into one assistant message opening with `[Consolidated record of the earlier conversation]`
on its own first line; system messages and the kept turns are untouched, a tool call is never separated
from its result, and a previous record is folded into the next. Only a message of exactly that shape is
treated as a previous record, so an ordinary reply that happens to begin with those words is
consolidated as what the assistant said. Images are described by type and size, never sent to the
summarizer.

**It is a reduced-fidelity option.** Compared with `CompactingAgentSession` it loses:

| `CompactingAgentSession` | `AgentKitChatReducer` |
| --- | --- |
| Tiered slots, older tiers coarser, a verbatim tier | One record, re-consolidated each time |
| Compaction level chosen from occupancy, with hysteresis | One fixed level; a reduction can even grow the history |
| Rotation into a fresh provider session | None; the history is trimmed in place |
| The provider's own token usage | Characters estimated from the messages |
| Works on GitHub Copilot, where the runtime owns history | Unavailable there: Copilot has no reducer hook |

Use `CompactingAgentSession` (see *Sessions*) for GitHub Copilot and wherever full fidelity matters;
use the reducer when your host already owns its history and you want AgentKit's consolidation in the
hook it has.

# Sessions

## Why a Session Is Core

A conversation eventually fills the provider's context window. What happens then is the provider's
decision unless the application takes it — and the provider's decision differs by provider, is
usually undocumented, and is never something an agent's author can reason about. AgentKit takes that
decision, in the same package as the tool contract, because an agent whose tools are guarded but
which forgets its own work after twenty turns is not much use.

The session is an AgentKit-owned object. It keeps its own transcript **out of session**, never
asking the live conversation to summarize itself — doing that spends the context the summary exists
to reclaim, and provokes the provider's own compactor. When the conversation approaches the window,
the session consolidates older history into records, creates a fresh provider session seeded with
them, and only then disposes the one it replaced. Replacement is the only reduction both provider
shapes support — one re-sends the history on every turn, the other holds it server-side — which is
why the behavior is identical on either.

## The Structure Is Counts, Not Tokens

Imagine a notebook. The last few pages are the full story. Behind them are summary cards. When the
notebook is nearly full, the oldest full pages are squashed onto one card. There is room for four
cards; when a fifth is needed, all four are squashed onto one bigger card on the next shelf up.
There are three shelves. When the top shelf is full and another card arrives, the oldest card goes
in the bin. If the notebook keeps filling quickly, the cards get shorter, fewer full pages are kept,
and the oldest card is binned sooner.

That is the whole arrangement: a verbatim tail of recent turns, three tiers of at most four
consolidated records each, and an oldest record discarded when the coarsest tier overflows. Nothing
is weighed against a token budget, and no record is measured against a size it was asked to hit.

**Tokens serve exactly one purpose: noticing that the window is filling.** The provider session
answers one question — how full am I, out of how much — and the engine believes it. That is why
there is no token estimator, no configurable budget, and no rule for deciding between two sources of
truth.

## Running a Session

An application states three things: a summarizer, a provider-session factory, and how much recent
history to keep word for word. Everything else is the library's.

```csharp
using DemaConsulting.AgentKit.Agents.ChatClient;
using DemaConsulting.AgentKit.Core;

// The window is a fact about the provider, so the provider side answers for it.
var providerSessions = new ChatClientProviderSessionFactory(chatClient, windowTokens: 32768);

// Consolidation runs outside the conversation it compacts, on a client of the application's
// choosing. A smaller, cheaper model is usually right: consolidation is summarization, not
// reasoning.
var summarizer = new ChatClientSummarizer(summaryChatClient);

var options = new AgentSessionOptions(
    summarizer,
    instructions: "You are a research assistant confined to the permitted locations.",
    tools: [.. tools],
    verbatimTurns: 20);

await using var session = await CompactingAgentSession.CreateAsync(options, providerSessions);

var turn = await session.SendAsync("Review every document in the corpus.");
Console.WriteLine(turn.Text);
```

`AgentSessionOptions` requires only the summarizer, because compaction cannot happen without one and
defaulting it would hand an application a session that silently never compacts. The instructions and
tools are carried unchanged across every rotation — rotation replaces history, never capability.
`verbatimTurns` is a **ceiling, not a floor**: the tail holds at most that many turns and holds
fewer under pressure.

The provider's context window is deliberately *not* configured here. It is a fact about the
provider, so it belongs where the provider is constructed; carrying a second copy in the options
invited the two to disagree, and left the session deciding which to believe.

**Hand the factory the client that talks to your provider, not a pipeline.** It builds the pipeline
itself: a prompt-size recorder directly around the client you supply, and the function-invocation
loop above that. Both placements matter, and getting either wrong is silent. The recorder must sit
underneath because a turn that calls tools is several requests, and the response the loop finally
returns reports their input tokens *added together* — read as occupancy, that has a tool-using agent
conclude its window is full on its first turn and rotate on every turn after it. The loop must sit
above because a session declares its tools on every request, and a bare client will emit tool calls
that nothing answers. Decorators of your own are welcome around the client you supply; do not add
function invocation, which the factory installs.

## Where the Window Comes From

An `IChatClient` publishes no context window — the abstraction exposes a provider name, a provider
URI and a default model identifier, and nothing about limits. So
`ChatClientProviderSessionFactory` is told one, once, where the application configures its provider.

Read it from the provider wherever the provider will say. On Ollama,
`DemaConsulting.AgentKit.Agents.Ollama` does the asking:

```csharp
using DemaConsulting.AgentKit.Agents.Ollama;
using Microsoft.Extensions.AI;

var window = await OllamaContextWindow.ReadAsync(ollamaClient, model, stated: null, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollamaClient, window.Tokens);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens);
```

It reports where the figure came from as well as what it is, because they are not interchangeable.
`LoadedModel` is the length the running instance is using, which is what will actually be enforced.
`Stated` means you supplied it. `Assumed` means no instance could be asked and a conservative
default was used. Show the source alongside the number: an assumption presented as a measurement is
how a session ends up sized on something nothing guarantees.

Compose `OllamaContextSizingChatClient` whichever of the three you got. A window that was only
*read* is no more durable than one that was only claimed, and an assumed figure was never known to
be right in the first place, because Ollama's default depends on available memory and on
`OLLAMA_CONTEXT_LENGTH`. Asking for the number in hand is what makes it true, and it forces no load
that the first chat request would not force anyway.

What a model *file* publishes as its maximum is deliberately never reported as the window to account
against. It describes what the file could support, not what the instance is running: one live server
published 262,144 tokens for a model it was running at 4,096. Accounting against the larger figure
would keep a session talking long after the server had begun discarding the start of the
conversation. The model catalog reports it as `ContextWindowTokens`, but only as the ceiling a chosen
window is checked against.

To choose the window rather than discover it, state it to the same call — nothing else changes:

```csharp
const int windowTokens = 32768;

var window = await OllamaContextWindow.ReadAsync(ollamaClient, model, windowTokens, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollamaClient, window.Tokens);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens);
```

A user who chose a model from the catalog chose its window too, already checked against the length the
model was trained for. Pass the selection instead, and its window is the stated one; with no window
chosen, the running instance is read as before:

```csharp
var window = await OllamaContextWindow.ReadAsync(ollamaClient, selection, cancellationToken);
IChatClient sized = new OllamaContextSizingChatClient(ollamaClient, window.Tokens);
var providerSessions = new ChatClientProviderSessionFactory(sized, window.Tokens, selection);
```

The decorator names the size on **every** request it forwards, not only the first, and a summarizer
needs the same treatment whichever model it runs on. The shipped API reference for
`OllamaContextSizingChatClient` carries the reasoning for both, along with the one caution that
comes with it: asking for a size is a property of the request, not a promise about the model.
Everything else you set on a request reaches the provider unchanged, and a `num_ctx` you set
yourself on a particular request is never overruled.

An application that sets the context length itself already knows the number it chose. For a hosted
model the window is a published property of the model the
application selected. The research-assistant sample reads it from Ollama and reports which of those
sources it used; see *Sample: Research Assistant*.

A provider that answers a turn without reporting token usage is **refused**, not estimated around.
Knowing when the window is filling is the one thing the library needs a token count for, and
guessing at it would mean guessing forever at the single fact the whole arrangement turns on.

## What a Turn Reports

Compaction is reported rather than hidden. An application that never looks is unaffected — the
session keeps working either way — but an application that does look can act on what it sees.
`AgentSessionResponse` carries five members:

- **`Text`** — the provider's answer. Show it, or act on it.
- **`Usage`** — occupancy after the turn: used, window, and the conversation's share of it. Watch it
  climb, so the next rotation is comprehensible rather than sudden.
- **`RotationOccurred`** — the session consolidated older history and replaced its provider session
  during this turn. Log it, and read `CompactingAgentSession.ConsolidationCount` beside it:
  summarizer calls are the dominant cost of this arrangement.
- **`Level`** — how hard the session is compacting: `Low`, `Medium` or `High`. A level climbing to
  `High` over several rotations is what a struggling long-running agent looks like.
- **`MaterialDropped`** — a fully consolidated context still did not fit, so a record or a turn was
  discarded outright. Act on it: it is the honest signal that compacting bought nothing.

`Level` is session state rather than a setting: a session that keeps filling its window shortly
after a rotation escalates, and one that runs a long stretch without filling relaxes. A higher level
keeps a shorter verbatim tail and tells the summarizer to be terser.

The answer in `Text` is produced **before** any compaction the turn triggers, by the session that
was live when the message arrived. A caller therefore never waits on a summarizer before receiving
an answer the session could already give.

## Writing or Choosing a Summarizer

`ISummarizer` is one method. `ChatClientSummarizer` implements it over any `IChatClient`, using the
consolidation prompt the library publishes as `ConsolidationPrompt` and the terseness clause the
session's current level selects. An application remains free to write its own — but it should not
have to, which is why one ships.

A summarizer must run **out of session** and must be stateless. The material arrives as an argument
precisely so that consolidation does not happen inside the session being compacted. Give it a client
that is not the one carrying the conversation.

## Exercising a Session Without a Model

`InMemoryProviderSession` and `InMemoryProviderSessionFactory` ship in Core rather than being
confined to this library's tests. The compaction promise is only believable if it can be exercised
end to end offline, and an application author writing their own summarizer, choosing a verbatim tail
length, or deciding what to do about a reported rotation needs exactly the same ability:

```csharp
var factory = new InMemoryProviderSessionFactory(windowTokens: 8192);
await using var session = await CompactingAgentSession.CreateAsync(options, factory);

await session.SendAsync("first");

// One entry per rotation, plus one for the original session, so a test can check what a rotation
// actually carried forward — which records survived, and which verbatim turns.
Console.WriteLine($"Provider sessions created: {factory.Sessions.Count}");
Console.WriteLine($"Seeded history: {factory.Sessions[^1].Seed.History.Count} entries");
```

## Disposal

An agent session owns a live provider session, which for some providers is server-side state that
keeps being billed for until it is released. Disposing the agent session releases whichever provider
session it currently holds — so `await using`, always.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
