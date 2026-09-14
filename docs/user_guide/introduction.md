# Introduction

## Purpose

This document is the user guide for AgentKit, a family of .NET libraries providing hardened,
provider-neutral agent tools. It exists so that a developer can hand an AI agent a set of
capabilities that are safe by construction — where the unsafe operation is not refused at call
time, but cannot be expressed.

## Scope

This user guide covers:

- Installation of the library
- What the library provides today, and how each part is used
- The path policy, tool limits, naming, result and tool pack contracts
- Three runnable samples: consuming the shipped tools (*document assistant*), an agent whose work
  spans turns (*research assistant*), and writing your own guarded tools (*custom tools*)

# Continuous Compliance

AgentKit follows the
[Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance) methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

Install the libraries using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
dotnet add package DemaConsulting.AgentKit.Tools
```

To build an agent from a provider, add the adapter for the provider you target:

```bash
dotnet add package DemaConsulting.AgentKit.Agents.ChatClient   # any IChatClient provider
dotnet add package DemaConsulting.AgentKit.Agents.Copilot      # the GitHub Copilot SDK
```

## API Documentation

Detailed API documentation for all public types and members is distributed in the `api/` folder
of the NuGet package.

This guide explains the safety model and how the pieces compose; the `api/` reference is the
signature-level companion to it. It is generated from the XML doc comments by the build — so it
cannot drift from the code it describes — and is organized for gradual disclosure: an index, then
a page per namespace, type, and member. That shape is deliberate, because the intended reader is
often a coding agent assembling an application against AgentKit, which works far better from one
small targeted page than from a single large document. The build fails on any public member that
lacks a documentation summary, so the reference is complete by construction.

# What the Library Provides Today

AgentKit Core is the contract package. It defines the safety model that every AgentKit tool, and
every tool an application writes for itself, is built against. Ready-made guarded tool families
ship in `DemaConsulting.AgentKit.Tools` — the text file, file, markdown, image, todo, memory and
agent families described under *Tool Families* below — so a consumer can attach shipped tools
directly, or write its own tools against this contract.

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

Every containment decision resolves symbolic links and directory junctions at every path component,
so a path that merely looks contained cannot reach outside the location the operator granted.
A refusal is a returned value, never an exception, so a refused tool call does not end an agent's
turn — and no path a caller supplies, including none at all, is reported as an exception. A refusal
states what was requested, how a relative request was interpreted, and which locations are permitted
with their access levels, so a confined agent learns where it may work instead of retrying against a
bare "no".

> **Transition hazard.** With a single granted working directory, tool output uses the relative
> dialect — a listing reports bare relative names and the model imitates them. Adding a second
> granted location moves paths under it to the absolute dialect (reported under an absolute header),
> and nothing else warns you the switch happened.

## Tool Limits

`ToolLimits` carries the ceilings a tool observes: the bytes it may read, the characters its
result may return to the model, the bytes of binary content it may return, the attachments it
may add in one turn, and how deep a chain of delegated agents may run. Limits are carried with
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

## Tool Names and Guarded Construction

`ToolName.Create` composes a name from a family and a verb, and `ToolName.Validate` checks a
name against the convention. Every tool name carries a family prefix, which is what stops an
application combining AgentKit with another tool provider from presenting the model with two
identically named tools.

`GuardedToolFactory.Create` is the only supported way to construct a tool. It validates the name
and applies the result-delivery guard to every tool it creates, so a tool author cannot omit either
by forgetting it.

## Tool Results

`ToolResult` constructs what a tool returns: `Text` for text, `Structured` for data that is
neither text nor content, `Binary` and `Image` for
content carrying a media type and an optional caption, and `Denied` for a refusal. A refusal names
its reason and states the fact that caused it; **a denial states a fact and never prescribes a
remedy**, because a denial that helpfully suggested another tool was measured pushing a model into
a destructive workaround. The optional redirect survives that rule in exactly two places, where
naming the tool *is* the fact being stated rather than a route around what was withheld: binary
content is offered to `image_read`, and an `.svg`, which genuinely is text, is offered to
`text_file_read`. Every other refusal — including every policy refusal — carries no redirect. Text
and content reach the
provider in the form the tool produced them rather than as serialized JSON, which is what allows a
returned image to be recognized as an image; a structured result is serialized to JSON on the way
out, because JSON is the form in which a provider can read structured data.

## Delivering Images to Any Provider

Providers differ in where they accept image content, and the difference is silent. Some deliver an
image a tool returned straight to the model. Others accept images on messages but not in tool
responses: the content is preserved all the way through the framework and then discarded at the
wire, after which the model describes a picture it never received and nothing reports an error.

A host targeting such a provider wraps its chat client once:

```csharp
IChatClient client = new ImagePromotingChatClient(providerClient);
```

The wrapper must sit **beneath** the function-invocation loop, so that it observes the conversation
after tool results have been appended. It then carries any image a tool result holds onto a
following user message, announced as coming from the tool, leaving every other message and their
order untouched. A conversation whose tool results carry no image is forwarded unchanged, so the
wrapper can be left installed. A host whose provider already delivers images from tool results
needs nothing.

## Tool Packs

A package publishes its tools as a pack by implementing `IToolPack`, declaring the family prefix
its tools carry and the `HostCapabilities` the host must provide. An application composes packs
through `ToolPackBuilder`, declaring what its host supports and adding one pack per capability it
wishes to attach. A pack whose required capabilities the host does not provide is never asked to
create its tools at all, so the model is never offered a tool it cannot use.

# Tool Families

`DemaConsulting.AgentKit.Tools` ships seven ready-made guarded tool families. Each family is a pack
an application adds to a `ToolPackBuilder`; the builder gates each pack on the host capabilities it
requires and returns the `AIFunction` list to hand to an agent framework. The table below is the
single place in the user-facing documentation that names every shipped tool; `lint.ps1` reads the
table itself and checks it against the tool-name and family-prefix constants in the source, in both
directions, so a tool the library ships but this table omits — and a tool this table names but the
library does not ship — fails the build. No other passage of this guide can stand in for a missing
row.

## Available Tools

| Family    | Tool                    | Purpose                                          | Required capability |
|-----------|-------------------------|--------------------------------------------------|---------------------|
| Text file | `text_file_search`      | Searches permitted text files for a pattern      | None                |
| Text file | `text_file_read`        | Reads a paged, line-numbered file window         | None                |
| Text file | `text_file_create`      | Creates a new text file within the policy        | None                |
| Text file | `text_file_replace`     | Replaces an exact span of text in a file         | None                |
| Text file | `text_file_cut_lines`   | Removes a line range into a named buffer         | None                |
| Text file | `text_file_copy_lines`  | Copies a line range into a buffer, source kept   | None                |
| Text file | `text_file_paste_lines` | Pastes previously cut lines back into a file     | None                |
| File      | `file_list`             | Lists files of any type within the policy        | None                |
| File      | `file_copy`             | Copies a file within the policy                  | None                |
| File      | `file_move`             | Moves a file within the policy                   | None                |
| File      | `file_delete`           | Deletes a single file within the policy          | None                |
| Markdown  | `markdown_outline`      | Reports the heading outline of a Markdown file   | None                |
| Image     | `image_read`            | Reads an image or PDF for a vision-capable agent | `Vision`            |
| Todo      | `todo_list`             | Reports the recorded steps, in recorded order    | None                |
| Todo      | `todo_set`              | Records a step, or updates the step with that id | None                |
| Todo      | `todo_remove`           | Drops a step by id from the task list            | None                |
| Memory    | `memory_file`           | Stores one memory under a short descriptor       | None                |
| Memory    | `memory_recall`         | Returns the memories nearest a stated question   | None                |
| Memory    | `memory_update`         | Replaces a memory's details, keeping its subject | None                |
| Memory    | `memory_revise`         | Replaces a memory's subject, details and source  | None                |
| Memory    | `memory_forget`         | Removes one memory permanently                   | None                |
| Agent     | `agent_run`             | Delegates a task to a named child agent profile  | `Delegation`        |

The text file (`TextFilePack`), file (`FilePack`), Markdown (`MarkdownPack`), todo (`TodoPack`) and
memory (`MemoryPack`) families require no host capability. Two families are gated. The image family
(`ImagePack`) requires the `Vision` host capability, and the agent family (`AgentPack`) requires
`Delegation`: unless the host declares the capability, the builder never asks the pack to create
its tools, so a model is never offered a tool its host cannot use.

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
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

var policy = new PathPolicy("/workspace", [PathRule.ReadWrite("/workspace")]);

IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .WithHostCapabilities(HostCapabilities.Vision)
    .Add(new TextFilePack())
    .Add(new FilePack())
    .Add(new MarkdownPack())
    .Add(new ImagePack())
    .Build();

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

The todo, memory and agent packs are added the same way, but take constructor arguments of their
own — a supplied store, an embedding generator, or the profiles, runner and child packs a delegated
agent is built from. *Sample: Research Assistant* below shows all three composed onto one policy.

# Building an Agent

A tool list is provider-neutral, but attaching it to a provider is not. Two adapter packages turn a
provider into a tool-using Microsoft Agent Framework agent in one call, each absorbing the one thing
that provider gets wrong.

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
function-invocation loop, and offers no option to disable it. A provider reached through an
`IChatClient` preserves an image a tool returns and then drops it at the wire, after which the model
describes a picture it never received. The adapter makes that mistake impossible: because the
decorator cannot be turned off, it cannot be forgotten. You do not wrap the client yourself.

## The GitHub Copilot SDK

`DemaConsulting.AgentKit.Agents.Copilot` builds an agent from a GitHub Copilot `CopilotClient`:

```csharp
using DemaConsulting.AgentKit.Agents.Copilot;

