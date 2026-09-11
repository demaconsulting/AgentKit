## ToolPackBuilder

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolPackBuilder` class composes tool packs into the tool list an application offers a model.

### Purpose

`ToolPackBuilder` turns a set of packs into the tool list an application offers a model, and it is
where the architectural decision *"tools are registered only when they can operate"* is realized.

**A pack whose required capabilities the host did not declare contributes no tools at all.** It is
not registered and then refused on use. The distinction is not a matter of tidiness: a tool the
model can see but cannot use costs a turn at best, and at worst the model — told only that the
tool refused — reasons its way around the refusal instead of abandoning the approach. An
unsupported pack is not even asked to create its tools, so there is nothing that could reach the
list by accident.

The builder is also where the single access policy is applied. Every pack receives the same
policy, so an author cannot widen one pack's reach by configuring it separately.

### Data Model

| Member              | Type               | Description                                           |
|---------------------|--------------------|-------------------------------------------------------|
| `_policy`           | `PathPolicy`       | The policy handed to every pack asked for its tools.  |
| `_packs`            | `List<IToolPack>`  | The packs added so far, in the order they were added. |
| `_hostCapabilities` | `HostCapabilities` | What the host has declared; initially `None`.         |

The initial value of `_hostCapabilities` is deliberately the closed one. A host that forgets to
declare what it supports is offered fewer tools, never more.

Instances are mutable and are **not safe for concurrent use**. Every other type in this system is
immutable and documents that it is safe for concurrent use, so the difference is stated here
rather than left to inference. A builder is a short-lived object assembled on one thread during
application start-up.

### Key Methods

#### ToolPackBuilder(PathPolicy policy)

Creates a composition governed by `policy`.

**Preconditions:** `policy` is non-null.

There is no constructor that omits the policy. A tool cannot be constructed without its policy, so
a builder cannot exist without one either; an unguarded composition is unrepresentable.

**Throws:** `ArgumentNullException` for a null policy.

#### WithHostCapabilities(HostCapabilities capabilities)

Declares what the host provides, and returns the builder so calls can be chained.

A later call **replaces** the declaration rather than adding to it, and the declaration is
evaluated at `Build`, so its position relative to `Add` does not matter.

#### Add(IToolPack pack)

Adds a pack to the composition and returns the builder so calls can be chained.

**Preconditions:** `pack` is non-null and carries a non-empty `FamilyPrefix` that no pack already
added claims. Prefixes are compared with ordinal comparison.

Adding the same pack instance twice is a collision like any other: it would publish every one of
that pack's tools twice, which is exactly the ambiguity the rule exists to prevent.

**Throws:** `ArgumentNullException` for a missing pack, and `ArgumentException` for an empty
family prefix or a prefix another added pack already claims.

#### Build()

Creates the tools of every pack the host can support.

**Algorithm**, for each pack in the order it was added:

| #   | Step                                                     | Failure                     |
|-----|----------------------------------------------------------|-----------------------------|
| 1   | Skip the pack unless `(declared & required) == required` | none; the pack is skipped   |
| 2   | Ask the pack for its tools, rejecting a null collection  | `InvalidOperationException` |
| 3   | Reject a null element                                    | `InvalidOperationException` |
| 4   | Reject a name outside the pack's declared family         | `InvalidOperationException` |
| 5   | Append the tool to the composed list                     | none                        |

Step 1 is evaluated **before** step 2, which is the whole point: an unsupported pack is never
asked for its tools, so no tool it would have produced can exist at all.

Tools appear in the order the packs were added and, within a pack, in the order it produced them.
The order a model sees is observable — it appears in transcripts and subtly influences tool
selection — so it is the caller's own stated order rather than an incidental one. Sorting by name
was rejected: it is equally deterministic but scatters a family across the list.

`Build` neither resets nor caches. Calling it twice reports the builder's state as it is at each
call and creates a fresh set of tools each time.

**Throws:** `InvalidOperationException` when a pack returns no collection, a null tool, or a tool
outside its declared family.

### Error Handling

| Condition                                     | Handling                               |
|-----------------------------------------------|----------------------------------------|
| Null policy, null pack                        | `ArgumentNullException` propagates     |
| Empty family prefix, colliding family prefix  | `ArgumentException` propagates         |
| Null tool collection, null tool, foreign name | `InvalidOperationException` propagates |
| A pack the host cannot support                | Skipped silently; no tools created     |

**Composition-time programming errors throw; only runtime policy decisions are returned.** This is
the same line `PathPolicy`, `ToolName` and `GuardedToolFactory` draw, and it holds here without
exception: nothing in this unit is reachable from a model's tool call, so nothing in it can end an
agent's turn. A colliding family prefix, a pack publishing outside its declared family, a missing
access policy — each is a mistake in the composing application's own code, discovered by the
developer who wrote it.

`Add` throws argument exceptions because the offending thing is its own argument. `Build` throws
`InvalidOperationException` because the offending thing is not an argument of `Build` — it is a
pack added earlier, or that pack's behavior, so the builder's *contents* are inconsistent.

A pack the host cannot support is not an error at all. It is the expected outcome of a host
declaring what it is, and the only correct handling is to leave its tools uncreated.

### Design Constraints

**The collision check must stay at `Add`.** Its value is that the exception names the call that
caused it. Moving it to `Build` would report the same fault at a call that did nothing wrong, and
would leave the builder holding a composition that cannot be built.

**The prefix-agreement check at `Build` is not redundant with the guarded construction path.**
`GuardedToolFactory` validates that a name is *well formed*; it has no notion of packs and
therefore cannot validate that a name *belongs to the pack publishing it*. The two answer
different questions, and without the second the first check is checking a declaration nobody
verified. The check does not, and cannot, verify that a tool was built through
`GuardedToolFactory` at all: construction provenance is not expressible against `AIFunction`, and
that residual risk is accepted here as it is for the message a `ToolResult` denial carries.

**Prefix comparison is ordinal.** Case-insensitive comparison was considered and rejected: tool
names are lowercase by convention, so of two prefixes differing only in case at most one can ever
produce a tool name the guarded factory accepts. Reporting a "collision" between them would report
the wrong problem; the uppercase prefix is reported where it belongs, by `ToolName`, with a message
about the name.

**Declaring capabilities replaces; it does not accumulate.** A declaration states what the host is,
and a host has one answer. Combining successive calls would make the declaration a one-way ratchet
no caller could narrow, and widening what a model is offered is the safety-relevant direction.

**The builder deliberately does not report which packs it skipped.** The information is already
available to the caller — it knows what it declared, and `RequiredCapabilities` is public — and a
skipped-pack list invites the exact mistake this unit prevents: a host that inspects it and
helpfully registers from it anyway. Should diagnosis be wanted later, a callback or logger can be
added without a breaking change.

### Dependencies

`ToolPackBuilder` depends on `IToolPack` and `HostCapabilities` (see *ToolPack Unit Design*), on
`PathPolicy`, which it holds and hands to every pack, and on `AIFunction` from
`Microsoft.Extensions.AI.Abstractions`, which is the form a tool takes.

### Callers

`ToolPackBuilder` is a public API entry point: an application constructs one with its access
policy, declares its host capabilities, chains an `Add` call per pack it wishes to attach, and
hands the composed list to the agent runtime. Nothing within this system calls it.
