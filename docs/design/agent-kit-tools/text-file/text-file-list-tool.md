### TextFileListTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileListTool` class publishes the `text_file_list` tool.

#### Purpose

To tell an agent what files it may read beneath a directory, as names it can immediately use, and
to never mention a file it may not read. It is the tool the read and write tools redirect a model
to when it asked for the wrong name, so it is also the unit that determines whether an agent
recovers from a mistake or loops on it.

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
| `NoMatches`            | `string` | The result reported when nothing matched                     |

The listing returned is a newline-separated sequence of names, in ordinal order, relative to the
requested directory, bounded by `PathPolicy.Limits.MaxResultCharacters`.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment.

**Preconditions:** `policy` is non-null; it is the policy the composition was built with.

**Algorithm:** validates `policy`, then returns
`GuardedToolFactory.Create(delegate, ToolName, ToolDescription)`. The delegate here is
**synchronous** and declared to return `object`; the guard applies identically to a synchronous
tool, and `object` remains the only type expressing the refusal-or-listing union.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(directory, searchPattern)`

**Algorithm**, in order:

1. An absent, empty or whitespace `directory` is refused as `InvalidRequest`
2. An absent or empty `searchPattern` becomes `*` — "everything here" is the least surprising
   reading for a model exploring a directory it does not know
3. `policy.TryResolveRead(directory, …)` — a refusal is returned as `PathNotPermitted`. Resolving
   first is what produces a refusal rather than the empty sequence enumeration alone would return,
   and the resolved location is what every reported name is made relative to
4. **`policy.EnumerateFiles(directory, pattern)` performs the enumeration.** This is the unit's
   central invariant, stated here because it is the thing a future change is most likely to get
   wrong
5. Each real path is relativized against the resolved directory, its separators normalized to `/`,
   and the names sorted in ordinal order
6. No matches returns the text `No files matched.` — an empty listing is a fact, not a refusal
7. A joined listing longer than `MaxResultCharacters` is refused as `ResourceTooLarge`, naming the
   ceiling and telling the model to narrow its pattern
8. Otherwise the listing is returned

**Preconditions:** none beyond a constructed tool; every argument is validated into a refusal.

**Postconditions:** every name in the listing denotes a file the read decision permits; no name is
absolute; the same tree always produces the same listing.

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

Everything a model controls produces a returned refusal, never an exception. The only exception the
unit raises is `ArgumentNullException` for a null policy at construction.

The unit needs no exception classification of its own: `PathPolicy.EnumerateFiles` promises never to
throw for a policy or file system reason — a denied, missing or unreadable directory yields an empty
sequence — and the resolution of the requested directory is already guarded by the policy. The
distinction between "the directory holds nothing" and "you may not look there" is preserved by
resolving the directory before enumerating it, so a refused directory is reported as a refusal
rather than as an empty listing an agent would misread.

**An oversized listing is refused, never truncated.** A truncated listing silently hides files the
model would then never ask for, with no way to detect the omission.

No refusal message contains a path, a permitted location or a directory separator, and no successful
listing contains an absolute path.

#### Dependencies

`PathPolicy` for both the directory decision and the enumeration, `ToolLimits` for the result
ceiling, `ToolResult` for every result it returns, and `GuardedToolFactory` for construction. From
the Base Class Library: `Path` and `StringComparer`. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the form the constructed tool takes.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by
the agent runtime an application composed it into. `TextFileReadTool` names this unit's `ToolName`
in two of its refusals, but does not call it.
