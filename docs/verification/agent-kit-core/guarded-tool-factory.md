## GuardedToolFactory Unit Verification Design

This document describes the unit-level verification strategy for the `GuardedToolFactory` class.

### Verification Approach

`GuardedToolFactory` is verified through unit tests that create real tools and **invoke them
through the runtime's own entry point**, then inspect the concrete runtime type of what comes
back. `ToolName`, `ToolResult` and the underlying function factory are all used as real
dependencies rather than substitutes: the behavior under verification is exactly the third-party
factory's result handling, so substituting it would verify the substitute instead of the guard.

**The declared-return-type constraint governs how these scenarios must be written, and is not
negotiable.** The underlying factory decides whether to serialize a tool's result by the
delegate's *declared* return type, not by the runtime type of the value returned. Every
requirement-linked scenario therefore declares its tool delegate as `Task<object>` — or, for the
synchronous scenario, `object` — because that is the shape the factory would serialize and the
only shape in which these scenarios mean anything. **A delegate declared `Task<DataContent>`
passes without the guard and proves nothing.** The explicit `(Func<Task<object>>)` casts in the
test source are load-bearing rather than ceremony; removing them is a regression in the compliance
evidence, not a simplification.

The passthrough scenario for binary content additionally carries a **negative** assertion — the
result is not a `JsonElement` — because that assertion names the failure being prevented rather
than merely describing the success.

**One scenario is deliberately not linked to a requirement.**
`AIFunctionFactory_UnguardedObjectReturn_FlattensDataContentToJsonElement` characterizes
third-party behavior in `Microsoft.Extensions.AI.Abstractions` rather than behavior this library
promises: it builds the same tool with **no** options and **no** guard, and asserts the result
*is* a `JsonElement` carrying a serialized `data:` URI. Leaving it unlinked means that if the
package later changes how an `object`-declared return is handled, only this clearly labeled
characterization scenario fails, without invalidating the compliance evidence for the guard
itself. Its name begins with the third-party type under characterization rather than with this
library's unit, for the same reason; it lives in this file because it is the evidence that
justifies the guard's existence. Tests without a corresponding requirement are explicitly
permitted by the testing standard.

Unit tests reside in `GuardedToolFactoryTests.cs` within the
`DemaConsulting.AgentKit.Core.Tests` project.

### Test Environment

- **Framework**: xUnit v3 running under the .NET SDK, using asynchronous test methods for the
  scenarios that invoke a tool
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline
- **Dependencies**: `Microsoft.Extensions.AI.Abstractions` 10.9.0, the version this library pins.
  The scenarios characterize that version's behavior, so a version change is expected to be
  reviewed against them
- **External services**: None; no provider is contacted and no network access is required
- **Mocking**: None
- **Isolation**: Each test creates its own tool; no state is shared

### Acceptance Criteria

A unit test run passes when all twelve scenarios below pass without error or exception beyond
those explicitly asserted. Any result arriving as a `JsonElement` from a guarded tool, any
invalid name or empty description accepted, and any name or description not carried by the
created tool constitutes a failure.

### Test Scenarios

#### AgentKitCore-GuardedToolFactory-ResultPassthrough: Binary Content Is Not Serialized

**Test**: `GuardedToolFactory_Create_DelegateDeclaredReturningObject_BinaryResultIsNotJsonSerialized`

The central scenario. Creates a tool whose delegate is declared `Task<object>` and returns binary
content, invokes it, and asserts the result is a `DataContent` **and specifically not** a
`JsonElement`. The negative assertion names the failure being prevented: without the guard the
provider never recognizes an attachment and the model fabricates a description of content it
never saw.

#### AgentKitCore-GuardedToolFactory-ResultPassthrough: A Captioned Image Survives as a Content List

**Test**: `GuardedToolFactory_Create_DelegateDeclaredReturningObject_CaptionedImageSurvivesAsContentList`

Asserts the result is a two-element content list with the caption first and the image second —
the exact shape `ToolResult.Image` produces, and the shape flattened into a JSON array without
the guard.

#### AgentKitCore-GuardedToolFactory-ResultPassthrough: A Refusal Survives as a String

**Test**: `GuardedToolFactory_Create_DelegateDeclaredReturningObject_DenialSurvivesAsString`

Pins the benign but observable consequence of the guard on the refusal path: the refusal arrives
as a raw `string` rather than as a `JsonElement` wrapping a JSON string. Both are consumable, so
nothing is broken — but the change is visible on every tool result and must not be read as a
defect.

#### AgentKitCore-GuardedToolFactory-GuardAlwaysApplied: A Synchronous Tool Is Guarded

**Test**: `GuardedToolFactory_Create_SynchronousDelegate_ResultIsStillPassedThrough`

Asserts the guard does not depend on the tool being asynchronous. The declared return type is
`object`, the synchronous counterpart of the trapped case.

#### AgentKitCore-GuardedToolFactory-GuardAlwaysApplied: A Parameterized Tool Is Guarded

**Test**: `GuardedToolFactory_Create_ParameterizedDelegate_ResultIsStillPassedThrough`

Invokes a tool with a bound argument and asserts both that the argument bound and that the result
was delivered unchanged, confirming the guard governs result delivery only and leaves parameter
binding and schema generation untouched.

#### AgentKitCore-GuardedToolFactory-NameValidated: An Unprefixed Name Is Refused

**Test**: `GuardedToolFactory_Create_UnprefixedName_ThrowsArgumentException`

Asserts the naming convention is applied before anything is constructed.

#### AgentKitCore-GuardedToolFactory-NameValidated: A Reserved Name Is Refused

**Test**: `GuardedToolFactory_Create_ReservedName_ThrowsArgumentException`

Asserts the bare Agent Framework file access names cannot be claimed through the factory, which
is what makes the collision impossible in practice.

#### AgentKitCore-GuardedToolFactory-NameValidated: The Created Tool Carries the Name

**Test**: `GuardedToolFactory_Create_ValidName_IsCarriedByTheCreatedTool`

Creates a tool from a lambda — which has no usable name of its own — and asserts the supplied
name reaches the model rather than a compiler-generated identifier.

#### AgentKitCore-GuardedToolFactory-DescriptionRequired: An Empty Description Is Refused

**Test**: `GuardedToolFactory_Create_EmptyDescription_ThrowsArgumentException`

Error path: a tool with no description leaves the model guessing what it does. The underlying factory
would default it to the empty string rather than failing, so the requirement must be enforced
here.

#### AgentKitCore-GuardedToolFactory-DescriptionRequired: The Created Tool Carries the Description

**Test**: `GuardedToolFactory_Create_Description_IsCarriedByTheCreatedTool`

Asserts the supplied description reaches the model.

#### AgentKitCore-GuardedToolFactory-RejectMissingDelegate: A Missing Implementation Is Refused

**Test**: `GuardedToolFactory_Create_NullDelegate_ThrowsArgumentNullException`

Error path: there is nothing to guard and nothing for the model to invoke.

#### Characterization (Deliberately Unlinked): An Unguarded Object Return Is Flattened

**Test**: `AIFunctionFactory_UnguardedObjectReturn_FlattensDataContentToJsonElement`

Builds the same tool **without** options and therefore without the guard, and asserts the result
*is* a `JsonElement` whose raw text carries the serialized content markers. This scenario is
intentionally linked to no requirement; see *Verification Approach* above for why. It is the
evidence that the guard is necessary rather than ceremonial, and it will be the first and only
scenario to fail if the underlying package changes this behavior.
