### ChildAgentRequest

![AgentKit Tools Agent Structure](AgentView.svg)

The `ChildAgentRequest` class is the bundle the library hands the host's runner for one delegated
agent.

#### Purpose

To carry everything the host needs to start one child — the profile the model selected, the
application-authored instructions, the tools the library composed against the child's own policy,
the task the parent stated, and the child's delegation depth — and to carry it in a shape the
host cannot assemble wrongly.

The type exists because the property most worth assembling wrongly is the tool list. A runner
that could hand-build its own request could hand it the parent's tools, and that is exactly the
mistake the family's isolation property exists to prevent. The constructor is `internal`; the
library is the only correct author of a request, because the tools it carries were composed by
`AgentPack.ComposeChild` from the child's own state.

#### Data Model

The class is sealed and immutable. Every property is set at construction and never changes.

| Member         | Type                        | Invariant                                            |
|----------------|-----------------------------|------------------------------------------------------|
| `ProfileName`  | `string`                    | The name the model selected; carries no authority    |
| `Instructions` | `string`                    | The application's authorship of what the child is    |
| `Tools`        | `IReadOnlyList<AIFunction>` | Composed against the child's own policy; may be empty|
| `Task`         | `string`                    | The one part the parent supplied; data, not authority|
| `Depth`        | `int`                       | The child's depth, counting a root agent as zero     |

The tool list is already the intersection of the profile's declared names and what the
application attached, so the host attaches it as it stands. It may be empty when the profile
declares no tools, in which case the host builds an agent that can only answer.

#### Key Methods

##### ChildAgentRequest(profileName, instructions, tools, task, depth)

Internal constructor. Everything here has been validated or constructed by `AgentRunTool` before
this point; no re-validation is required or performed.

**Preconditions:** the caller is the library. All arguments are non-null; `depth` is non-negative.

**Algorithm:** captures each argument on the corresponding property.

**Postconditions:** the request is immutable and safe to hand across a runner boundary.

#### The Runner's Contract

The runner the application gave `AgentPack` is a `Func<ChildAgentRequest, CancellationToken,
Task<string?>>`. It is expected to build an agent from `Instructions` and `Tools` exactly as
given, run it against `Task`, and return the final text. It should not add tools, because a tool
the application did not attach is a capability the profile did not admit, and it should not
replace the instructions, because those are the application's authorship of what the child is.

The cancellation token the tool call observed is passed through, so a canceled parent turn
reaches the child rather than leaving a delegated agent detached. A runner returning `null` or
empty text is reported to the parent as a child that said nothing rather than as a failure. An
exception raised inside the runner propagates rather than becoming a refusal, because failing to
reach a model provider is the host's condition to classify: a transient outage and a
misconfigured credential look the same from inside this library.

#### Error Handling

The type itself raises nothing. It has no public constructor, and the internal one runs no
validation because it is only reached from validated call sites. Runtime failures are the
runner's — this type is only the vehicle it receives.

#### Dependencies

`AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, as the type of the tools it carries.
Nothing else.

#### Callers

`AgentRunTool` constructs one request per accepted `agent_run` call. The host's runner receives
it as its only argument, alongside a cancellation token. Nothing in this package reads the type
after the runner returns.
