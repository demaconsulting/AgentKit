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

| Member            | Type     | Invariant                                                      |
| ----------------- | -------- | -------------------------------------------------------------- |
| `ToolName`        | `string` | `text_file_read`; public constant; carries the family prefix   |
| `ToolDescription` | `string` | Non-empty; the basis on which a model chooses this tool        |
| Denial messages   | `string` | Tool-composed constants; policy denials come from `PathPolicy` |

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

##### The tool delegate: `(string? path = null, CancellationToken cancellationToken = default)`

**Algorithm**, in this order, because the order is itself the contract:

1. An absent, empty or whitespace `path` is refused as `InvalidRequest`, naming the form a
   request should take. **The parameter carries a default**, which is load-bearing rather than
   cosmetic: a parameter with no default is required by the function factory, and an omitted
   argument then fails inside the factory before this step is reached, leaving the model an
   opaque framework error rather than a refusal it can act on
2. `policy.TryResolveRead(path, …)` — a refusal is returned as `PathNotPermitted` carrying the
   policy's own message unchanged. **A relative path is interpreted against the workspace here**,
   which is why a bare file name a listing reported is directly usable, and an absolute path is
   accepted unchanged. This step resolves every path component, so a link that escapes
   the permitted location is refused here without this unit knowing links exist
3. An existing directory is refused as `InvalidRequest`, redirecting to `text_file_list`
4. A non-existent file is refused as `TargetNotFound`, redirecting to `text_file_list`
5. A file larger than `MaxReadBytes` is refused as `ResourceTooLarge`, naming the ceiling. Size is
   judged before the file is opened, so an oversized file is never loaded merely to discover it was
   oversized
6. A file whose leading bytes indicate **binary content** is refused as `UnsupportedMediaType`
   before any decode, so a binary file is never returned as garbled replacement characters.
   Detection is content-based: a recognized byte-order mark (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE)
   means text; otherwise a NUL byte, or a failure of strict UTF-8 validation over a leading window,
   means binary. The byte-order-mark check is first precisely so a legitimately encoded UTF-16 or
   UTF-32 file — which carries NUL bytes yet decodes correctly — is not misclassified by the NUL
   rule; the four-byte UTF-32 marks are tested before the two-byte UTF-16 marks because the UTF-16
   LE mark is a prefix of the UTF-32 LE mark. The *redirect* a refusal offers is chosen from the
   extension by reusing `ImageMediaTypes.TryResolveMediaType`: the file is offered to `image_read`
   when it resolves, and refused without a redirect otherwise. This step sits after the size check
   (the cheaper gate) and before the decode (so detection precedes any decode and therefore the
   result-ceiling check)
7. The text is read; text longer than `MaxResultCharacters` is refused as `ResourceTooLarge`,
   naming that ceiling
8. Otherwise the text is returned

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
unreadable file. Cancellation is not classified, so a canceled read propagates as the runtime
expects.

**An oversized file is refused, never truncated.** A truncated file is an omission the model cannot
detect; it would then reason confidently about content it never saw. Refusing with the ceiling
named lets it narrow the request instead.

**A binary file is refused as `UnsupportedMediaType`, never decoded into garbled text.** The binary
sniff opens the file for a leading window and is subject to the same explicit
`IsAccessFailure` classification as the decode, so a read failure during the sniff becomes the
ordinary unreadable-file refusal rather than a thrown exception. The strict UTF-8 validation flushes
its decoder only when the window reached end of file, so a multi-byte character the window boundary
split in half is buffered rather than rejected and a valid UTF-8 file is never misclassified because
a character straddled the window edge.

Disclosure depends on which unit composes the refusal. A `PathNotPermitted` refusal carries the
`PathPolicy` message unchanged: it states what was requested, how a relative request was
interpreted, and the permitted locations with their access levels, so a confined model learns where
it may read instead of guessing. Refusals this unit composes itself — including the binary-file
redirect, the directory and missing-file redirects, and the oversized-file ceilings — are constants
or interpolate only an integer ceiling or a sibling tool name. **Each nevertheless states what the
model should do instead** — the form a path takes, or the tool that would find the right name —
because an agent told only "no" retries the same request until it abandons the task.

#### Dependencies

`PathPolicy` and `ToolLimits` for the decision and the ceilings, `ToolResult` for every result it
returns, `GuardedToolFactory` for construction, and `TextFileListTool.ToolName` for the two
redirects. For the binary guard it reuses the sibling image family's `ImageMediaTypes.TryResolveMediaType`
to choose the `image_read` redirect and `ImageReadTool.ToolName` to name it, so media-type knowledge
lives in the one unit that owns it rather than being duplicated here; the reference is mutual with
`ImageMediaTypes`, which already redirects an `.svg` back to this tool, and both directions are leaf
references to published constants within one assembly. From the Base Class Library: `File`,
`FileInfo`, `Directory` and `FileStream`, and `UTF8Encoding`/`Decoder` from `System.Text` for the
strict UTF-8 validation. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the form the
constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. Nothing else in this package calls the unit.
