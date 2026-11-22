package AgentKitCore_ToolOutputStore {
    private import SoftwareItemMetadata::*;

    part def ToolOutputStore {
        doc /* Holds the large tool outputs one composition has retained in memory, normalized into pageable lines and named by opaque monotonic references of the form output-N, evicting the oldest first to stay within its ceilings and distinguishing a malformed, unknown or evicted reference. It captures its entries and counter for export and can be filled once from such a capture under its own ceilings. Internal; no path is ever involved. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolOutputStore.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolOutputStoreTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-output-store.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-output-store.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-output-store.yaml");
        }
    }
}
