### FileListTool

![AgentKit Tools File Structure](FileView.svg)

The `FileListTool` class publishes the `file_list` tool.

#### Purpose

To list files of any type beneath a permitted directory, or across every permitted discovery root
when no directory is supplied. It is the discovery tool for the file family, the tool a model reaches
for on its own to learn the names a read or change should have used.

The unit lists file entities, not text content. Its output gives a model names it can hand to
`file_copy`, `file_move`, `file_delete`, or a content reader such as `text_file_read`.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member                 | Type     | Invariant                                               |
| ---------------------- | -------- | ------------------------------------------------------- |
| `ToolName`             | `string` | `file_list`; public constant; carries the family prefix |
| `DefaultSearchPattern` | `string` | `*`; used when no pattern is supplied                   |
| `RecursivePrefix`      | `string` | `**/`; accepted and stripped before enumeration         |
| `RecursiveEverything`  | `string` | `**`; treated as every file recursively                 |
| Empty location markers | `string` | Name read-only or read-write access in discovery output |
| `NameSeparator`        | `string` | Newline; separates headers, markers and names           |

The listing is bounded by `PathPolicy.Limits.MaxResultCharacters`. A listing that exceeds the limit
is refused rather than truncated.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`FilePack` is the only place the `file` family prefix is claimed.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded synchronous delegate with optional
`directory` and optional `pattern` arguments. Omitted arguments reach the tool body as a discovery
listing and a default pattern rather than framework errors.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate: `(string? directory = null, string? pattern = null)`

**Algorithm**, in this order:

1. The pattern is normalized. Null, empty or whitespace becomes `*`; a bare `**` also becomes `*`;
   a leading `**/` is stripped; any remaining directory portion is discarded so the final file-name
   portion is used by policy enumeration.
2. An omitted directory is a discovery request. Each discovery root from the policy is listed under
   its absolute header.
3. A named directory is resolved with `policy.TryResolveRead`. A refusal is returned as
   `PathNotPermitted`.
4. The named directory is listed under the working-directory anchor when the request was relative and
   the policy can emit relative names; otherwise it is listed under its resolved absolute header.
5. An empty named-directory result returns `No files matched.`.
6. A result over `MaxResultCharacters` is refused with advice to narrow the pattern.
7. Otherwise the listing is returned as text.

##### ListEveryLocation(PathPolicy policy, string pattern)

Builds a discovery listing. Every permitted location is represented, including an empty one. An empty
read-write location emits `(no files - read-write)`; an empty read-only location emits
`(no files - read-only)`. This prevents a model from assuming the working directory is the only
usable location.

##### ListOneDirectory(...)

Builds a listing for one named directory. Names are sorted ordinally and rendered relative to the
chosen header. Separators are normalized to forward slash so output is stable across hosts.

#### Error Handling

A refused named directory is returned as a policy denial. Empty listings are not errors; they are
facts returned as text. An oversized listing is refused rather than truncated, because truncation
would hide files the model would then never ask for.

The unit does not catch file-system exceptions directly during enumeration because enumeration is
owned by `PathPolicy.EnumerateFiles`. A null policy at construction raises `ArgumentNullException` as
a composing-application error.

**Enumeration is a policy call.** The unit never enumerates recursively through `Directory` itself.
That keeps symbolic links or junctions outside the grants from leaking as listed names.

#### Dependencies

`PathPolicy` and `ToolLimits` for enumeration, dialect and ceilings; `ToolResult` for results; and
`GuardedToolFactory` for construction. From the Base Class Library it uses `Path`, ordering and
string helpers. `AIFunction`, from `Microsoft.Extensions.AI.Abstractions`, is the constructed tool
type.

#### Callers

`FilePack.CreateTools` is the only caller of `Create`. The tool is invoked by the agent runtime and
is the discovery tool a model reaches for on its own when it needs to learn a path.
