package AgentKitCore_ToolOutputSpill {
    private import SoftwareItemMetadata::*;

    part def ToolOutputSpill {
        doc /* Composes opt-in retention of large tool outputs into a tool list: rejects a result ceiling too small to carry a preview's header, wraps every tool so a structured result is serialized to JSON with the tool's serializer options as the guarded factory does and an output exceeding the result ceiling is retained and replaced by a bounded preview naming its reference and the reader tools, passes binary and other non-text content through unchanged, refuses an output too large to retain, and appends the two reader tools over a fresh per-composition store, composing through one ToolOutputRetention per call. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputSpill.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputSpillTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-spill.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-spill.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-spill.yaml");
        }
    }
}
