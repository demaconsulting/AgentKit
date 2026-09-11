# System Verification Design

This document describes the system-level verification strategy for the AgentKit Core.

## Verification Approach

The AgentKit Core system is verified through system-level integration tests that
exercise the library as a whole from the perspective of a consumer. Tests instantiate the library
using its public API and assert on observable outputs, without relying on knowledge of internal
implementation details. No mocking or stubbing is required at the system level — the entire
integrated system is exercised as it would be used by a real caller.

System tests reside in `AgentKitCoreTests.cs` within the
`DemaConsulting.AgentKit.Core.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required
- **File system**: The path-containment scenarios require a writable temporary directory and the
  ability to create a real reparse point within it — a directory junction created by
  `cmd.exe /c mklink /J` on Windows, and a directory symbolic link on Linux and macOS. A Windows
  symbolic link is deliberately not used, because it requires a privilege an unelevated developer
  session does not hold and would therefore pass on the elevated CI runner while failing on every
  workstation
- **Isolation**: Each test method constructs its own `Demo` instance or its own temporary
  directory tree; no shared state between tests

## External Interface Simulation

The greeting scenarios have no external interfaces requiring simulation; they call the public API
directly with controlled inputs and verify returned values and thrown exceptions.

The path-containment scenarios do touch one external interface — the host file system — and it is
deliberately **not** simulated. The behavior under verification is exactly the operating system's
own link resolution and directory enumeration, so a simulated file system would verify the
simulation rather than the control. Each scenario instead creates a disposable temporary tree
containing a genuine reparse point and removes it afterwards, deleting directory links before the
recursive delete because a recursive delete over a tree containing a junction fails.

## System-Level Test Scenarios

### Integration: Provides Expected Functionality

**Test**: `AgentKitCore_SystemIntegration_DefaultConstruction_ReturnsExpectedGreeting`

Exercises end-to-end system behavior: constructs a `Demo` instance using the default constructor
and calls `DemoMethod` with a valid name. Asserts that the system produces the expected greeting
string `"Hello, System!"`, confirming that all components integrate correctly under default
configuration.

### Customization: Handles Configuration Properly

**Test**: `AgentKitCore_SystemCustomization_CustomPrefix_ReturnsExpectedGreeting`

Verifies that the system correctly propagates a custom prefix supplied at construction time.
Constructs a `Demo` instance with prefix `"Welcome"`, calls `DemoMethod` with a valid name, and
asserts the result is `"Welcome, Integration!"`. Confirms that the configuration path through all
integrated components functions as expected.

### Validation: DemoMethod Null Input Throws ArgumentNullException

**Test**: `AgentKitCore_SystemValidation_DemoMethodNullInput_ThrowsArgumentNullException`

Verifies that the system rejects a `null` argument to `DemoMethod` with `ArgumentNullException`.
Constructs a `Demo` instance with the default constructor and passes `null` to `DemoMethod`.
Confirms that the system boundary enforces the null-rejection contract.

### Validation: DemoMethod Empty Input Throws ArgumentException

**Test**: `AgentKitCore_SystemValidation_DemoMethodEmptyInput_ThrowsArgumentException`

Verifies that the system rejects an empty-string argument to `DemoMethod` with `ArgumentException`.
Constructs a `Demo` instance with the default constructor and passes `string.Empty` to `DemoMethod`.
Confirms that the system boundary enforces the empty-string rejection contract.

### Validation: Constructor Null Prefix Throws ArgumentNullException

**Test**: `AgentKitCore_SystemValidation_ConstructorNullPrefix_ThrowsArgumentNullException`

Verifies that the system rejects a `null` prefix argument at construction time with
`ArgumentNullException`. Attempts to construct a `Demo` instance with `null` as the prefix.
Confirms that the system boundary prevents invalid configuration from being established.

### Validation: Constructor Empty Prefix Throws ArgumentException

**Test**: `AgentKitCore_SystemValidation_ConstructorEmptyPrefix_ThrowsArgumentException`

Verifies that the system rejects an empty-string prefix argument at construction time with
`ArgumentException`. Attempts to construct a `Demo` instance with `string.Empty` as the prefix.
Confirms that the system boundary prevents empty-string configuration from being established.

### Integration: Exposes Configured Prefix

**Test**: `AgentKitCore_SystemIntegration_CustomPrefix_ExposesPrefix`

Verifies that the `Prefix` property exposes the prefix supplied at construction time. Constructs
a `Demo` instance with a custom prefix and reads the `Prefix` property. Confirms the system's
public API correctly surfaces the configured prefix to callers.

### Path Containment: A File Beneath a Directory Link Is Denied

**Test**: `AgentKitCore_SystemPathContainment_FileBeneathDirectoryLink_IsDenied`

Verifies that the system judges access by the location a path actually reaches. Configures a
policy confined to one location, creates a genuine directory link inside it pointing at a sibling
directory, and requests a file through that link. Asserts the request is refused, that no location
is handed back, and that a reason is supplied.

### Path Containment: Enumeration Across a Link Excludes the Escaped File

**Test**: `AgentKitCore_SystemPathContainment_EnumerationAcrossLink_ExcludesEscapedFile`

Verifies that the system applies the same containment decision to listing as to direct access.
Places one file inside the permitted location and one outside it, links the two, and lists the
permitted location through the public API. Asserts the contained file appears and the escaped
file does not.

### Path Policy: Reading Widely While Writing Narrowly

**Test**: `AgentKitCore_SystemPathPolicy_ReadWideWriteNarrow_AllowsReadDeniesWrite`

Verifies that read access and write access are independent. Configures unrestricted reads with
writes confined to one location, then reads and attempts to write the same location outside it.
Asserts the read is permitted and the write is refused.

### Path Policy: A Denied Path Returns a Denial Without Throwing

**Test**: `AgentKitCore_SystemPathPolicy_DeniedPath_ReturnsDenialWithoutThrowing`

Verifies that a refusal reaches the caller as a return value carrying a reason, not as an
exception. Requests a location outside the permitted one and asserts the call returns a refusal
with no location and a non-empty reason.

### Path Policy: A Denial Message Contains No Host Paths

**Test**: `AgentKitCore_SystemPathPolicy_DenialMessage_ContainsNoHostPaths`

Verifies that nothing about the host's layout leaves the system in a denial message. Asserts the
message contains neither the requested path nor the permitted location, and contains no directory
separator at all.

### Path Policy: Construction Without Rules Is Rejected

**Test**: `AgentKitCore_SystemPathPolicy_ConstructionWithoutRules_IsRejected`

Verifies that the system refuses to create a path access policy with either rule missing,
confirming at the system boundary that an unguarded policy is unrepresentable.

### Tool Limits: The Access Policy Carries the Published Ceilings

**Test**: `AgentKitCore_SystemToolLimits_PolicyCarriesDefaultLimits_ExposesPublishedValues`

Verifies that the ceilings a tool observes reach it through the access policy a host actually
builds. Constructs a real policy from two rooted rules without configuring any ceilings, and
asserts it exposes the four published values. Confirms that a host which states no budget still
operates within a bounded one.

### Guarded Tool: An Image Result Reaches the Runtime as Content

**Test**: `AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent`

Verifies the whole tool contract end to end. Composes a name through `ToolName`, builds a tool
through the only supported construction path whose delegate is declared to return an object, and
invokes it through the runtime's own entry point. Asserts the result is a two-element content
list — caption then image — rather than serialized JSON. The declared return type is deliberate:
a strongly-typed declaration would pass without the result-delivery guard and would prove
nothing; see _GuardedToolFactory Unit Verification Design_.

### Guarded Tool: A Denied Path Returns a Refusal Rather Than Throwing

**Test**: `AgentKitCore_SystemGuardedTool_DeniedPath_ReturnsDenialResultNotException`

Verifies the access policy, the result constructors and the guarded factory acting together.
Builds a tool governed by a policy confined to one location, invokes it with a path outside that
location, and asserts the call completes and returns refusal text naming the reason. Confirms a
refusal is a recoverable step for an agent rather than the end of its turn.

### Tool Naming: A Bare File Access Name Is Rejected

**Test**: `AgentKitCore_SystemToolNaming_BareFileAccessName_IsRejected`

Verifies at the system boundary that the only supported construction path will not issue a name
that collides with the Agent Framework's bare file access tools, so an application combining both
libraries cannot offer the model two tools with the same name.

### Guarded Tool: A Constructed Tool Carries Its Validated Name and Description

**Test**: `AgentKitCore_SystemGuardedTool_ConstructedTool_CarriesValidatedNameAndDescription`

Verifies that a tool built the only supported way is selectable by a model rather than anonymous.
Asserts the created tool carries the composed name and the supplied description.

## Acceptance Criteria

A system-level test run passes when all eighteen scenarios above pass without error or exception
beyond those explicitly asserted. Any unexpected exception, wrong exception type, wrong return
value, permitted path that should have been refused, escaped file appearing in a listing, or tool
result arriving as serialized JSON rather than as the content the tool produced constitutes a
failure.
