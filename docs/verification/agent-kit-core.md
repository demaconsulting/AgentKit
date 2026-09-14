# System Verification Design

This document describes the system-level verification strategy for the AgentKit Core.

## Verification Approach

The AgentKit Core system is verified through system-level integration tests that
exercise the library as a whole from the perspective of a consumer. Tests instantiate the library
using its public API and assert on observable outputs, without relying on knowledge of internal
implementation details. No mocking or stubbing is required at the system level — the entire
integrated system is exercised as it would be used by a real caller.

The system under verification is the contract Core publishes: the policy primitives that bound
where a tool may act, the guarded path by which a tool is constructed, the results a tool returns
through, and the pack contract by which an application composes tools. Each scenario below
therefore exercises one of those properties end to end rather than any single unit in isolation;
unit-level behavior is verified separately in the unit verification documents.

System tests reside in `AgentKitCoreTests.cs` within the
`DemaConsulting.AgentKit.Core.Tests` project.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: No external services, databases, or network access required. The image
  promotion scenario uses a scripted `IChatClient` that contacts nothing; see
  _ImagePromotingChatClient Unit Verification Design_ for why a real provider would not observe
  the behavior under test
- **File system**: The path-containment scenarios require a writable temporary directory
- **Isolation**: Each test method constructs its own policy, tool, or temporary directory tree;
  no state is shared between tests

## External Interface Simulation

The path-containment scenarios touch one external interface — the host file system — and it is
deliberately **not** simulated. The decision under verification is made about real paths and the
listing it filters comes from the real operating system, so a simulated file system would verify
the simulation rather than the control. Each scenario instead creates a disposable temporary tree
and removes it afterwards.

## System-Level Test Scenarios

### Path Containment: A Relative Escape Is Denied

**Test**: `AgentKitCore_SystemPathContainment_RelativeEscape_IsDenied`

Verifies that the system judges access by the normalized location a path denotes. Configures a
policy confined to one location and requests a file in a sibling directory by climbing out of the
permitted location with a parent segment. Asserts the request is refused, that no location is
handed back, and that a reason is supplied.

### Path Containment: Enumeration Lists Only Permitted Files

**Test**: `AgentKitCore_SystemPathContainment_Enumeration_ListsOnlyPermittedFiles`

Verifies that the system applies the same containment decision to listing as to direct access.
Places one file inside the permitted location and one outside it, and lists the permitted
location through the public API. Asserts the contained file appears and the outside file does
not.

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

### Path Policy: A Denial Message Discloses the Permitted Locations

**Test**: `AgentKitCore_SystemPathPolicy_DenialMessage_DisclosesPermittedLocations`

Verifies that a denial tells a confined model the truthful map of where it may work. Requests a
path outside the permitted location and asserts the message echoes the requested path, names the
permitted location, and marks its access level — the host-path-disclosure rule the earlier
redaction requirement enforced having been deliberately dropped.

### Path Policy: A Relative Path From a Model Resolves Against the Workspace

**Test**: `AgentKitCore_SystemPathPolicy_RelativePathFromModel_ResolvesAgainstWorkspaceRoot`

Verifies that the system reads a path the way a model writes one. Configures a policy for one
workspace holding a file, requests that file by name alone, and asserts the request is permitted
and the returned location reads back the file's content. This is the system-level regression for
a defect in which such a request was refused because the name was measured from the location the
host process happened to be running from.

### Path Policy: A Denial Message States How to Recover

**Test**: `AgentKitCore_SystemPathPolicy_DenialMessage_StatesHowToRecover`

Verifies that a relative request escaping the working directory is refused with a denial an agent
can act on. Asserts the message echoes the caller's input verbatim, states how the relative path
was interpreted against the working directory, and names the permitted location — the three
ordered parts of a disclosing denial.

### Image Delivery: A Tool-Returned Image Reaches the Provider on a User Message

**Test**: `AgentKitCore_SystemImagePromotion_ToolImageResult_ReachesTheProviderOnAUserMessage`

Verifies provider-independent image delivery end to end. Builds the caption-then-image tool result
a guarded tool produces, sends the conversation through the decorator as a function-invocation
loop would, and asserts the client behind it received the image on a following user message, as
the same content instance. Confirms a host can make image delivery independent of whether its
provider carries images out of tool results.

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
nothing. The guard is selective rather than a blanket passthrough — text and content are
preserved while structured data is serialized — and the unit-level scenarios that pin both edges
of that selection are described in _GuardedToolFactory Unit Verification Design_.

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

### Tool Packs: A Host Without a Capability Is Offered No Tools From the Dependent Pack

**Test**: `AgentKitCore_SystemToolPacks_HostWithoutCapability_PackContributesNoTools`

Verifies capability-gated registration end to end. Composes a real access policy, a pack that
requires nothing and a pack that requires vision, and builds for a host that declares nothing.
Asserts the composed list contains only the undemanding pack's tool **and** that the vision pack
was never asked to create its tools — the assertion that distinguishes "not registered" from
"registered then filtered", and the reason the model can never see a tool it cannot use.

### Tool Packs: A Capable Host Receives Every Attached Tool

**Test**: `AgentKitCore_SystemToolPacks_CapableHost_ReceivesEveryAttachedTool`

Verifies that gating withholds nothing a host can support. Declares vision, attaches both packs,
and asserts the exact composed sequence — pack-add order, then each pack's own order — and that
the policy the application constructed is the one the pack was handed.

### Tool Packs: Colliding Family Prefixes Are Rejected

**Test**: `AgentKitCore_SystemToolPacks_CollidingFamilyPrefixes_AreRejected`

Verifies at the system boundary that an application cannot attach two packs claiming one family
prefix, a situation in which which of two identically prefixed tools a model invokes is undefined.
Asserts the refusal occurs where the application composed its packs rather than in a model's
behavior later.

## Acceptance Criteria

A system-level test run passes when all seventeen scenarios above pass without error or exception
beyond those explicitly asserted. Any unexpected exception, wrong exception type, wrong return
value, permitted path that should have been refused, relative path resolved against the process
working directory, escaped file appearing in a listing, denial message containing a host location
or offering no way forward, tool
result arriving as serialized JSON rather than as the content the tool produced, tool-returned
image failing to reach the provider, or tool offered
to a host that cannot support it constitutes a failure.
