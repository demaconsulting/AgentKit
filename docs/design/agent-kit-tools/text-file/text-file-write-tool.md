### TextFileWriteTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileWriteTool` class publishes the `text_file_write` tool.

#### Purpose

To replace the text of one file the access policy permits the agent to write, and to refuse
everything else. This is the unit that makes the read-only/read-write grant distinction
observable: it consults the write decision alone, so a path the agent may read is refused for
writing unless a read-write grant permits it too. An operator who grants wide reading and narrow
writing has expressed exactly that expectation, and it is only real because this unit ignores what
read-only grants would have allowed for reading.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `text_file_write`; public constant; carries the family prefix  |
| `ToolDescription` | `string` | Non-empty; states that existing content is replaced            |
| Denial messages   | `string` | Tool-composed constants; policy denials come from `PathPolicy` |

**No write ceiling exists, and that is a decision rather than an omission.** `ToolLimits` publishes
a read budget and a result budget; neither describes a write, and repurposing one would give a host
a control whose name does not say what it does. The size of a write is already bounded by the
model's own output. If a write ceiling is ever wanted, it belongs in `ToolLimits` as its own named
member, not borrowed from a neighbor.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate is declared
`Task<object>` deliberately; see *GuardedToolFactory Unit Design*. The policy is captured by the
delegate's closure, so a write tool governed by nothing — the most dangerous thing this library
could hand an agent — cannot be constructed.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? path = null, string? content = null, CancellationToken cancellationToken = default)`

**Algorithm**, in order:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`, naming the form a
   request should take. **Both parameters carry a default**, which is load-bearing rather than
   cosmetic: a parameter with no default is required by the function factory, and an omitted
   argument then fails inside the factory before this step is reached, leaving the model an
   opaque framework error rather than a refusal it can act on
2. An absent `content` is refused as `InvalidRequest`. An **empty** string is not absent: it writes
   an empty file, which is a legitimate outcome an agent may intend
3. `policy.TryResolveWrite(path, …)` — and only the write decision. A refusal is returned as
   `PathNotPermitted` carrying the policy's own message unchanged. **A relative path is
   interpreted against the workspace here**, identically to a read, so an agent can write back
   under the name it read. Because the policy resolves
   every path component, a destination that reaches outside the permitted location through a link
   is refused here
4. An existing directory is refused as `InvalidRequest`. No redirect is offered, because no other
   tool does this better
5. A missing parent directory is refused as `TargetNotFound`. **The tool creates no directory**
6. The content is written, replacing any existing content, and a confirmation naming the character
   count — and no location — is returned

**Preconditions:** none beyond a constructed tool; every argument is validated into a refusal.

**Postconditions:** on success the file's entire content is the supplied text, and the confirmation
reports the character count. On any refusal the file system is unchanged.

#### Error Handling

Everything a model controls produces a returned refusal, never an exception. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction, which is a programming
error in the composing application.

File system failures are caught by an explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and reported as an
`InvalidRequest` refusal. Enumerating the classification rather than catching everything keeps a
genuine defect visible during development. Cancellation is not classified and propagates.

**The tool never creates a directory.** Silently materializing a tree is a file system side effect
the operator did not ask for, and a mistyped path would scatter directories the agent then believes
are real. Refusing is the fail-safe reading and tells the model precisely what is wrong.

**Writing replaces.** Appending would silently duplicate text, and the model — which cannot see the
result — would build its next step on a file that no longer says what it believes. The replacement
semantics are stated in the tool's description so the model is never surprised by them.

Disclosure depends on which unit composes the refusal. A `PathNotPermitted` refusal carries the
`PathPolicy` message unchanged: it states what was requested, how a relative request was
interpreted, and all permitted locations with their access levels. For a write denial, a readable
but read-only location therefore appears as `(read-only)`, explaining why the write is refused
there. Refusals this unit composes itself — including the missing-path, missing-content,
path-is-directory, missing-parent and write-failure cases — are constants; the success
confirmation interpolates only the character count. **Each refusal nevertheless states what the
model should do instead** — the form a path takes, or the content to supply — because an agent told
only "no" retries the same request until it abandons the task.

#### Dependencies

`PathPolicy` for the write decision, `ToolResult` for every result it returns, and
`GuardedToolFactory` for construction. From the Base Class Library: `File`, `Directory` and `Path`.
`AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the form the constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. Nothing else in this package calls the unit.
