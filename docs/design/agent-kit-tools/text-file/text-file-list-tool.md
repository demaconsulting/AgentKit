### TextFileListTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileListTool` class publishes the `text_file_list` tool.

#### Purpose

To tell an agent what files it may read beneath a directory, as names it can immediately use, and
to never mention a file it may not read. It is the tool the read and write tools redirect a model
to when it asked for the wrong name, so it is also the unit that determines whether an agent
recovers from a mistake or loops on it. It is usually the first tool an agent calls, and at that
point the agent has no directory name to give — so answering a request that names no directory is
part of the unit's purpose rather than an edge case.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member                 | Type     | Invariant                                                    |
|------------------------|----------|--------------------------------------------------------------|
| `ToolName`             | `string` | `text_file_list`; public constant; carries the family prefix |
| `ToolDescription`      | `string` | Non-empty; the basis on which a model chooses this tool      |
| `DefaultSearchPattern` | `string` | `*`; used when the request supplies no pattern               |
| `NameSeparator`        | `string` | A newline; a comma would be ambiguous within a file name     |
| `ReportedSeparator`    | `char`   | `/` on every platform                                        |
| `BlockSeparator`       | `string` | A blank line between discovery listing blocks                |
| `NoMatches`            | `string` | The result reported when nothing matched                     |
| `EmptyReadWriteMarker` | `string` | `(no files - read-write)`; empty read-write location marker  |
| `EmptyReadOnlyMarker`  | `string` | `(no files - read-only)`; empty read-only location marker    |

The listing returned is one or more newline-separated blocks, bounded by
`PathPolicy.Limits.MaxResultCharacters`. Each block starts with an absolute location header, written
once for that location, followed by the matching names in ordinal order relative to that header.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate here is
**synchronous** and declared to return `object`; the guard applies identically to a synchronous
tool, and `object` remains the only type expressing the refusal-or-listing union.

