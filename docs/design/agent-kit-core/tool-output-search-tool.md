## ToolOutputSearchTool

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolOutputSearchTool` class builds the `tool_output_search` tool, which finds a pattern in a
large tool output the composition retained, reporting matching lines by number.

### Purpose

A retained output is usually large precisely because it holds many things, and a model that can
only page must read most of it to find the one it needs. Searching first and then reading around
the line found is how a developer reads a large log, and the same pairing `text_file_search` and
`text_file_read` give a model over files.

It is offered and confined exactly as `tool_output_read` is: appended by `ToolOutputSpill.Apply`,
reading only that call's store, and taking an identifier and search arguments with no path
parameter.

The class is static; a constructed tool captures only the lock-guarded store and the immutable
limits and is safe for concurrent use.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `ToolName` | `const string` | `tool_output_search`. |
| `NoMatches` | `const string` | `No matches.` |
| `GroupSeparator` | `const string` | `--`, separating runs of matches as grep does. |
| `MatchTimeout` | `TimeSpan` | One second per line, the bound `text_file_search` applies. |
| `SearchBudget` | `TimeSpan` | One second for the whole search, checked between lines. |

Parameters: `id`, `pattern`, `contextLines` (default 0), `ignoreCase` (default false), `literal`
(default true), `maxMatches` (optional).

### Key Methods

#### Create(ToolOutputStore store, ToolLimits limits)

Builds the tool through `GuardedToolFactory.Create` with a delegate declared to return `object`.

#### Search

1. Refuses a missing pattern, or negative `contextLines` or `maxMatches`, as `InvalidRequest`.
2. Looks the identifier up; a failure returns `ToolOutputStore.Refusal`.
3. Passes the entry to `SearchEntry` with `SearchBudget`, which builds the matcher with
   `RegexOptions.CultureInvariant` (plus `IgnoreCase` when asked) and the one-second per-line
   timeout; a literal pattern is escaped first. An invalid expression is refused as
   `InvalidRequest`.
4. Makes one pass over the lines in order. Before each line it checks the elapsed time against the
   search budget; a spent budget, like a `RegexMatchTimeoutException` on a line, is refused as
   `InvalidRequest` naming the limit and the output. A line that times out is refused as
   `The search pattern took longer than 1 second to evaluate against one line of {id}.`; a spent
   budget, which applies to literal and regular-expression searches alike, is refused as
   `The search took longer than 1 second to run against {id}.`. The per-line timeout alone
   would let an expression just under it on every line run for one timeout per line; the budget
   bounds the whole search by itself plus at most one line's timeout — about two seconds in all —
   so a pathological expression cannot stall the turn.
5. Each line is tested until the cap is reached. A match is written as soon as it is found, after
   whatever of its leading context the listing does not already hold, and the lines after it are
   written as trailing context. The output is grep-style: `N:content` for a match, `N-content` for
   context — including a line that matches past the cap — and `--` between runs when context was
   requested and a run does not touch the previous one. The numbers are the ones
   `tool_output_read` pages by. No list of matches or ranges is held.
6. Before each piece is written, its size is checked against `MaxResultCharacters`; a piece that
   would carry the listing over the ceiling ends the search at once with a `ResourceTooLarge`
   refusal naming the ceiling. The listing therefore never holds more than the ceiling's worth of
   result, and an oversized result costs no more than the ceiling's worth of reading.
7. No match returns `No matches.`; otherwise the listing is returned.

Because the size refusal is decided as the listing is written, a search whose result crosses the
ceiling before a later line would time out is now refused as `ResourceTooLarge`, where the earlier
find-then-render order reported the timeout. Both outcomes are refusals; only which one is reported
when both apply has changed.

#### SearchEntry(ToolOutputEntry entry, ToolLimits limits, string pattern, int contextLines, bool ignoreCase, bool literal, int? maxMatches, TimeSpan searchBudget, CancellationToken cancellationToken)

The search logic over one resolved entry — steps 3 to 7 above. It is internal rather than private
so the unit tests can drive it with an entry whose lines count every read and with an already-spent
time budget, proving an oversized result and a slow search each stop early without real waiting.

### Error Handling

| Condition | Handling |
| --- | --- |
| Missing pattern, negative context or cap | `InvalidRequest` refusal returned |
| Invalid regular expression | `InvalidRequest` refusal returned |
| Expression times out on a line, or the search budget is spent | `InvalidRequest` refusal returned |
| Missing, malformed, unknown or evicted identifier | Refusal from `ToolOutputStore.Refusal` |
| Result over the ceiling | `ResourceTooLarge` refusal returned |
| Null store or limits at construction | `ArgumentNullException` propagates |

### Dependencies

- **ToolOutputStore** — lookups and refusals; see *ToolOutputStore Unit Design*.
- **GuardedToolFactory**, **ToolResult**, **ToolLimits** — see their unit designs.
- **System.Text.RegularExpressions** — the matcher and its timeout.

### Callers

**ToolOutputSpill** — creates the tool in `Apply`. The model invokes it through the runtime.
