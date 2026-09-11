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

Install the library using the .NET CLI:

```bash
dotnet add package DemaConsulting.AgentKit.Core
```

# What the Library Provides Today

AgentKit Core is the contract package. It does not itself ship ready-made tools; it defines the
safety model that every AgentKit tool, and every tool an application writes for itself, is built
against. The ready-made tool families will ship in `DemaConsulting.AgentKit.Tools`, which is not
yet published, so a consumer today writes tools against this contract directly.

## Path Policy

Read access and write access are expressed as two independent rules. A rule is either unrestricted
or confined to one location, and each rule carries its own denied patterns. Rules are created
through `PathRule.Unrestricted` and `PathRule.Rooted`, and paired into a `PathPolicy`.

A policy cannot be constructed without both of its rules, so an unguarded policy cannot exist.
Access is requested through `PathPolicy.TryResolveRead` and `PathPolicy.TryResolveWrite`, which
return whether the access is permitted, the real location on success, and a redacted reason on
refusal. Directory listings are obtained through `PathPolicy.EnumerateFiles`, which applies the
same decision, so a listing can never advertise a file that access would refuse.

Every containment decision resolves symbolic links and directory junctions at every path component,
so a path that merely looks contained cannot reach outside the location the operator granted.
A refusal is a returned value, never an exception, so a refused tool call does not end an agent's
turn.

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

`ToolResult` constructs what a tool returns: `Text` for text, `Binary` and `Image` for
content carrying a media type and an optional caption, and `Denied` for a refusal. A refusal names
its reason and may redirect the model to a more appropriate tool. Results reach the provider in the
form the tool produced them rather than as serialized JSON, which is what allows a returned image
to be recognized as an image.

## Tool Packs

A package publishes its tools as a pack by implementing `IToolPack`, declaring the family prefix
its tools carry and the `HostCapabilities` the host must provide. An application composes packs
through `ToolPackBuilder`, declaring what its host supports and adding one pack per capability it
wishes to attach. A pack whose required capabilities the host does not provide is never asked to
create its tools at all, so the model is never offered a tool it cannot use.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
