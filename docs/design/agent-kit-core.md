# System Design

This document provides the system-level design for the AgentKit Core.

![AgentKit Core Structure](AgentKitCoreView.svg)

## Architecture

The AgentKit Core is the small, stable contract package on which every other AgentKit package
depends. It supplies the policy primitives that bound where a tool may act, the single guarded
path by which a tool is constructed, the result constructors a tool returns through, and the pack
contract by which a package publishes its tools to an application. It provides no agent, no
tool-calling loop and no provider abstraction; those belong to the agent framework an application
chooses. Core is deliberately slow-moving, because every other package inherits its churn.

The system consists of:

- **RealPathResolver Unit**: Reports the real file system location of a path, following
  symbolic links and directory junctions at every path component
- **PathRule Unit**: One access rule — unrestricted or confined to a location — carrying its own
  denied patterns
- **PathPolicy Unit**: Pairs an independent read rule and write rule, carries the workspace
  location relative paths are interpreted against, and provides the single containment decision
  used by both direct access and directory enumeration
- **ToolLimits Unit**: Carries the ceilings a tool observes when reading, returning and
  attaching content
- **ToolResult Unit**: Constructs the results a guarded tool returns to the model, and defines
  the reasons a tool may refuse an operation
- **ToolName Unit**: Defines the tool naming convention and validates names against it
- **GuardedToolFactory Unit**: The only supported way to construct a tool, applying the
  result-delivery guard and the naming rules to every tool it creates
- **ToolPack Unit**: Defines the contract a package implements to publish its tools as one
  capability-gated family, and the set of host capabilities a pack may require
- **ToolPackBuilder Unit**: Composes tool packs into the tool list an application offers a model,
  registering a pack only when the host provides every capability the pack requires
- **ImagePromotingChatClient Unit**: Makes an image a tool returned visible to a provider whose
  tool-result channel cannot carry one, by promoting it onto a following user message

There are no subsystems. The three path-safety units form one collaboration:
`PathPolicy` makes a relative request absolute against the workspace location it carries, resolves
the result through `RealPathResolver`, and then consults exactly one
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

The two pack-contract units form a third collaboration, which is where the first two meet an
application. A package author implements `IToolPack` to publish its tools as one family; an
application constructs a `ToolPackBuilder` with its `PathPolicy`, declares what its host supports,
and adds a pack per capability it wishes to attach. The builder registers a pack only when the
host provides every capability the pack declared, hands the registered packs the one policy it
holds, and verifies that each tool a pack returns carries the family prefix that pack claimed.
`IToolPack` depends only on `PathPolicy`, and `ToolPackBuilder` depends only on `IToolPack` and
`PathPolicy`, so this collaboration is acyclic too.

`ImagePromotingChatClient` stands apart from all three. It depends on nothing within the system —
only on the chat client abstraction the OTS dependency supplies — and nothing within the system
depends on it. It exists because delivering an image to a model is not finished when a tool
returns one: one provider carries an image out of a tool result to the model, while another
preserves the same content through the framework and discards it at the wire, after which the
model describes a picture it never received. A host that has chosen such a provider wraps its
client in this decorator, beneath the function-invocation loop, and the image is carried onto a
user message instead. It is opt-in rather than automatic because a host whose provider already
delivers images gains nothing from it, and because Core provides no agent loop of its own into
which it could be installed.

## External Interfaces

The system exposes the following public API to external consumers.

The path-safety API:

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
- **PathPolicy(PathRule readRule, PathRule writeRule, ToolLimits limits, string? baseDirectory)**:
  Constructs a policy with explicit resource ceilings and, optionally, the location relative
  requests are interpreted against. When no base is given it defaults to the read rule's location,
  failing that the write rule's, failing that the process working directory. Throws
  `ArgumentNullException` when any of the first three arguments is null, and `ArgumentException`
  for an invalid base.
- **PathPolicy.ForWorkspace(string root)** and **PathPolicy.ForWorkspace(string root, ToolLimits
  limits)**: Creates a policy whose read rule, write rule and base are all `root`. Throws
  `ArgumentNullException` / `ArgumentException` for a missing or empty workspace.
- **PathPolicy.ReadRule**, **PathPolicy.WriteRule**: Read-only properties exposing the two rules.
- **PathPolicy.Limits**: Read-only property exposing the ceilings every governed tool observes.
- **PathPolicy.BaseDirectory**: Read-only property exposing the real location relative requests
  are interpreted against.
- **PathPolicy.TryResolveRead / TryResolveWrite(string? path, out string? realPath, out string?
  denialMessage)**: Returns whether the access is permitted, with the real location on success
  and a redacted reason carrying recovery guidance on refusal. A relative path is interpreted
  against `BaseDirectory`; an omitted, empty, whitespace or placeholder path denotes
  `BaseDirectory` itself.
