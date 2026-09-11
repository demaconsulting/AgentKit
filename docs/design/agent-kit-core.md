# System Design

This document provides the system-level design for the AgentKit Core.

![AgentKit Core Structure](AgentKitCoreView.svg)

## Architecture

The AgentKit Core is a minimal .NET library template demonstrating DEMA Consulting
best practices. The system consists of:

- **Demo Unit**: Simple greeting functionality demonstrating library patterns
- **RealPathResolver Unit**: Reports the real file system location of a path, following
  symbolic links and directory junctions at every path component
- **PathRule Unit**: One access rule — unrestricted or confined to a location — carrying its own
  denied patterns
- **PathPolicy Unit**: Pairs an independent read rule and write rule, and provides the single
  containment decision used by both direct access and directory enumeration
- **ToolLimits Unit**: Carries the ceilings a tool observes when reading, returning and
  attaching content
- **ToolResult Unit**: Constructs the results a guarded tool returns to the model, and defines
  the reasons a tool may refuse an operation
- **ToolName Unit**: Defines the tool naming convention and validates names against it
- **GuardedToolFactory Unit**: The only supported way to construct a tool, applying the
  result-delivery guard and the naming rules to every tool it creates

There are no subsystems. The `Demo` unit is a self-contained leaf class exposed directly through
the public API; see _Demo Unit Design_. The three path-safety units form one collaboration:
`PathPolicy` resolves a requested path through `RealPathResolver` and then consults exactly one
`PathRule`, which itself resolved its confined location through `RealPathResolver` when it was
created. `RealPathResolver` depends on nothing within the system, so the collaboration is
acyclic.

The four tool-contract units form a second collaboration. A pack author composes a tool name
through `ToolName` and hands it, with a delegate, to `GuardedToolFactory`, which validates the
name through `ToolName` and applies the result-delivery guard to every tool it creates. The
delegate returns a value built by `ToolResult` — text, content, or a refusal — and the guard is
what delivers that value to the runtime in the form the tool produced it. The two collaborations
meet at `PathPolicy`, which carries a `ToolLimits` so that every tool a host governs observes one
budget. `ToolLimits` and `ToolName` depend on nothing within the system, so this collaboration is
likewise acyclic.

## External Interfaces

The system exposes the following public API to external consumers:

- **Demo()**: Default constructor; initializes the instance with the default prefix `"Hello"`
- **Demo(string prefix)**: Custom-prefix constructor; initializes the instance with the specified
  prefix. Throws `ArgumentNullException` if `prefix` is null; throws `ArgumentException` if
  `prefix` is an empty string.
- **Demo.Prefix**: Read-only property that returns the greeting prefix configured at construction
- **Demo.DemoMethod(string name)**: Returns a greeting string in the format `{prefix}, {name}!`.
  Throws `ArgumentNullException` if `name` is null; throws `ArgumentException` if `name` is an
  empty string.

| Interface                      | Direction        | Format                        | Constraints                  |
|--------------------------------|------------------|-------------------------------|------------------------------|
| `Demo()`                       | Inbound          | Constructor call              | None; always succeeds        |
| `Demo(string prefix)`          | Inbound          | Constructor call              | `prefix` non-null, non-empty |
| `Demo.Prefix`                  | Outbound         | `string` property read        | None; always succeeds        |
| `Demo.DemoMethod(string name)` | Inbound/Outbound | Method call / `string` return | `name` non-null, non-empty   |

The system additionally exposes the path-safety API:

- **RealPathResolver.Resolve(string path)**: Returns the real, absolute, normalized location of
  `path`. Throws `ArgumentNullException` for a null path, `ArgumentException` for an empty or
  invalid path, and `IOException` for a cyclic or over-deep link chain.
- **PathRule.Unrestricted(IEnumerable&lt;string&gt;? denyPatterns)**: Creates a rule with no
  location constraint. Throws `ArgumentException` for a null or empty pattern.
- **PathRule.Rooted(string root, IEnumerable&lt;string&gt;? denyPatterns)**: Creates a rule
  confined to `root`, resolved to its real location. Throws `ArgumentNullException` for a null
  location and `ArgumentException` for an empty or invalid location or pattern.
