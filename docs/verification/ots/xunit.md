## xUnit Verification

This document provides the verification evidence for the xUnit OTS software item. Requirements
for this OTS item are defined in the xUnit OTS Software Requirements document.

### Required Functionality

xUnit v3 (xunit.v3 and xunit.runner.visualstudio) is the unit-testing framework used by the
project. It discovers and runs all test methods and writes TRX result files that feed into coverage
reporting and requirements traceability. Passing tests confirm the framework is functioning
correctly.

### Verification Approach

xUnit is verified by self-validation evidence from the CI pipeline. Each scenario names a specific
test method that xUnit must discover, execute, and record in a TRX result file. A passing pipeline
run for all scenarios constitutes evidence that both requirements are satisfied.

### Test Scenarios

#### ToolName_Create_FamilyAndVerb_ProducesUnderscoreSeparatedName

**Scenario**: xUnit discovers and runs this synchronous [Fact] test; the test verifies that a tool
name composed from a family and a verb is separated by an underscore.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output.

**Requirement coverage**: `AgentKit-OTS-xUnit-Execute`, `AgentKit-OTS-xUnit-Report`.

#### ToolName_Validate_BareAgentFrameworkName_IsRejected

**Scenario**: xUnit discovers and runs this data-driven [Theory] test once per inline data case;
the test verifies that a bare Agent Framework tool name is rejected.

**Expected**: xUnit executes every data case, each passes, and each result appears in the TRX
output, confirming that discovery covers theories as well as facts.

**Requirement coverage**: `AgentKit-OTS-xUnit-Execute`, `AgentKit-OTS-xUnit-Report`.

#### AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent

**Scenario**: xUnit discovers and runs this asynchronous [Fact] test; the test verifies that an
image result reaches the runtime as content rather than as serialized JSON.

**Expected**: xUnit executes the asynchronous test to completion, the test passes, and the result
appears in the TRX output, confirming that discovery and execution cover asynchronous tests.

**Requirement coverage**: `AgentKit-OTS-xUnit-Execute`, `AgentKit-OTS-xUnit-Report`.

#### AgentKitCore_SystemPathContainment_FileBeneathDirectoryLink_IsDenied

**Scenario**: xUnit discovers and runs this test on every platform and target framework in the CI
matrix; the test verifies that a file reachable only through a directory link is denied.

**Expected**: xUnit executes the test, the test passes, and the result appears in the TRX output
attributed to the platform and framework that produced it, which is what lets the platform
requirements filter results by source.

**Requirement coverage**: `AgentKit-OTS-xUnit-Execute`, `AgentKit-OTS-xUnit-Report`.

### Requirements Coverage

- **`AgentKit-OTS-xUnit-Execute`**: ToolName_Create_FamilyAndVerb_ProducesUnderscoreSeparatedName,
  ToolName_Validate_BareAgentFrameworkName_IsRejected,
  AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent,
  AgentKitCore_SystemPathContainment_FileBeneathDirectoryLink_IsDenied
- **`AgentKit-OTS-xUnit-Report`**: ToolName_Create_FamilyAndVerb_ProducesUnderscoreSeparatedName,
  ToolName_Validate_BareAgentFrameworkName_IsRejected,
  AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent,
  AgentKitCore_SystemPathContainment_FileBeneathDirectoryLink_IsDenied
