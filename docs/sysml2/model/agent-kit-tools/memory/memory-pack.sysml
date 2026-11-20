package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def MemoryPack {
        doc /* Publishes the memory tool family as one pack: file, recall, update, revise and forget in that order over one store, taking the embedding generator, the author's controls and optional substitute persistence at construction, allocating a fresh in-process store per composition when none was supplied, requiring no host capability, accepting a policy it never consults, and publishing the suggested instruction an application should give an agent that carries the family, including that a correction drawn from a different document is a revision citing that document. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Memory/MemoryPack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Memory/MemoryPackTests.cs");
            designFiles = ("docs/design/agent-kit-tools/memory/memory-pack.md");
            verificationFiles = ("docs/verification/agent-kit-tools/memory/memory-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/memory/memory-pack.yaml");
        }
    }
}
