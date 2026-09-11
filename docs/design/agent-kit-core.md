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

There are no subsystems. The `Demo` unit is a self-contained leaf class exposed directly through
the public API; see _Demo Unit Design_. The three path-safety units form one collaboration:
`PathPolicy` resolves a requested path through `RealPathResolver` and then consults exactly one
`PathRule`, which itself resolved its confined location through `RealPathResolver` when it was
created. `RealPathResolver` depends on nothing within the system, so the collaboration is
acyclic.

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
- **PathPolicy(PathRule readRule, PathRule writeRule)**: The only constructor. Throws
  `ArgumentNullException` when either rule is null.
- **PathPolicy.ReadRule**, **PathPolicy.WriteRule**: Read-only properties exposing the two rules.
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
| `PathPolicy.TryResolveRead`  | Inbound/Outbound | Method call / `bool` and `out` | `path` non-null, non-empty    |
| `PathPolicy.TryResolveWrite` | Inbound/Outbound | Method call / `bool` and `out` | `path` non-null, non-empty    |
| `PathPolicy.EnumerateFiles`  | Inbound/Outbound | Method call / `IEnumerable`    | Both args non-null, non-empty |

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