- **PathPolicy.EnumerateFiles(string? directory, string searchPattern)**: Returns the real
  locations of the permitted files beneath a directory, which is `BaseDirectory` when no
  directory is named.

| Interface                    | Direction        | Format                         | Constraints                   |
|------------------------------|------------------|--------------------------------|-------------------------------|
| `RealPathResolver.Resolve`   | Inbound/Outbound | Method call / `string` return  | `path` non-null, non-empty    |
| `PathRule.Unrestricted`      | Inbound/Outbound | Factory call / `PathRule`      | Patterns non-null, non-empty  |
| `PathRule.Rooted`            | Inbound/Outbound | Factory call / `PathRule`      | `root`, patterns non-empty    |
| `PathRule.Root`              | Outbound         | `string?` property read        | None; always succeeds         |
| `PathRule.DenyPatterns`      | Outbound         | `IReadOnlyList<string>` read   | None; always succeeds         |
| `PathRule.Allows`            | Inbound/Outbound | Method call / `bool` return    | Resolved, non-null, non-empty |
| `new PathPolicy(...)`        | Inbound          | Constructor call               | Both rules non-null           |
| `PathPolicy.ForWorkspace`    | Inbound/Outbound | Factory call / `PathPolicy`    | `root` non-null, non-empty    |
| `PathPolicy.ReadRule`        | Outbound         | `PathRule` property read       | None; always succeeds         |
| `PathPolicy.WriteRule`       | Outbound         | `PathRule` property read       | None; always succeeds         |
| `PathPolicy.Limits`          | Outbound         | `ToolLimits` property read     | None; always succeeds         |
| `PathPolicy.BaseDirectory`   | Outbound         | `string` property read         | None; always succeeds         |
| `PathPolicy.TryResolveRead`  | Inbound/Outbound | Method call / `bool` and `out` | None; any path is answered    |
| `PathPolicy.TryResolveWrite` | Inbound/Outbound | Method call / `bool` and `out` | None; any path is answered    |
| `PathPolicy.EnumerateFiles`  | Inbound/Outbound | Method call / `IEnumerable`    | Pattern non-null, non-empty   |

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
- **ToolResult.Structured(object value)**: Returns the supplied value, which the guarded
  construction path serializes to JSON on the way to the runtime. Throws
  `ArgumentNullException` for a null value.
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
| `ToolResult.Structured`     | Inbound/Outbound | Method call / `object` return | `value` non-null                  |
| `ToolResult.Binary`         | Inbound/Outbound | Method call / `object` return | `mediaType` non-null, non-empty   |
| `ToolResult.Image`          | Inbound/Outbound | Method call / `object` return | `mediaType` denotes an image      |
| `ToolResult.Denied`         | Inbound/Outbound | Method call / `object` return | Reason defined, message non-empty |
| `ToolName.Create`           | Inbound/Outbound | Method call / `string` return | Family and verb non-empty         |
| `ToolName.Validate`         | Inbound          | Method call                   | `name` satisfies the convention   |
| `GuardedToolFactory.Create` | Inbound/Outbound | Method call / `AIFunction`    | Delegate, name, description valid |

The system additionally exposes the pack composition API:

- **IToolPack.FamilyPrefix**: Read-only property exposing the family prefix every tool in the pack
  carries. Must be non-null and non-empty.
- **IToolPack.RequiredCapabilities**: Read-only property exposing the capabilities the host must
  provide for the pack's tools to operate. `HostCapabilities.None` means the pack is always
  registered.
- **IToolPack.CreateTools(PathPolicy policy)**: Creates the pack's tools, governed by the supplied
  policy. Must return a non-null collection containing no null element, every tool of which
  carries the declared family prefix. Not called at all when the host does not provide the
  required capabilities.
- **HostCapabilities**: Flags enumeration naming what a host can provide — `None` and `Vision`.
- **new ToolPackBuilder(PathPolicy policy)**: Creates a composition governed by `policy`. Throws
  `ArgumentNullException` when the policy is null.
- **ToolPackBuilder.WithHostCapabilities(HostCapabilities capabilities)**: Declares what the host
  provides, replacing any earlier declaration, and returns the builder.
- **ToolPackBuilder.Add(IToolPack pack)**: Adds a pack and returns the builder. Throws
  `ArgumentNullException` for a missing pack, and `ArgumentException` for an empty family prefix or
  a prefix another added pack already claims.
- **ToolPackBuilder.Build()**: Returns the tools of every pack the host can support, in pack-add
  order. Throws `InvalidOperationException` when a pack returns no collection, a null tool, or a
  tool outside its declared family.

