package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MemoryReviseTool {
        doc /* Publishes the memory_revise tool: replaces a memory's descriptor, details and provenance together and recomputes its vector, taking provenance as a stated parameter and clearing rather than inheriting what the caller did not state, so no memory silently keeps a superseded citation. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryReviseTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryReviseToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-revise-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-revise-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-revise-tool.yaml");
        }
    }
}
