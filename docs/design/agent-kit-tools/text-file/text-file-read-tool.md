### TextFileReadTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileReadTool` class publishes the `text_file_read` tool.

#### Purpose

To return the text of one file the access policy permits the agent to read, and to refuse — in a
way the agent can act on — every request it cannot honor. It is the tool an agent reaches for most
often, and it is the one that has the most ways to be asked for something it must not provide: a
path outside the permitted location, a path that reaches outside through a link, a directory, a
file that does not exist, or a file too large to return.

The unit does no containment reasoning of its own. It asks the policy, and shapes the answer into
something a model can use.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                    |
|-------------------|----------|--------------------------------------------------------------|
| `ToolName`        | `string` | `text_file_read`; public constant; carries the family prefix |
| `ToolDescription` | `string` | Non-empty; the basis on which a model chooses this tool      |
| Denial messages   | `string` | Compile-time constants; contain no host location             |

Two ceilings from `PathPolicy.Limits` bound the operation: `MaxReadBytes`, the greatest size that
may be read from one source, and `MaxResultCharacters`, the deliberately tighter budget for what
may be returned to the model. Both are inclusive — a file exactly at a ceiling is returned.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`TextFilePack` is the only place the `text_file` family prefix is claimed.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate is declared
`Task<object>` deliberately — that is the shape the underlying function factory would otherwise
serialize into JSON, and is precisely the case the guard exists for; see *GuardedToolFactory Unit
Design*. The policy is captured by the delegate's closure.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(path, cancellationToken)`

**Algorithm**, in this order, because the order is itself the contract:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`
2. `policy.TryResolveRead(path, …)` — a refusal is returned as `PathNotPermitted` carrying the
   policy's own message unchanged. This step resolves every path component, so a link that escapes
   the permitted location is refused here without this unit knowing links exist
3. An existing directory is refused as `InvalidRequest`, redirecting to `text_file_list`
4. A non-existent file is refused as `TargetNotFound`, redirecting to `text_file_list`
5. A file larger than `MaxReadBytes` is refused as `ResourceTooLarge`, naming the ceiling. Size is
   judged before the file is opened, so an oversized file is never loaded merely to discover it was
   oversized
6. The text is read; text longer than `MaxResultCharacters` is refused as `ResourceTooLarge`,
   naming that ceiling
7. Otherwise the text is returned

The policy decision precedes every observation of the file system, so a refused path never
discloses whether it exists.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception: an exception raised
during a tool call ends the agent's turn and strands it with no way forward. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction, which is a programming
error in the composing application rather than anything a model can provoke — the same dividing
line `PathPolicy` and `ToolName` draw.

File system failures are caught by an explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. The classification is enumerated rather than catching everything so that
a genuine defect still surfaces during development instead of being reported to a model as an
unreadable file. Cancellation is not classified, so a cancelled read propagates as the runtime
expects.

**An oversized file is refused, never truncated.** A truncated file is an omission the model cannot
detect; it would then reason confidently about content it never saw. Refusing with the ceiling
named lets it narrow the request instead.

No refusal message contains a path, a permitted location or a directory separator. The messages are
constants and the only interpolated values are integers naming a ceiling.

#### Dependencies

`PathPolicy` and `ToolLimits` for the decision and the ceilings, `ToolResult` for every result it
returns, `GuardedToolFactory` for construction, and `TextFileListTool.ToolName` for the two
redirects. From the Base Class Library: `File`, `FileInfo` and `Directory`. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the form the constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. Nothing else in this package calls the unit.
