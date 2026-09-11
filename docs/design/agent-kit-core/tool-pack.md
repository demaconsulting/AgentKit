## ToolPack

![AgentKit Core Structure](AgentKitCoreView.svg)

The `IToolPack` interface defines what a tool pack is, and the `HostCapabilities` enumeration
defines the vocabulary in which a pack states what it needs of its host.

### Purpose

A package exposes its tools as a *pack* rather than as individual tools, so that attaching a
package to an application costs one line rather than one line per tool. `ToolPack` defines what a
pack is: a family prefix it owns, the host capabilities its tools require, and a way to create
those tools from an access policy.

A pack **declares** what it needs rather than **deciding** whether it can operate. The decision
belongs to `ToolPackBuilder`, because only the builder knows what the host declared — and because
a pack that decided for itself would have to refuse at call time, by which point the model has
already been offered the tool.

The unit also defines `HostCapabilities`, the vocabulary in which that need is expressed.

Implementations are expected to be stateless and therefore safe for concurrent use; `CreateTools`
receives everything it needs as an argument.

### Data Model

`HostCapabilities` is a flags enumeration:

| Member   | Value | Meaning                                                                    |
|----------|-------|----------------------------------------------------------------------------|
| `None`   | `0`   | No capability is required or declared.                                     |
| `Vision` | `1`   | The host can accept image content in a result and present it to the model. |

A capability is a property of the *host* — the application and the model behind it — not of the
machine. Whether a model can accept image content is the host's to declare, and no library can
discover it.

`None` is a real value, not a placeholder for "unset". A pack whose `RequiredCapabilities` is
`None` requires nothing of its host and is therefore registered by every host, which is the
natural expression of "always available" and needs no special case in the gating rule:
`(declared & None) == None` holds for every declaration. This is the opposite choice from
`DenialReason`, which deliberately has no zero member — an unspecified denial reason is always a
bug, whereas an unspecified capability requirement is the common case.

`Vision` exists because the image tool family cannot operate without it. **No other member is
defined, and that restraint is deliberate.** A speculative capability is public API that must be
honored forever, and a host that declares one it does not understand silently widens what the
model is offered. Members are added when a pack needs one, and each new member must be zero or a
single bit — a member overlapping another would silently satisfy a requirement the host never
declared, which a unit test enforces reflectively over every member, including members that do
not exist yet.

`IToolPack` holds no state of its own; it states what an implementation must provide:

| Member                    | Type                      | Invariant                                    |
|---------------------------|---------------------------|----------------------------------------------|
| `FamilyPrefix`            | `string`                  | Non-null, non-empty; leads every tool's name |
| `RequiredCapabilities`    | `HostCapabilities`        | `None` means the pack is always registered   |
| `CreateTools(PathPolicy)` | `IEnumerable<AIFunction>` | Non-null; contains no null element           |

### Key Methods

#### FamilyPrefix

The pack's identity. Two packs claiming the same prefix would publish tools whose names are
ambiguous, which `ToolPackBuilder.Add` refuses. The value must be non-null and non-empty, and must
be the leading portion of every name `CreateTools` returns — a promise `ToolPackBuilder.Build`
verifies rather than trusts.

#### RequiredCapabilities

The capabilities the host must provide for this pack's tools to operate. `HostCapabilities.None`
means the pack is always registered.

#### CreateTools(PathPolicy policy)

Creates the pack's tools, governed by the supplied access policy.

**Preconditions:** `policy` is the policy the composition was built with; a pack does not supply
one of its own.

**Postconditions:** the returned collection is non-null, contains no null element, and every tool
carries `FamilyPrefix` followed by an underscore.

Called once per `ToolPackBuilder.Build`, and **not called at all** when the host does not provide
`RequiredCapabilities`. That is the property the whole gating design rests on: an unsupported
pack's tools are never built, so none of them can reach the model by accident.

The policy is a parameter rather than pack state so that implementations stay stateless and a
pack cannot be configured with a policy of its own.

### Error Handling

`IToolPack` is a contract, so it defines obligations rather than handling. The obligations it
places on an implementation — a non-empty prefix, a non-null collection, no null element, names
within the declared family — are enforced by `ToolPackBuilder`, which throws when one is broken;
see *ToolPackBuilder Unit Design*. Enforcement lives there because a pack may come from a package
compiled without nullable annotations, so the contract is checked rather than assumed.

An implementation that needs to refuse an operation at **run time** does so from within its tools,
by returning a `ToolResult` denial rather than throwing; see *ToolResult Unit Design*. Nothing in
the pack contract itself is reachable from a model's tool call.

### Dependencies

`IToolPack` depends on `PathPolicy`, which it receives as the argument to `CreateTools`, and on
`AIFunction` from `Microsoft.Extensions.AI.Abstractions`, which is the form a tool takes.
`HostCapabilities` depends on nothing.

### Callers

`IToolPack` is a public API extension point: a package author implements it to publish that
package's tools. Within this system it is consumed by `ToolPackBuilder`, which reads
`RequiredCapabilities` to decide whether to register the pack, reads `FamilyPrefix` to detect
collisions and to verify the names a pack publishes, and calls `CreateTools` for the packs it
registers.