// The host constructs, starts, and disposes the client.
await using var client = new CopilotClient(options);
await client.StartAsync();

var agent = CopilotAgentFactory.Create(
    client,              // the host owns this client
    tools,               // the AIFunction list composed above
    instructions: "You are a document assistant. Use the tools provided.");
```

Copilot arrives as a complete runtime carrying its own tools — shell, fetch, file editing and more.
The adapter suppresses them by deriving the session allow-list from the same tools you supplied, so
a confined agent is offered only the tools you gave it. By default it also installs a permission
handler that approves exactly those tools and rejects everything else; supply your own handler to
override that. The adapter does **not** install the image-promoting decorator, because the Copilot
runtime already delivers a tool-returned image to the model.

**Choosing the model**: pass `model:` to name the Copilot model backing the session:

```csharp
var agent = CopilotAgentFactory.Create(
    client,
    tools,
    instructions: "You are a document assistant. Use the tools provided.",
    model: "gpt-5.4-mini");
```

Omit it and the Copilot runtime applies its own default. The choice is worth making deliberately:
model capability drives how reliably an agent uses its tools and how accurately it reads an image,
the same way it does when you pick an Ollama model for an `IChatClient` agent. Select the model
*through* the factory rather than by building a session configuration yourself — a hand-built
session forgoes the built-in tool suppression and the default-safe permission handler, producing an
agent whose every tool call is denied.

**Ownership**: the host owns the `CopilotClient` — whoever constructs and starts it disposes it. The
factory builds the agent over the client without taking ownership of it and creates nothing
disposable of its own.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