- **PathRule.Root**, **PathRule.DenyPatterns**: Read-only properties exposing the rule's real
  confined location (or none) and its denied patterns.
- **PathRule.Allows(string realPath)**: Returns whether an already-resolved location is
  permitted.
- **PathPolicy(PathRule readRule, PathRule writeRule)**: Constructs a policy with the library's
  documented resource ceilings. Throws `ArgumentNullException` when either rule is null.
- **PathPolicy(PathRule readRule, PathRule writeRule, ToolLimits limits)**: Constructs a policy
  with explicit resource ceilings. Throws `ArgumentNullException` when any argument is null.
- **PathPolicy.ReadRule**, **PathPolicy.WriteRule**: Read-only properties exposing the two rules.
- **PathPolicy.Limits**: Read-only property exposing the ceilings every governed tool observes.
- **PathPolicy.TryResolveRead / TryResolveWrite(string path, out string? realPath, out string?
  denialMessage)**: Returns whether the access is permitted, with the real location on success
  and a redacted reason on refusal.
- **PathPolicy.EnumerateFiles(string directory, string searchPattern)**: Returns the real
  locations of the permitted files beneath a directory.

| Interface                    | Direction        | Format                         | Constraints                   |
|------------------------------|------------------|--------------------------------|-------------------------------|
| `RealPathResolver.Resolve`   | Inbound/Outbound | Method call / `string` return  | `path` non-null, non-empty    |
| `PathRule.Unrestricted`      | Inbound/Outbound | Factory call / `PathRule`      | Patterns non-null, non-empty  |
| `PathRule.Rooted`            | Inbound/Outbound | Factory call / `PathRule`      | `root`, patterns non-empty    |
| `PathRule.Root`              | Outbound         | `string?` property read        | None; always succeeds         |
| `PathRule.DenyPatterns`      | Outbound         | `IReadOnlyList<string>` read   | None; always succeeds         |
| `PathRule.Allows`            | Inbound/Outbound | Method call / `bool` return    | Resolved, non-null, non-empty |
| `new PathPolicy(...)`        | Inbound          | Constructor call               | Both rules non-null           |
| `PathPolicy.ReadRule`        | Outbound         | `PathRule` property read       | None; always succeeds         |
| `PathPolicy.WriteRule`       | Outbound         | `PathRule` property read       | None; always succeeds         |
| `PathPolicy.Limits`          | Outbound         | `ToolLimits` property read     | None; always succeeds         |
| `PathPolicy.TryResolveRead`  | Inbound/Outbound | Method call / `bool` and `out` | `path` non-null, non-empty    |
| `PathPolicy.TryResolveWrite` | Inbound/Outbound | Method call / `bool` and `out` | `path` non-null, non-empty    |
| `PathPolicy.EnumerateFiles`  | Inbound/Outbound | Method call / `IEnumerable`    | Both args non-null, non-empty |

The system additionally exposes the tool-contract API:

- **new ToolLimits(int maxReadBytes, int maxResultCharacters, int maxBinaryBytes, int
  maxAttachmentsPerTurn)**: Creates a set of resource ceilings. Every parameter is optional and
  defaults to the corresponding published constant. Throws `ArgumentOutOfRangeException` for a
  negative ceiling; a ceiling of zero is accepted and disables the operation.
- **ToolLimits.Default**: The shared set of ceilings a host receives when it configures nothing.
- **ToolLimits.MaxReadBytes**, **MaxResultCharacters**, **MaxBinaryBytes**,
  **MaxAttachmentsPerTurn**: Read-only properties exposing the configured ceilings.
- **ToolResult.Text(string text)**: Returns the supplied text. Throws `ArgumentNullException`
  for a null text; an empty text is permitted.
- **ToolResult.Binary(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)**:
  Returns content carrying its media type, preceded by the caption when one is supplied. Throws
  `ArgumentNullException` / `ArgumentException` for a missing or empty media type.
- **ToolResult.Image(ReadOnlyMemory&lt;byte&gt; data, string mediaType, string? caption)**: As
  `Binary`, and additionally throws `ArgumentException` when the media type does not denote an
  image.
