package AgentKitTools_MemoryUpdateTool {
    private import SoftwareItemMetadata::*;

    part def MemoryUpdateTool {
        doc /* Publishes the memory_update tool: replaces a memory's detail payload while leaving its descriptor, its provenance and its vector untouched, embedding nothing, and echoes back what the memory is still filed under and still cites. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryUpdateTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryUpdateToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-update-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-update-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-update-tool.yaml");
        }
    }
}