| Interface                              | Direction        | Format                        | Constraints           |
|----------------------------------------|------------------|-------------------------------|-----------------------|
| `IToolPack.FamilyPrefix`               | Outbound         | `string` property read        | Non-null, non-empty   |
| `IToolPack.RequiredCapabilities`       | Outbound         | `HostCapabilities` read       | None; always succeeds |
| `IToolPack.CreateTools`                | Inbound/Outbound | Method call / `IEnumerable`   | No null element       |
| `new ToolPackBuilder(...)`             | Inbound          | Constructor call              | `policy` non-null     |
| `ToolPackBuilder.WithHostCapabilities` | Inbound/Outbound | Method call / builder         | None; always succeeds |
| `ToolPackBuilder.Add`                  | Inbound/Outbound | Method call / builder         | Prefix unclaimed      |
| `ToolPackBuilder.Build`                | Inbound/Outbound | Method call / `IReadOnlyList` | Packs honor contracts |

The system additionally exposes one provider-compatibility API:

- **new ImagePromotingChatClient(IChatClient innerClient)**: Wraps a chat client so that an image
  a tool returned is promoted onto a user message before the request is forwarded. Throws
  `ArgumentNullException` when the inner client is null. Both the whole-response and the
  streaming member rewrite the conversation identically and are otherwise pass-through.

| Interface                           | Direction        | Format                 | Constraints       |
|-------------------------------------|------------------|------------------------|-------------------|
| `new ImagePromotingChatClient(...)` | Inbound          | Constructor call       | Client non-null   |
| `GetResponseAsync`                  | Inbound/Outbound | Method call / `Task`   | Messages non-null |
| `GetStreamingResponseAsync`         | Inbound/Outbound | Method call / sequence | Messages non-null |

## Dependencies

The AgentKit Core takes exactly one runtime NuGet dependency,
`Microsoft.Extensions.AI.Abstractions`. It exists because a tool is an `AIFunction`, and
`AIFunction` is the common currency across the GitHub Copilot SDK, the Microsoft Agent Framework
and any `Microsoft.Extensions.AI` `IChatClient`. Depending on the abstractions package — rather
than on any provider, runtime or agent loop — is what lets one guarded tool be offered to all of
them without Core choosing a provider on the application's behalf. Nothing further is taken:
there is no provider package, no agent framework package and no transitive runtime. As a
third-party published library, `Microsoft.Extensions.AI.Abstractions` is an OTS item; its
integration is recorded in _OTS Integration Design_ (`docs/design/ots.md`) and its dedicated
_Microsoft.Extensions.AI.Abstractions Design_.

The following OTS items are used for building and verifying this system and are not consumed at
runtime; see _OTS Integration Design_ (`docs/design/ots.md`) and each item's dedicated design
document for details:

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
  are routed through it so that a listing can never advertise a file that access would refuse. It
  also holds _the location a relative request means_, so that a bare file name a model states is
  made absolute against the workspace before the link resolution and the containment test are
  applied. Placing the base on the policy rather than on a rule is what keeps reads and writes
  interpreting one name the same way when one direction is unrestricted and the other confined.

Three further properties are part of the control: a refused access is reported as a returned
denial rather than an exception, so a refusal cannot terminate an agent's turn; that denial
states the form a permitted request takes, without naming any host location, so a refusal is a
step the agent can recover from rather than a dead end it retries; and a policy
cannot be constructed without both of its rules, so an unguarded policy is unrepresentable.

Two properties of the tool contract are risk control measures in their own right. **Selective
result marshalling** ensures a tool's output reaches the provider in the form the provider can
read: text, content and sequences of content are delivered as the tool produced them, because
when they are not, a returned image is flattened into JSON, the provider never recognizes an
attachment, and the model states that it can see an image and then fabricates a description of
it — a silent failure producing confidently wrong output. Anything else is serialized as the
underlying factory would have serialized it, because preserving every result indiscriminately was
tried and produced the opposite failure: a tool returning structured data reached the provider as
a raw object it could not read. **Family-prefixed naming** ensures an
application combining this library with the Agent Framework cannot present the model with two
identically named tools, a situation in which which tool is invoked is undefined. Both are
enforced at the single supported construction path, `GuardedToolFactory`, so a tool author cannot
omit either; see _GuardedToolFactory Unit Design_. Resource ceilings carried with the access
policy are a liveness control rather than a safety control, and are described in _ToolLimits Unit
Design_.

