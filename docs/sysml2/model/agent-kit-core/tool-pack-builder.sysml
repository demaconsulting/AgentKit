package AgentKitCore_ToolPackBuilder {
    private import SoftwareItemMetadata::*;

    part def ToolPackBuilder {
        doc /* Composes tool packs into the tool list an application offers a model, registering a pack only when the host provides every capability the pack requires and rejecting two packs that claim the same family prefix, and, when the application asks, composing retention of large tool outputs over the result. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ToolPackBuilder.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ToolPackBuilderTests.cs");
            designFiles = ("docs/design/agent-kit-core/tool-pack-builder.md");
            verificationFiles = ("docs/verification/agent-kit-core/tool-pack-builder.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/tool-pack-builder.yaml");
        }
    }
}
