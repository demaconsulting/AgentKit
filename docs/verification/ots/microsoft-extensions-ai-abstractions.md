## Microsoft.Extensions.AI.Abstractions Verification

This document provides the verification evidence for the `Microsoft.Extensions.AI.Abstractions`
OTS software item.

### Required Functionality

`Microsoft.Extensions.AI.Abstractions` supplies the provider-neutral currency AgentKit tools are
built from: `AIFunction` and `AIFunctionFactory.Create` for expressing a tool, the
`AIFunctionFactoryOptions.MarshalResult` hook through which `GuardedToolFactory` installs its
result-delivery guard, and the `AIContent` type hierarchy (`AIContent`, `TextContent`,
`DataContent`) that carries binary and image results to the provider in a typed form rather than
as serialized JSON.

### Verification Approach

Unlike the build-and-verify pipeline tools, this OTS item is a runtime library with no
self-validation CLI. Per `software-items.md`, an OTS library is verified through integration
tests proving the required functionality works. AgentKit's own tests exercise the abstraction
end to end: `GuardedToolFactory` builds real `AIFunction` instances through
`AIFunctionFactory.Create`, installs its guard through `AIFunctionFactoryOptions.MarshalResult`,
and each tool is invoked as the runtime would invoke it. The observed results — a carried name, a
typed `DataContent`, a caption-then-image `AIContent` list, a plain-string refusal, and the
un-guarded flattening the guard exists to prevent — prove the library provides the required
functionality. These tests run on every platform with no platform filter, so a single-OS run
satisfies them.

### Test Scenarios

#### GuardedToolFactory_Create_ValidName_IsCarriedByTheCreatedTool

**Scenario**: A tool is built from a lambda through the factory with an explicit name and invoked.

**Expected**: The `AIFunction` the factory produces carries the supplied name rather than a
compiler-generated identifier.

**Requirement coverage**: `AgentKit-OTS-MicrosoftExtensionsAIAbstractions-FunctionFactory`.

#### GuardedToolFactory_Create_DelegateDeclaredReturningObject_BinaryResultIsNotJsonSerialized

**Scenario**: A guarded tool returning binary content is invoked as the runtime would.

**Expected**: The result arrives as a typed `DataContent`, not as a serialized `JsonElement`.

**Requirement coverage**: `AgentKit-OTS-MicrosoftExtensionsAIAbstractions-FunctionFactory`,
`AgentKit-OTS-MicrosoftExtensionsAIAbstractions-Content`.

#### GuardedToolFactory_Create_DelegateDeclaredReturningObject_CaptionedImageSurvivesAsContentList

**Scenario**: A guarded tool returning a captioned image is invoked as the runtime would.

**Expected**: The result is an `AIContent` list whose two parts are a `TextContent` caption
followed by a `DataContent` image, in that order.

**Requirement coverage**: `AgentKit-OTS-MicrosoftExtensionsAIAbstractions-Content`.

#### GuardedToolFactory_Create_DelegateDeclaredReturningObject_DenialSurvivesAsString

**Scenario**: A guarded tool that refuses the request is invoked as the runtime would.

**Expected**: The refusal reaches the runtime as a plain `string` carrying the denial reason,
through the `MarshalResult` guard, rather than as a JSON wrapping.

**Requirement coverage**: `AgentKit-OTS-MicrosoftExtensionsAIAbstractions-MarshalResult`.

#### AIFunctionFactory_UnguardedObjectReturn_FlattensDataContentToJsonElement

**Scenario**: The same object-declared tool is built through `AIFunctionFactory.Create`
**without** the `MarshalResult` guard and invoked.

**Expected**: The content is flattened into a `JsonElement`, characterizing the third-party
behavior the `MarshalResult` guard is installed to override.

**Requirement coverage**: `AgentKit-OTS-MicrosoftExtensionsAIAbstractions-MarshalResult`.

### Requirements Coverage

- **`AgentKit-OTS-MicrosoftExtensionsAIAbstractions-FunctionFactory`**:
  GuardedToolFactory_Create_ValidName_IsCarriedByTheCreatedTool,
  GuardedToolFactory_Create_DelegateDeclaredReturningObject_BinaryResultIsNotJsonSerialized
- **`AgentKit-OTS-MicrosoftExtensionsAIAbstractions-Content`**:
  GuardedToolFactory_Create_DelegateDeclaredReturningObject_CaptionedImageSurvivesAsContentList,
  GuardedToolFactory_Create_DelegateDeclaredReturningObject_BinaryResultIsNotJsonSerialized
- **`AgentKit-OTS-MicrosoftExtensionsAIAbstractions-MarshalResult`**:
  GuardedToolFactory_Create_DelegateDeclaredReturningObject_DenialSurvivesAsString,
  AIFunctionFactory_UnguardedObjectReturn_FlattensDataContentToJsonElement
