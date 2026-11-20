package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def GuardedToolFactory {
        doc /* The only supported way to construct a tool, validating the tool name and applying a selective result-delivery guard so that text and content reach the runtime in the form the tool produced them while any other result is serialized to JSON as the underlying function factory would have done. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/GuardedToolFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/GuardedToolFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-core/guarded-tool-factory.md");
            verificationFiles = ("docs/verification/agent-kit-core/guarded-tool-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/guarded-tool-factory.yaml");
        }
    }
}