**Capability-gated registration** is a risk control measure of the same kind. Where a host cannot
support a tool, the tool is not offered at all rather than offered and refused: a model that can
see a tool it cannot use will spend a turn on it, and a model told only that a tool refused will
reason around the refusal rather than abandon the approach. Because an unsupported pack is never
asked to create its tools, accidental registration is unrepresentable rather than merely unlikely;
see _ToolPackBuilder Unit Design_. Family prefix ownership is part of the same measure: a pack
claims a prefix no other pack claims, and every tool it publishes must carry that prefix, so an
application cannot present the model with two tools it cannot tell apart.

## Data Flow

**Path containment path:**

1. **Input**: A read rule, a write rule and a workspace location at policy construction, then a
   requested path per access
2. **Interpretation**: An omitted, empty, whitespace or placeholder request is read as denoting
   the workspace itself, and a relative request is made absolute against the workspace — never
   against the location the host process happens to be running from
3. **Resolution**: Every component of the now-absolute path is examined, so that symbolic links
   and directory junctions are replaced by their real targets. This happens after step 2 so that
   a relative escape and an absolute one reach the same decision
4. **Decision**: The real location is offered to exactly one rule — the read rule for a read, the
   write rule for a write — which applies its denied patterns first and then its location
   constraint
5. **Output**: Either the real location the caller may act on, or a denial carrying a fixed
   reason and a fixed statement of the form a permitted request takes, neither of which contains
   any host detail
6. **Enumeration**: A directory listing routes the directory and every candidate file through the
   same read decision, so the listing and direct access always agree

**Guarded tool construction and result path:**

1. **Input**: A tool name composed from a family and a verb, a description, and a delegate
   implementing the tool
2. **Validation**: The name is checked against the naming convention — lowercase characters, a
   family prefix, no reserved collision — and the description is required to be non-empty
3. **Construction**: The tool is created through the single supported path, which supplies the
   result-delivery guard internally so it cannot be omitted or replaced
4. **Invocation**: The runtime calls the tool, which consults the access policy and its resource
   ceilings and produces either text, binary content, a caption accompanied by an image,
   structured data, or a refusal naming its reason
5. **Output**: The guard delivers text and content to the runtime unchanged, so content remains
   recognizable to the provider and a refusal remains readable text the model can act on, and
   serializes anything else to JSON so that structured data reaches the model in a form it can
   read

**Provider-independent image delivery path:**

1. **Input**: The conversation a function-invocation loop has assembled, with tool results
   already appended to it
2. **Inspection**: Each tool message is examined for a function result carrying image content,
   in either of the two shapes a guarded tool produces
3. **Promotion**: A user message holding a fixed announcement and the same content instances is
   inserted immediately after each tool result that carried an image; a text-only result adds
   nothing
4. **Output**: The rewritten conversation is forwarded to the wrapped client, so a provider that
   accepts images on messages but not in tool responses still receives the image

**Pack composition path:**

1. **Input**: An access policy at builder construction, a declaration of what the host supports,
   and one pack per capability the application wishes to attach
2. **Registration check**: Each pack is registered only when every capability it requires is one
   the host declared; a pack requiring none is registered by every host
3. **Creation**: Only a registered pack is asked for its tools, and it is handed the one policy
   the builder holds — an unsupported pack's tools are never built at all
4. **Agreement check**: Every tool a pack returns must carry the family prefix that pack declared,
   so the prefix no other pack may claim is a promise that is verified rather than trusted
5. **Output**: One ordered tool list — pack-add order, then the order each pack produced its tools
   — which the application hands to the agent runtime

## Design Constraints

- **Minimal contract**: The smallest public surface that packs must share, changed slowly,
  because every other AgentKit package depends on it and inherits its churn
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

Portability is achieved by restricting the implementation to Base Class Library (BCL) APIs
available across all target frameworks and to the provider-neutral
`Microsoft.Extensions.AI.Abstractions` surface. No platform-specific native interop, OS-specific
APIs, or framework-version-specific features are used.

### Integration Patterns

- **NuGet Packaging**: Standard .NET library packaging and distribution
- **CI/CD Integration**: Automated build, test, and quality validation
- **Requirements Traceability**: All features linked to passing tests
- **Review Management**: Systematic file review using ReviewMark patterns
- **Providers That Drop Images From Tool Results**: Providers differ in where they accept image
  content, and the difference is silent. One carries an image out of a tool result to the model;
  another accepts images on messages but not in tool responses, preserves the content through the
  framework, and discards it at the wire — after which the model describes a picture it never
  received and reports no error. A control exchange placing the same bytes on a user message was
  described correctly by the same model on the same provider, so the limitation is one of channel
  rather than capability. A host targeting such a provider wraps its chat client in
  `ImagePromotingChatClient`, **beneath** the function-invocation loop so that the decorator
  observes tool results after they have been appended; see _ImagePromotingChatClient Unit Design_.
  A host whose provider already delivers images from tool results installs nothing.
