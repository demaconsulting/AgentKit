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

Parameters: `id`, `pattern`, `contextLines` (default 0), `ignoreCase` (default false), `literal`
(default true), `maxMatches` (optional).

### Key Methods

#### Create(ToolOutputStore store, ToolLimits limits)

Builds the tool through `GuardedToolFactory.Create` with a delegate declared to return `object`.

#### Search

1. Refuses a missing pattern, or negative `contextLines` or `maxMatches`, as `InvalidRequest`.
2. Looks the identifier up; a failure returns `ToolOutputStore.Refusal`.
3. Builds the matcher with `RegexOptions.CultureInvariant` (plus `IgnoreCase` when asked) and the
   one-second timeout; a literal pattern is escaped first. An invalid expression is refused as
   `InvalidRequest`.
4. Tests each line in order until the cap; a `RegexMatchTimeoutException` is refused as
   `InvalidRequest`, naming the timeout, so a pathological expression cannot stall the turn.
5. No match returns `No matches.`. Otherwise each match's context window is merged into
   contiguous ranges and rendered grep-style: `N:content` for a match, `N-content` for context,
   and `--` between runs when context was requested. The numbers are the ones `tool_output_read`
   pages by.
6. A result over `MaxResultCharacters` is refused as `ResourceTooLarge`, naming the ceiling.

### Error Handling

| Condition | Handling |
| --- | --- |
| Missing pattern, negative context or cap | `InvalidRequest` refusal returned |
| Invalid regular expression | `InvalidRequest` refusal returned |
| Expression times out | `InvalidRequest` refusal returned |
| Missing, malformed, unknown or evicted identifier | Refusal from `ToolOutputStore.Refusal` |
| Result over the ceiling | `ResourceTooLarge` refusal returned |
| Null store or limits at construction | `ArgumentNullException` propagates |

### Dependencies

- **ToolOutputStore** — lookups and refusals; see *ToolOutputStore Unit Design*.
- **GuardedToolFactory**, **ToolResult**, **ToolLimits** — see their unit designs.
- **System.Text.RegularExpressions** — the matcher and its timeout.

### Callers

**ToolOutputSpill** — creates the tool in `Apply`. The model invokes it through the runtime.
