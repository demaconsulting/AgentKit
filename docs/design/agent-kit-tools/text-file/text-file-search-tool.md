### TextFileSearchTool

![AgentKit Tools TextFile Structure](TextFileView.svg)

The `TextFileSearchTool` class publishes the `text_file_search` tool.

#### Purpose

To find a text or regular expression pattern across text files the access policy permits the agent to
read, reporting matches in the grep-style form `path:line:content`. Search is the discovery step for
content: the path and 1-based line number it reports feed directly into `text_file_read`, and the
content it reports can become the exact raw text supplied to `text_file_replace`.

The unit's most important responsibility is non-disclosure. Every candidate comes from
`PathPolicy.EnumerateFiles`, so a search never discloses the content, path or existence of a file the
policy would refuse.

#### Data Model

The class is static and holds no state. A constructed tool holds exactly one captured value — the
`PathPolicy` supplied at construction — and that value is immutable.

| Member                | Type     | Invariant                                                |
| --------------------- | -------- | -------------------------------------------------------- |
| `ToolName`            | `string` | `text_file_search`; public constant; carries the prefix  |
| `DefaultFilePattern`  | `string` | `*`; used when the caller supplies no file pattern       |
| `RecursivePrefix`     | `string` | `**/`; accepted and stripped before enumeration          |
| `RecursiveEverything` | `string` | `**`; treated as every file recursively                  |
| `NoMatches`           | `string` | Returned as text when the search finds nothing           |
| `GroupSeparator`      | `string` | `--`; separates context groups when context is requested |

The search is additionally bounded by `PathPolicy.Limits.MaxResultCharacters` for the assembled
result. `MaxReadBytes` no longer gates a searched file: each file is streamed line by line so a file
larger than the read ceiling is searched rather than skipped. It bounds only a single pathological
line, whose match and rendering use a `MaxReadBytes`-length prefix so no one line is materialized
whole.

#### Key Methods

##### Create(PathPolicy policy)

Creates the tool. `internal` rather than public, because the pack is the unit of attachment and
`TextFilePack` is the only place the `text_file` family prefix is claimed.

**Preconditions:** `policy` is non-null.

**Algorithm:** validates `policy`, then builds a guarded delegate with these arguments: required
`pattern`, optional `path`, optional `filePattern`, `contextLines`, `ignoreCase`, `literal`, optional
`maxMatches`, and a cancellation token. `literal` defaults to `true`, so ordinary text is matched as
text unless the caller opts into regular expressions.

**Postconditions:** the returned tool carries `ToolName`, carries a non-empty description, and is
governed by the supplied policy for the rest of its life.

##### The tool delegate

**Algorithm**, in this order:

1. A null or empty `pattern` is refused as `InvalidRequest`.
2. Negative `contextLines` or `maxMatches` is refused as `InvalidRequest`.
3. A named `path` that the policy refuses is returned as `PathNotPermitted`. An omitted path is a
   discovery request and searches every readable discovery root.
4. The matcher is compiled. A literal pattern is regex-escaped; a regular expression pattern is used
   as supplied. An invalid regular expression is refused.
5. The file glob is normalized. A missing pattern becomes `*`; a leading `**/` or a bare `**` is
   honored and reduced to the file-name pattern used by the policy enumeration.
6. The result is assembled from the permitted candidates.
7. No matches are returned as `No matches.`, not as a refusal.
8. A result longer than `MaxResultCharacters` is refused with advice to narrow the search.
9. Otherwise the grep-style listing is returned.

##### BuildResultAsync(...)

Enumerates the permitted files, sorts them ordinally, and searches each in turn until the match cap
is reached. A running match-and-group progress is carried across files so the cap and the group
separators span the whole result.

##### AppendFileMatchesAsync(...)

Skips a file whose leading bytes are binary — detected by `TextFileBinaryGuard` from a leading sniff
that never materializes the whole file — then streams the file line by line through
`TextLines.EnumerateLines`, running the matcher against each line's content. Only the matched lines
and the context lines around them are retained; the rest of the file is streamed solely to keep the
line numbering and the true total exact, so a file larger than the read ceiling is searched within
bounded memory rather than skipped for size. Each match is rendered as `path:line:content`. Context
lines are rendered with `path-line-content`, and adjacent context windows are merged so the same line
is not printed twice. When context is requested, a `--` line separates one group from the next. A file
that cannot be read is skipped silently, because search must not disclose anything a direct read would
refuse or fail to provide.

#### Error Handling

Model-controlled malformed requests are returned as refusals. A missing or refused search root is a
policy refusal rather than an empty result, so the model learns where it may search. Invalid regular
expressions are also refused and the message tells the model to fix the expression or set `literal`
to `true`.

Access failures while reading a candidate file are caught by explicit classification — `IOException`,
`UnauthorizedAccessException`, `NotSupportedException`, `SecurityException` — and cause that
candidate to be skipped. Cancellation is not classified, so it propagates through the async search.

**Enumeration is the security boundary.** The implementation never calls recursive directory APIs to
find candidates. A file the policy would refuse is not listed as a candidate, so its path,
content and existence stay undisclosed.

#### Dependencies

`PathPolicy` and `ToolLimits` for enumeration and ceilings, `ToolResult` for results,
`GuardedToolFactory` for construction, `TextLines` for the shared line model and path separator
normalization, and `TextFileBinaryGuard` for the leading binary sniff. It uses `Regex` from
`System.Text.RegularExpressions`, `StringBuilder` and `Encoding` from `System.Text`, and file APIs
including `FileStream` and `StreamReader` from the Base Class Library. `AIFunction`, from
`Microsoft.Extensions.AI.Abstractions`, is the constructed tool type.

#### Callers

`TextFilePack.CreateTools` is the only caller of `Create`, and the constructed tool is invoked by the
agent runtime an application composed it into. Nothing else in this package calls the unit.
