package AgentKitTools_MemoryOptions {
    private import SoftwareItemMetadata::*;

    part def MemoryOptions {
        doc /* The author-governed controls over the memory family: the cosine similarity at or above which a new descriptor is treated as a near-duplicate (default 0.88) and the number of memories a recall returns (default 5), validated at construction, immutable, and published with a shared default instance. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryOptionsTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-options.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-options.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-options.yaml");
        }
    }
}
