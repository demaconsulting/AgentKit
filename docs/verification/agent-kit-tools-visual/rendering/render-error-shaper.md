### RenderErrorShaper Unit Verification Design

This document describes the unit-level verification strategy for the `RenderErrorShaper` class.

#### Verification Approach

The format scenarios draw the real broken sources the rendering spike recorded, through the real
rendering library, so the shaper is verified against the exceptions and messages the library
actually produces rather than a hand-written imitation. The classification scenarios construct
exception chains directly, because each must show that the type — and not the message text —
decided the code, and a real render cannot produce a misleading message on demand. The wording
scenarios construct failures directly, so each asserts one rule.

Unit tests reside in `Rendering/RenderErrorShaperTests.cs` within the
`DemaConsulting.AgentKit.Tools.Visual.Tests` project, using `Rendering/RenderSamples.cs`.

#### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: The CanvasNet Viewer, in process, for the format scenarios
- **File system**: A temporary tree whose paths the scrubbing scenario looks for
- **Isolation**: No state is shared between tests

#### Acceptance Criteria

A unit test run passes when all fourteen scenarios below pass without error or exception beyond those
explicitly asserted. A code decided by message text, a missing or wrong location, a misplaced caret,
a help topic named that the help tool does not serve, a host-oriented phrase or hint reaching the
model, a host path not scrubbed, or a note dropped each constitute a failure.

#### Test Scenarios

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-LocationAndCaret: A Syntax Error States Its Code, Line, Column and Caret

**Test**: `RenderErrorShaper_Shape_SyntaxError_StatesCodeLineColumnAndCaret`

Run as a theory over a Mermaid bad arrow (line 2, column 5), a PlantUML statement without a target
(2, 1), a Vega-Lite unknown mark (1, 41) and an OpenSCAD missing parenthesis (2, 1). Each report
opens with the format's code and the location, carries none of the library's framing, quotes the
line, and puts the caret under the column.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-LocationAndCaret: A Malformed SVG Is Located by the XML Reader

**Test**: `RenderErrorShaper_Shape_MalformedSvg_IsCorruptInputWithItsPosition`

A malformed SVG is `corrupt-input` at line 1 with the line quoted, the location having come from the
XML reader's position inside the message.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-ClassifiesByType: A Misleading Message Is Classified by Type

**Test**: `RenderErrorShaper_Classify_MisleadingMessage_IsClassifiedByType`

A Vega-Lite specification exception whose message speaks of a nesting limit — the message the
library gives for malformed JSON — is classified as `vega-lite-error`, not `budget-exceeded`.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-ClassifiesByType: A Refused File in the Chain Decides the Code

**Test**: `RenderErrorShaper_Classify_RefusedFileInTheChain_IsFileAccessDenied`

A `FileReferenceException` beneath a Vega-Lite render exception makes the failure
`file-access-denied`, located at line 3, column 20, pointer `/data/url`, with the message cleaned of
its framing and prefix.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-ClassifiesByType: Each Known Type Has Its Code

**Test**: `RenderErrorShaper_Classify_KnownExceptionTypes_AreGivenTheirCodes`

A budget exception, an unsupported-feature exception, an XML exception beneath invalid data, a
viewer exception with a code and an unknown exception are classified `budget-exceeded`,
`unsupported-feature`, `corrupt-input`, `page-out-of-range` and `operation-failed`.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HelpTopicOnlyWhenServed: A Help Topic Is Named Only When Served

**Test**: `RenderErrorShaper_Shape_HelpTopic_IsNamedOnlyWhenItExists`

With a help tool that serves `mermaid/diagnostics` the report ends with the help line; with one that
does not, or with none, it names no help topic.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostTextRewritten: Host-Oriented Text Is Rewritten

**Test**: `RenderErrorShaper_Shape_HostOrientedText_IsRewritten`

A refusal naming the file-access policy and "allowed roots", with a trailing host hint, is rewritten
to "by the access policy" and "the locations the agent may read" with this unit's own hint; a
command-line hint is dropped; a usable library hint is kept.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: Host Paths Are Scrubbed

**Test**: `RenderErrorShaper_Shape_HostPaths_AreScrubbed`

A message naming a file inside the workspace and one outside it by full path, in both separator
spellings, is shaped with the first relative to the working directory and the second as
`<outside>/b.png`, and no part of the temporary directory's absolute path remains.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: A Host Path in Another Case Is Scrubbed

