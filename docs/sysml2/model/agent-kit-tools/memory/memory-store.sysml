package AgentKitTools_MemoryStore {
    private import SoftwareItemMetadata::*;

    part def MemoryStore {
        doc /* The substitutable persistence contract for memories — add, find, replace, remove, cosine search nearest-first and count — together with the default in-process implementation: a locked list with an exhaustive cosine scan that refuses vectors of mismatched length. Also defines the two immutable records the contract is expressed in: a memory, and a memory paired with the cosine similarity of one comparison. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryStore.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryStoreTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-store.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-store.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-store.yaml");
        }
    }
}
