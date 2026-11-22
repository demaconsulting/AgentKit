package AgentKitCore_ToolPack {
    private import SoftwareItemMetadata::*;

    part def ToolPack {
        doc /* Defines the contract a package implements to publish its tools as one capability-gated family, together with the set of host capabilities a pack may require. A pack may return the subset of its family the supplied access policy leaves able to act, so the contract promises a non-null collection of non-null, family-prefixed tools rather than the same tools under every policy. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolPack.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolPackTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-pack.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-pack.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-pack.yaml");
        }
    }
}