**Both parameters carry a default**, which is load-bearing rather than cosmetic. A parameter with
no default is required by the function factory, and an omitted argument then fails inside the
factory before the tool body is reached — the model receives an opaque framework error rather
than anything it can act on. This was observed in practice. Declaring the defaults is what lets
the omitted case reach the body at all.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? directory = null, string? searchPattern = null)`

**Algorithm**, in order:

1. An absent or empty `searchPattern` becomes `*` — "everything here" is the least surprising
   reading for a model exploring a directory it does not know
2. An omitted, empty, whitespace or placeholder directory is a discovery request. It enumerates
   every `policy.DiscoveryRoots()` location, rendering each location once as an absolute header and
   rendering its matching names beneath it. **Every permitted location contributes a block, including
   one that currently holds no matching file**: an empty location renders as its absolute header
   followed by a single marker line naming its access level, taken from
   `policy.TryResolveWrite(root, …)` — `(no files - read-write)` when a read-write grant covers it,
   `(no files - read-only)` otherwise. A single granted working directory therefore establishes the
   relative dialect — bare names beneath the working-directory header — while an ungranted working
   directory can only be addressed by absolute headers
3. Otherwise `policy.TryResolveRead(directory, …)` resolves the requested directory; a refusal is
   returned as `PathNotPermitted`. Resolving first is what produces a refusal rather than the empty
   sequence enumeration alone would return, and the resolved location participates in the output
   dialect decision. **The directory is passed to the policy exactly as the model supplied it**:
   the policy interprets a relative directory against the working directory, accepts an absolute
   directory unchanged, and this unit invents no reading of its own
4. **`policy.EnumerateFiles(…, pattern)` performs the enumeration**, using each discovery root or
   the requested directory as appropriate. This is the unit's central invariant, stated here because
   it is the thing a future change is most likely to get wrong
5. The output anchor is chosen to mirror the caller's path dialect. A relative request within the
   granted working directory uses the working directory as the header, so names beneath it are bare
   working-directory-relative names the model can hand straight back to `text_file_read`. An
   absolute request, or a location outside the working directory, uses the requested location's own
   absolute header because a relative name could not truthfully address it
6. Each real path is relativized against the chosen header, its separators normalized to `/`, and
   the names sorted in ordinal order
7. No matches returns the text `No files matched.` — an empty listing is a fact, not a refusal
8. A joined listing longer than `MaxResultCharacters` is refused as `ResourceTooLarge`, naming the
   ceiling and telling the model to narrow its pattern
9. Otherwise the listing is returned

**Preconditions:** none beyond a constructed tool; no caller-supplied directory can raise an
exception, and none is refused for being absent.

**Postconditions:** every name beneath a location header denotes a file the read decision permits;
the header states the anchor for those names; the same tree always produces the same listing.

**The location-header shape.** A full absolute path repeated for every listed file would spend
roughly ninety characters per file on the same prefix and trip the result ceiling before the model
learned much. The header pays that absolute-location cost once per listed location, and the short
names beneath it demonstrate the form the model should imitate. When that header is the granted
working directory, the names are bare working-directory-relative paths that can be passed directly
to `text_file_read`; when the header is another location, the absolute header is the truthful way
to address it.

**The omitted-directory reading.** An omitted, empty, whitespace or placeholder directory lists
every permitted location, each under its own absolute header. The previous behavior — a refusal —
was wrong in both directions: it told a model its request was malformed when the request was the
only one the model could make, and it sent the model guessing at locations it has no business
exploring. Discovery now establishes the path dialect up front: a single granted working directory
establishes bare relative names, while an ungranted working directory produces only absolute
headers because relative names would not address a granted location. **Discovery reports every
permitted location, including one that currently holds no matching file.** An empty location renders
as its absolute header followed by a single marker line — `(no files - read-write)` or
`(no files - read-only)`, chosen by `policy.TryResolveWrite` — so a model learns the location exists
and may address it, most importantly a freshly created, still-empty output location whose path the
agent needs on its very first run. The marker sits beneath its header with a single newline and
cannot be mistaken for a file name, an error, or the next location's absolute header. An empty string
now arises only when the policy carries no grants at all, which the caller still reports as
`No files matched.`

**The enumeration invariant.** Enumeration goes through `PathPolicy.EnumerateFiles` and never
through `Directory.EnumerateFiles` or `Directory.GetFiles`. Recursive enumeration performed by the
operating system follows directory junctions and symbolic links, so a directly-enumerating
implementation would surface files lying outside the permitted location even though reading them is
denied — and disclosing that such a file exists, then inviting the agent to ask for it, is itself
the leak. The policy filters every candidate through the same read decision direct access uses,
which is what guarantees a listing and a read can never reach different conclusions. A dedicated
scenario, `TextFileListTool_List_LinkToOutsideRoot_DoesNotListEscapedFile`, exercises a real
reparse point to prove it.

#### Error Handling

Everything a model controls produces a returned refusal or an answer, never an exception. The only
exception the unit raises is `ArgumentNullException` for a null policy at construction.

The unit needs no exception classification of its own: `PathPolicy.EnumerateFiles` promises never to
throw for a policy or file system reason — a denied, missing or unreadable directory yields an empty
sequence — and the resolution of the requested directory is already guarded by the policy. The
distinction between "the directory holds nothing" and "you may not look there" is preserved by
resolving the directory before enumerating it, so a refused directory is reported as a refusal
rather than as an empty listing an agent would misread.

**An oversized listing is refused, never truncated.** A truncated listing silently hides files the
model would then never ask for, with no way to detect the omission.

**Every refusal states what to do instead**, because an agent told only "no" retries the same
request until it abandons the task. A refusal produced by `PathPolicy` is returned unchanged: it
states what was requested, how a relative request was interpreted, and the permitted locations with
their access levels. The oversized-listing refusal this unit composes names only the
`MaxResultCharacters` ceiling and tells the model to narrow its pattern.

#### Dependencies

`PathPolicy` for both the directory decision and the enumeration, `ToolLimits` for the result
ceiling, `ToolResult` for every result it returns, and `GuardedToolFactory` for construction. From
the Base Class Library: `Path` and `StringComparer`. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the form the constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. `TextFileReadTool` names this unit's `ToolName`
in two of its refusals, but does not call it.
