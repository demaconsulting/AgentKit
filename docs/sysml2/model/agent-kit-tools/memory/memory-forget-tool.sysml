package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MemoryForgetTool {
        doc /* Publishes the memory_forget tool: removes exactly one named memory permanently, reports the size of the store afterwards, refuses an identifier the store does not hold, and publishes no way to express a bulk removal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryForgetTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryForgetToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-forget-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-forget-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-forget-tool.yaml");
        }
    }
}