**Test**: `RenderErrorShaper_Shape_HostPathInOtherCase_IsScrubbedWhereNamesIgnoreCase`

On Windows and macOS, whose file names ignore case, a message naming a file inside the workspace by
its full path in upper case is shaped with the path relative to the working directory, and no part
of the temporary directory's absolute path remains in any case. Elsewhere the two spellings name
different folders, so the scenario is skipped there, and the requirement links it only to Windows
and macOS results.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: A Sibling of a Root Is Not Partially Replaced

**Test**: `RenderErrorShaper_Scrub_SiblingOfARoot_IsNotPartiallyReplaced`

A text naming a file in the workspace's `-backup` sibling, in both separator spellings and quoted,
is scrubbed to `'<path>'` twice: never to a relative-looking `.-backup`, and with no part of the
sibling's absolute path left.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: A Root in Device-Prefix and File-URI Forms Is Replaced Whole

**Test**: `RenderErrorShaper_Scrub_RootInDevicePrefixAndFileUriForms_IsReplacedWhole`

With a workspace whose name holds a space, a file in it named with a `\\?\` prefix, with a `//?/`
prefix and as a percent-encoded `file:` URI is in each case scrubbed to the workspace-relative form,
with no prefix residue.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: An Unknown Absolute Path Is Replaced With a Placeholder

**Test**: `RenderErrorShaper_Scrub_UnknownAbsolutePath_IsReplacedWithAPlaceholder`

A text naming a file under the temporary folder (outside every grant), a drive-qualified path, a
UNC path, a quoted POSIX path and a `file:` URI has each replaced by `<path>`, with the surrounding
quotes and punctuation kept, while the JSON pointer `(/data/url)` and the relative path `../x.png`
are left exactly as written.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: An Inner Exception's Host Path Never Reaches the Model

**Test**: `RenderErrorShaper_Shape_InnerExceptionHostPath_NeverReachesTheModel`

A read failure whose inner I/O error names a host file under the temporary folder is shaped with the
outer message only — naming the file as the model wrote it — and none of the inner text. A viewer
failure whose own message embeds the same host path is shaped with the path as `'<path>'`. Neither
report contains any part of the temporary folder's path.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: Hostile Long Text Is Scrubbed Quickly

**Test**: `RenderErrorShaper_Scrub_HostileLongInput_FinishesQuicklyWithoutRawText`

Four texts of about 100 KB, each crafted against one fallback expression — an unclosed quote before
50,000 `/a` names, `C:/` before 100,000 commas, two back slashes before 100,000 letters, and
`file://` before 100,000 commas — are each scrubbed within two seconds without reaching a time
limit. The drive text becomes `<path>` and the URI text `<path>` followed by its commas, with no
`C:/` or `file:` left.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: Quoted JSON Pointers and Escapes Are Kept

**Test**: `RenderErrorShaper_Scrub_QuotedJsonPointerAndWebPaths_ArePreserved`

The quoted JSON pointers `"/data/values/0"` and `"/encoding/x/field"` and the quoted regular
expression `"\\d+\\s"` are left exactly as written, while the quoted host paths `'/home/u/y.png'`,
`'/tmp/cache/x'` and `'//server/share/x'` each become `'<path>'`.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: The Source Excerpt Is Shown as Written

**Test**: `RenderErrorShaper_Shape_SourceExcerpt_IsNotScrubbed`

A failure at line 2, column 6 of inline source whose line is `A:/x --> '/home/u/y.png'` is shaped
with that line quoted verbatim in the excerpt and the caret under column 6.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-HostPathsScrubbed: A Scrub That Times Out Withholds Its Text

**Test**: `RenderErrorShaper_Scrub_Timeout_ReturnsWithheldTextNeverRaw`

A scrub that throws a regular expression match timeout is run through the guard, which returns the
withheld-details text and none of the raw text.

##### AgentKitToolsVisual-Rendering-RenderErrorShaper-NotesListed: A Timeout Lists Its Notes

**Test**: `RenderErrorShaper_Shape_TimeoutWithNotes_ListsTheNotes`

A two-second timeout carrying two notes is shaped with its code, the seconds, the advice to
simplify the source and both notes.