- **ToolResult.Denied(DenialReason reason, string message, string? redirectToolName)**: Returns
  the refusal text. Throws `ArgumentOutOfRangeException` for an undefined reason,
  `ArgumentNullException` / `ArgumentException` for a missing or empty message, and
  `ArgumentException` for an invalid redirect tool name.
- **ToolName.Create(string family, string verb)**: Composes and validates `{family}_{verb}`.
  Throws `ArgumentNullException` / `ArgumentException` for a missing or invalid part.
- **ToolName.Validate(string name)**: Validates a tool name against the naming convention.
  Throws `ArgumentNullException` for a null name and `ArgumentException` for any violation.
- **GuardedToolFactory.Create(Delegate method, string name, string description,
  JsonSerializerOptions? serializerOptions)**: Creates a tool whose result is delivered to the
  runtime unchanged. Throws `ArgumentNullException` for a missing delegate or description, and
  `ArgumentException` for an empty description or an invalid name.

| Interface                   | Direction        | Format                        | Constraints                       |
|-----------------------------|------------------|-------------------------------|-----------------------------------|
| `new ToolLimits(...)`       | Inbound          | Constructor call              | Every ceiling zero or greater     |
| `ToolLimits.Default`        | Outbound         | `ToolLimits` property read    | None; always succeeds             |
| `ToolLimits.MaxReadBytes`   | Outbound         | `int` property read           | None; always succeeds             |
| `ToolResult.Text`           | Inbound/Outbound | Method call / `object` return | `text` non-null                   |
| `ToolResult.Binary`         | Inbound/Outbound | Method call / `object` return | `mediaType` non-null, non-empty   |
| `ToolResult.Image`          | Inbound/Outbound | Method call / `object` return | `mediaType` denotes an image      |
| `ToolResult.Denied`         | Inbound/Outbound | Method call / `object` return | Reason defined, message non-empty |
| `ToolName.Create`           | Inbound/Outbound | Method call / `string` return | Family and verb non-empty         |
| `ToolName.Validate`         | Inbound          | Method call                   | `name` satisfies the convention   |
| `GuardedToolFactory.Create` | Inbound/Outbound | Method call / `AIFunction`    | Delegate, name, description valid |

## Dependencies

The AgentKit Core has zero runtime NuGet dependencies — it is implemented exclusively
against the .NET Base Class Library. The following OTS items are used for building and verifying
this system (not consumed at runtime); see _OTS Integration Design_ (`docs/design/ots.md`) and
each item's dedicated design document for details:

- **BuildMark** — generates build-notes documentation; see _BuildMark Design_
- **FileAssert** — validates generated documents against acceptance criteria; see
  _FileAssert Design_
- **Pandoc** — converts Markdown documentation to HTML; see _Pandoc Design_
- **ReqStream** — enforces requirements-to-test traceability; see _ReqStream Design_
- **ReviewMark** — enforces file review coverage and currency; see _ReviewMark Design_
- **SarifMark** — converts CodeQL SARIF results to markdown; see _SarifMark Design_
- **SonarMark** — generates SonarCloud quality reports; see _SonarMark Design_
- **VersionMark** — captures and publishes tool-version information; see _VersionMark Design_
- **WeasyPrint** — converts HTML documentation to PDF; see _WeasyPrint Design_
- **xUnit** — executes unit and integration tests; see _xUnit Design_

## Risk Control Measures

Path containment is a risk control measure (IEC 62304 §5.3.3). An agent acts on instructions
derived from model output, which may be influenced by content the operator did not author, so the
file locations an agent can reach must be bounded by the host rather than by the agent's own
restraint.

The measure is segregated into three units whose responsibilities do not overlap:

- **RealPathResolver** establishes _where a path actually leads_, resolving symbolic links and
  directory junctions at every path component. Isolating this makes the one algorithm whose
  correctness the whole control depends on separately reviewable and separately testable.
- **PathRule** establishes _what a location grants_, with read access and write access expressed
  as independent rules so that neither can silently widen the other.
