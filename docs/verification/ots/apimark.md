## ApiMark Verification

This document provides the verification evidence for the ApiMark OTS software item. Requirements
for this OTS item are defined in the ApiMark OTS Software Requirements document.

### Required Functionality

DemaConsulting.ApiMark generates the API reference for each shipped package from that package's XML
doc comments, organized for gradual disclosure: an index page, then a page per namespace, type, and
member. Two properties of that output are relied upon beyond mere existence — that a documented
example renders as a fenced code block a reader can copy, and that the behavioral caveats written
in `<remarks>` survive into the generated pages.

Beyond generation, ApiMark is relied upon to **enforce** documentation coverage. The four shipped
projects set `ApiMarkEnforceDocs=Public` and `ApiMarkEnforceDocsSeverity=Error`,
so a public member without an XML doc summary fails the build rather than shipping
a reference with a hole in it.

### Verification Approach

ApiMark is verified by asserting the **real generated output of the real build**. The MSBuild
integration runs during the ordinary `dotnet build` of each shipped package, and the CI workflow
then runs FileAssert against the resulting files immediately afterwards, in the same job, before
the test step. If ApiMark had not run, or had run without producing content, the assertions would
fail and the build would fail with it.

ApiMark's own `--validate` self-test is deliberately **not** used as evidence. It covers only
version and help display and does not self-test generation, so passing it would prove nothing about
the capability these requirements describe. Asserting the generated artifacts is the stronger and
more honest evidence, and it is evidence about this repository's actual configuration rather than
about the tool in the abstract.

Two of the assertions check content rather than existence, because existence alone would not detect
the failure modes that matter. A reference whose examples rendered as prose, or whose remarks were
dropped, would still produce a complete-looking file tree.

### Test Scenarios

#### ApiMark_CoreApiIndex

**Scenario**: ApiMark generates the API reference for the AgentKit Core package during the build.

**Expected**: The index page `src/DemaConsulting.AgentKit.Core/generated/api/api.md` exists and
names the `DemaConsulting.AgentKit.Core` namespace.

**Requirement coverage**: `AgentKit-OTS-ApiMark-Generate`.

#### ApiMark_ToolsApiIndex

**Scenario**: ApiMark generates the API reference for the AgentKit Tools package during the build.

**Expected**: The index page `src/DemaConsulting.AgentKit.Tools/generated/api/api.md` exists and
names the `DemaConsulting.AgentKit.Tools` namespace.

**Requirement coverage**: `AgentKit-OTS-ApiMark-Generate`.

#### ApiMark_CopilotApiIndex

**Scenario**: ApiMark generates the API reference for the AgentKit Copilot adapter package during
the build.

**Expected**: The index page
`src/DemaConsulting.AgentKit.Agents.Copilot/generated/api/api.md` exists and names the
`DemaConsulting.AgentKit.Agents.Copilot` namespace.

**Requirement coverage**: `AgentKit-OTS-ApiMark-Generate`.

#### ApiMark_ChatClientApiIndex

**Scenario**: ApiMark generates the API reference for the AgentKit ChatClient adapter package during
the build.

**Expected**: The index page
`src/DemaConsulting.AgentKit.Agents.ChatClient/generated/api/api.md` exists and names the
`DemaConsulting.AgentKit.Agents.ChatClient` namespace.

**Requirement coverage**: `AgentKit-OTS-ApiMark-Generate`.

#### ApiMark_ExampleCodeRendered

**Scenario**: A member documented with an `<example>` containing a `<code>` block is generated to
its own member page.

**Expected**: The `PathPolicy.EmitRelative` member page contains a fenced `csharp` code block, so
the example is presented as copyable code rather than as prose.

**Requirement coverage**: `AgentKit-OTS-ApiMark-RenderExamples`.

#### ApiMark_TransitionHazardDocumented

**Scenario**: The path-model transition hazard, documented in the `<remarks>` of the `PathPolicy`
constructor, is generated to the constructor's page.

**Expected**: The `PathPolicy` constructor page contains the phrase "Transition hazard".

**Requirement coverage**: `AgentKit-OTS-ApiMark-RenderExamples`.

#### ApiMark_PackConstructorsDocumented

**Scenario**: Documentation-coverage enforcement is active for the shipped packages, and the
constructors it requires to be documented are documented and reach the generated reference.

**Expected**: The `TextFilePack` and `ImagePack` constructor pages each contain the summary prose
"The pack carries no state".

**Rationale**: Enforcement is verified by the build itself. With
`ApiMarkEnforceDocsSeverity=Error`, a single undocumented public member fails the build, so a
green build cannot coexist with an undocumented public member — the absence of a failure is the
evidence, and it is produced on every CI run rather than by a separate step. This assertion adds
the positive half: that the two constructors whose absence of documentation was the entire measured
debt are now present, documented, and generated. The two are chosen deliberately because they were
the real defect: as implicit default constructors they were invisible to `CS1591`, so they are
precisely the case that would silently regress if enforcement were removed or became inert.

**Requirement coverage**: `AgentKit-OTS-ApiMark-EnforceDocs`.

### Requirements Coverage

- **`AgentKit-OTS-ApiMark-Generate`**: ApiMark_CoreApiIndex, ApiMark_ToolsApiIndex,
  ApiMark_CopilotApiIndex, ApiMark_ChatClientApiIndex
- **`AgentKit-OTS-ApiMark-RenderExamples`**: ApiMark_ExampleCodeRendered,
  ApiMark_TransitionHazardDocumented
- **`AgentKit-OTS-ApiMark-EnforceDocs`**: ApiMark_PackConstructorsDocumented
