### ImagePack

![AgentKit Tools Image Structure](ImageView.svg)

The `ImagePack` class publishes the image family as one pack.

#### Purpose

To be the one thing an application attaches in order to give a vision-capable agent image access. A
package exposes its tools as a pack rather than as individual tools so that attaching it costs one
line rather than one line per tool, and so that exactly one place claims the family prefix the
tools' names are protected by.

The pack is also what makes the family's construction discipline and its capability gate
enforceable. Because the tool's factory is `internal` and this is the pack all attachment goes
through, there is no way for an application to obtain an image tool that was not created here, from
the policy the composition supplied; and because the pack declares the capability the family
requires, the composition can withhold the family from a host that cannot present its content.

#### Data Model

The class is sealed and holds no state; it is safe for concurrent use.

| Member                    | Type                      | Invariant                                     |
|---------------------------|---------------------------|-----------------------------------------------|
| `FamilyPrefix` (constant) | `string`                  | `image`; leads every tool name it creates     |
| `IToolPack.FamilyPrefix`  | `string`                  | Reports the constant above                    |
| `RequiredCapabilities`    | `HostCapabilities`        | `Vision`; the family is gated on it           |
| `CreateTools(PathPolicy)` | `IEnumerable<AIFunction>` | Non-null; no null element; every tool         |

The prefix is published both as a constant and through the contract. The constant lets a test or a
composing application name the family without repeating a string literal that could drift from the
names the tools actually carry; the contract member is what `ToolPackBuilder` reads. They cannot
disagree, because the contract member returns the constant. The contract member is implemented
explicitly so that the constant can keep the name the contract uses; the class is sealed, so the
member is not hidden from a derived type.

#### Key Methods

##### RequiredCapabilities

Reports `HostCapabilities.Vision`. The family returns image content, which is of no use to a host
that cannot present it to a model, so the composition withholds the family from a host that has not
declared it can. Because the builder gates before it asks a pack for its tools, a non-vision host
never even causes `CreateTools` to run — the family is withheld rather than offered and refused.

##### CreateTools(PathPolicy policy)

Creates the family's tools.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with, not one
the pack invented.

**Algorithm:** validates `policy`, then returns `ImageReadTool.Create(policy)` followed by
`ImageCropTool.Create(policy)` and `ImageAutoCropTool.Create(policy)` as the three elements of a
collection, in that order.

**Postconditions:** exactly the read tool, the crop tool and the auto-crop tool, non-null, each
named `image_`-prefixed and governed by the supplied policy. The three are created together because
they are one capability: the read tool states the coordinate space the crop tool consumes, so a pack
that published only one of them would offer a model either a region request it cannot aim or a
size it has nothing to use — and the auto-crop tool answers the region question a model cannot state
in that coordinate space at all, because it can see that a picture is mostly margin without being
able to measure where the margin stops. Called once per `ToolPackBuilder.Build`, and only when the
host provides the Vision capability.

**This pack does not filter on the policy, and the reason is a decision rather than an oversight.**
The rule the text-file and file families apply — described under *Policy-derived publication* in the
system design — gates a tool on the writes it can only perform, not on whether every argument it
accepts could be used. All three tools here clear that bar under a policy that permits no writing
anywhere:
reading an image is a read, and each region tool's primary mode returns the region inline as image
content and writes nothing. The optional `destination` both region tools accept genuinely does need
a write grant, but
withholding a whole tool over an optional argument would remove a fully working capability, and
treating a narrowable parameter as grounds for suppressing a tool would mean every optional
argument needs a gate of its own. A destination named under a read-only policy is answered by the
ordinary write denial, which enumerates the writable locations, so the model learns its options in
one turn instead of losing the tool permanently.

**Not filtering includes the degenerate policy, and that is accepted rather than corrected.** Under
a policy holding no grants at all, all three tools are published and `image_read` refuses every
path it is given, because `PathPolicy.TryResolveRead` can admit none. A symmetric read-visibility
check would remove that one case at the cost of a second publication question in every pack:
`PathRule` offers only `ReadOnly` and `ReadWrite`, and `AccessLevel` has no write-only member, so
write access always implies read access and a read tool is unusable only where nothing whatever is
granted — a composition whose agent can touch no file however its tool list is trimmed.

**Nor does either region tool's description vary with the policy.** That decision, and the
compile-time constraint that forces it, are recorded in *Image Crop Tool Design* and apply
identically to *Image Auto Crop Tool Design*.

#### Error Handling

`ArgumentNullException` is raised for a null policy. This is validated here as well as in the tool's
factory so that the failure names the composing application's mistake at the line that made it,
rather than surfacing from inside a tool the application did not write. A family created without a
policy would hand an agent unrestricted file access while appearing correctly composed, which is the
failure this check exists to make impossible.

Nothing else in the unit can fail. The obligations the pack contract places on it — a non-empty
prefix, a non-null collection, no null element, names within the declared family — are verified by
`ToolPackBuilder.Build`, which throws when one is broken; see *ToolPackBuilder Unit Design*. The
pack is not reachable from a model's tool call, so no runtime refusal arises here.

#### Dependencies

`IToolPack` and `HostCapabilities` for the contract it implements, `PathPolicy` as the argument it
passes on, and the read, crop and auto-crop tool units whose internal factories it calls.
`AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the form the created tools take.

#### Callers

An application constructs an `ImagePack` and adds it to a `ToolPackBuilder`; the builder then calls
`CreateTools`, but only for a host that declared the Vision capability. Nothing inside this package
calls the unit.
