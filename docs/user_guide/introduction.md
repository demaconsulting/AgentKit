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

# What the Library Provides Today

AgentKit Core is the contract package. It defines the safety model that every AgentKit tool, and
every tool an application writes for itself, is built against. Ready-made guarded tool families
ship in `DemaConsulting.AgentKit.Tools` — the text file and image families described under
[Tool Families](#tool-families) below — so a consumer can attach shipped tools directly, or write
its own tools against this contract.

## Path Policy

Read access and write access are expressed as two independent rules. A rule is either unrestricted
or confined to one location, and each rule carries its own denied patterns. Rules are created
through `PathRule.Unrestricted` and `PathRule.Rooted`, and paired into a `PathPolicy`.

Most applications want one workspace for both directions, and `PathPolicy.ForWorkspace` names it
once:

```csharp
var policy = PathPolicy.ForWorkspace("/workspace");
```

That single argument becomes the permitted read location, the permitted write location, and the
**base directory** — the location a relative path is interpreted against. Use a `PathPolicy`
constructor directly when reads and writes need different locations; the constructor takes an
optional base directory, and defaults it to the read rule's location.

**A path a model supplies is read relative to the base directory.** A model asks for `notes.txt`,
not for its absolute location, so that is the request the policy answers. A path resolved against
the location the host process happened to be started from would refuse every legitimate request
while looking, from the outside, like a containment decision. Absolute paths remain expressible and
remain subject to the same containment decision. A request naming no path at all — an omitted,
empty or whitespace argument, or the literal word a model's runtime prints for absence — means the
base directory itself.

A policy cannot be constructed without both of its rules, so an unguarded policy cannot exist.
Access is requested through `PathPolicy.TryResolveRead` and `PathPolicy.TryResolveWrite`, which
return whether the access is permitted, the real location on success, and a redacted reason on
refusal. Directory listings are obtained through `PathPolicy.EnumerateFiles`, which applies the
same decision, so a listing can never advertise a file that access would refuse; a listing that
names no directory lists the base directory.

Every containment decision resolves symbolic links and directory junctions at every path component,
so a path that merely looks contained cannot reach outside the location the operator granted.
A refusal is a returned value, never an exception, so a refused tool call does not end an agent's
turn — and no path a caller supplies, including none at all, is reported as an exception. A refusal
states the form a permitted request takes while still naming no host location, so a refused agent
has something to act on rather than a bare "no" to retry against.

## Tool Limits

`ToolLimits` carries the ceilings a tool observes: the bytes it may read, the characters its
result may return to the model, the bytes of binary content it may return, and the attachments it
may add in one turn. Limits are carried with the policy, through `PathPolicy.Limits`, so every
tool an application attaches observes one budget rather than each inventing its own. A host that
configures nothing still operates within the published defaults.

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
its reason and may redirect the model to a more appropriate tool. Text and content reach the
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

`DemaConsulting.AgentKit.Tools` ships two ready-made guarded tool families. Each family is a pack
an application adds to a `ToolPackBuilder`; the builder gates each pack on the host capabilities it
requires and returns the `AIFunction` list to hand to an agent framework.

## Available Tools

| Family    | Tool              | Purpose                                                   | Required capability |
|-----------|-------------------|-----------------------------------------------------------|---------------------|
| Text file | `text_file_read`  | Reads a text file within the policy                       | None                |
| Text file | `text_file_write` | Writes a text file within the policy                      | None                |
| Text file | `text_file_list`  | Lists text files within the policy                        | None                |
| Image     | `image_read`      | Reads an image or PDF document for a vision-capable agent | `Vision`            |

The text file family (`TextFilePack`) requires no host capability. The image family (`ImagePack`)
requires the `Vision` host capability: unless the host declares `HostCapabilities.Vision`, the
builder never asks the pack to create `image_read`, so a model is never offered a tool its host
cannot use.

## Composing a Tool List

An application names its workspace, adds the packs it wants to a
`ToolPackBuilder`, declares the capabilities its host supports, and builds the tool list:

```csharp
using DemaConsulting.AgentKit.Core;
using DemaConsulting.AgentKit.Tools.TextFile;
using DemaConsulting.AgentKit.Tools.Image;
using Microsoft.Extensions.AI;

var policy = PathPolicy.ForWorkspace("/workspace");

IReadOnlyList<AIFunction> tools = new ToolPackBuilder(policy)
    .WithHostCapabilities(HostCapabilities.Vision)
    .Add(new TextFilePack())
    .Add(new ImagePack())
    .Build();

// Hand `tools` to ChatOptions.Tools, an IChatClient, or Microsoft Agent Framework.
```

Every tool returned observes the same policy and limits: a `text_file_read` that steps outside the
rooted location, or exceeds the byte ceiling, returns a refusal rather than the file. Every tool
also reads a path the same way, so a name `text_file_list` reported can be handed straight back to
`text_file_read` or `image_read`, and `text_file_list` called with no directory lists the
workspace root. The tool
`Create` factories are internal, so composing through the packs is the only supported way to obtain
these tools.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