- **PathPolicy** makes _the single decision_, and both direct access and directory enumeration
  are routed through it so that a listing can never advertise a file that access would refuse.

Two further properties are part of the control: a refused access is reported as a returned
denial rather than an exception, so a refusal cannot terminate an agent's turn; and a policy
cannot be constructed without both of its rules, so an unguarded policy is unrepresentable.

Two properties of the tool contract are risk control measures in their own right. **Result
passthrough** ensures a tool's output reaches the provider in the form the tool produced it: when
it does not, a returned image is flattened into JSON, the provider never recognizes an
attachment, and the model states that it can see an image and then fabricates a description of
it — a silent failure producing confidently wrong output. **Family-prefixed naming** ensures an
application combining this library with the Agent Framework cannot present the model with two
identically named tools, a situation in which which tool is invoked is undefined. Both are
enforced at the single supported construction path, `GuardedToolFactory`, so a tool author cannot
omit either; see _GuardedToolFactory Unit Design_. Resource ceilings carried with the access
policy are a liveness control rather than a safety control, and are described in _ToolLimits Unit
Design_.

The `Demo` unit carries no risk control responsibility.

## Data Flow

**Construction path:**

1. **Input**: Constructor parameter `prefix` (optional; defaults to `"Hello"` when using `Demo()`)
2. **Validation**: `Demo(string prefix)` rejects null with `ArgumentNullException`; rejects empty
   string with `ArgumentException`
3. **Storage**: Valid prefix stored for use in subsequent greeting calls

**Method-call path:**

1. **Input**: Method parameter `name` (required, non-empty string)
2. **Validation**: `DemoMethod` rejects null with `ArgumentNullException`; rejects empty string
   with `ArgumentException`
3. **Processing**: Simple string formatting combining stored prefix and supplied name
4. **Output**: Formatted greeting string in the format `{prefix}, {name}!`

**Path containment path:**

1. **Input**: A read rule and a write rule at policy construction, then a requested path per
   access
2. **Resolution**: The requested path is made absolute and every path component is examined, so
   that symbolic links and directory junctions are replaced by their real targets
3. **Decision**: The real location is offered to exactly one rule — the read rule for a read, the
   write rule for a write — which applies its denied patterns first and then its location
   constraint
4. **Output**: Either the real location the caller may act on, or a denial carrying a fixed
   reason that contains no host detail
5. **Enumeration**: A directory listing routes the directory and every candidate file through the
   same read decision, so the listing and direct access always agree

**Guarded tool construction and result path:**

1. **Input**: A tool name composed from a family and a verb, a description, and a delegate
   implementing the tool
2. **Validation**: The name is checked against the naming convention — lowercase characters, a
   family prefix, no reserved collision — and the description is required to be non-empty
3. **Construction**: The tool is created through the single supported path, which supplies the
   result-delivery guard internally so it cannot be omitted or replaced
4. **Invocation**: The runtime calls the tool, which consults the access policy and its resource
   ceilings and produces either text, binary content, a caption accompanied by an image, or a
   refusal naming its reason
5. **Output**: The guard delivers that value to the runtime unchanged rather than serializing it
   to JSON, so content remains recognizable to the provider and a refusal remains readable text
   the model can act on

## Design Constraints

- **Simplicity**: Minimal functionality to serve as template
- **Compliance**: All functionality must be traceable to requirements
- **Quality**: Zero warnings, full test coverage, complete documentation
- **Portability**: Compatible across supported .NET platforms

### Platform Support

The library targets the following frameworks, enabling broad compatibility across modern .NET
runtimes:

| Target Framework   | Runtime / Environment                             |
|--------------------|---------------------------------------------------|
| `net8.0`           | .NET 8 LTS                                        |
| `net9.0`           | .NET 9                                            |
| `net10.0`          | .NET 10                                           |

The library is supported on the following operating systems:

- **Windows** — primary developer and CI platform
- **Linux** — CI/CD and containerized environments
- **macOS** — developer workstations using Apple platforms

Portability is achieved by restricting the implementation exclusively to Base Class Library (BCL)
APIs available across all target frameworks. No platform-specific native interop, OS-specific
APIs, or framework-version-specific features are used.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns
