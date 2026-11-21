## ToolOutputReadTool

![AgentKit Core Structure](AgentKitCoreView.svg)

The `ToolOutputReadTool` class builds the `tool_output_read` tool, which reads a range of lines
from a large tool output the composition retained instead of returning whole.

### Purpose

A retained output is only useful if the model can read it. `tool_output_read` pages through one by
line number, in exactly the shape `text_file_read` pages through a file, so a model already knows
how to read a page and how to ask for the next one.

**It cannot reach the file system.** Its parameters are an output identifier and a line range;
there is no path parameter, so no request it accepts can name a file. It reads only the store
`ToolOutputSpill.Apply` allocated for its composition. It is appended by that call and is never
attached directly by an application.

The class is static; a constructed tool captures only the lock-guarded store and the immutable
limits and is safe for concurrent use.

### Data Model

| Member | Type | Description |
| --- | --- | --- |
| `ToolName` | `const string` | `tool_output_read`. |
| `ToolDescription` | `const string` | The description the model reads. |
| `LineNumberDelimiter` | `const string` | `"\| "`, matching `text_file_read`. |

Parameters: `id` (string, required), `startLine` (1-based, optional, default 1), `lineCount`
(optional). Every parameter is nullable so an omitted argument reaches the body as a request the
tool answers rather than a framework error.

### Key Methods

#### Create(ToolOutputStore store, ToolLimits limits)

Builds the tool through `GuardedToolFactory.Create` with a delegate declared to return `object`.

#### Read

1. Refuses `startLine < 1` or `lineCount < 0` as `InvalidRequest`.
2. Looks the identifier up; a failure returns `ToolOutputStore.Refusal`.
3. Passes the entry to `ReadEntry`, which does the rest. A window starting past the end, or a zero
   count, returns the honest empty header `output-3 lines S-(S-1) of N`.
4. With an explicit count, accumulates the page's exact size — the header, then each line's cost —
   and refuses the range as `ResourceTooLarge` at the first line that carries it over
   `MaxResultCharacters`: `Lines A-B of output-3 exceed the M-character result limit. Request
   fewer lines.` A truncated page would hide lines the model believes it read. The refusal names
   the range and the ceiling but not the page's full size, which could only be stated by walking
   the whole range; refusing early means even a request for every line reads no more of the output
   than the ceiling admits. Only a page that fits is built, into a buffer of its exact size.
5. With no count, takes as many whole lines from the start as fit the ceiling, budgeting against
   the widest possible header and reading lines only until the first that does not fit. No range
   end was requested, so this is not a truncation; the header states exactly where the page
   stopped. When not even one line fits, the result is a `ResourceTooLarge` refusal naming the
   ceiling.

Steps 4 and 5 share one line-cost function — the line feed before the line, its padded number,
the delimiter and the content — so their accounting cannot drift from the page rendered.

#### ReadEntry(ToolOutputEntry entry, ToolLimits limits, int start, int? lineCount)

The page logic over one resolved entry — steps 3 to 5 above. It is internal rather than private
so the unit tests can drive it with an entry whose lines count every read, proving an oversized
request reads no more of the output than the ceiling admits.

**Page shape:** a header `output-3 lines A-B of N` followed by each line as its number, padded to
the width of `N`, then `| `, then the content.

### Error Handling

| Condition | Handling |
| --- | --- |
| Invalid range | `InvalidRequest` refusal returned |
| Missing or malformed identifier | `InvalidRequest` refusal returned |
| Unknown or evicted identifier | `TargetNotFound` refusal returned |
| Page over the ceiling | `ResourceTooLarge` refusal returned, naming the range and the ceiling |
| Null store or limits at construction | `ArgumentNullException` propagates |

### Dependencies

- **ToolOutputStore** — lookups and refusals; see *ToolOutputStore Unit Design*.
- **GuardedToolFactory**, **ToolResult**, **ToolLimits** — see their unit designs.

### Callers

**ToolOutputSpill** — creates the tool in `Apply`. The model invokes it through the runtime.
