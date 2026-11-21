package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def GuardedToolFactory {
        doc /* The only supported way to construct a tool, validating the tool name and applying a selective result-delivery guard so that text and content reach the runtime in the form the tool produced them while any other result is serialized to JSON as the underlying function factory would have done, and returning the tool as a transparent wrapper that resolves a declared-oversized output to its refusal, or hands it to the composition's retention when one is composed; it also bounds any other function, including one the library did not build, by the result-character and binary-byte ceilings of a tool limits value. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/GuardedToolFactory.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/GuardedToolFactoryTests.cs");
            designFiles = ("docs/design/agent-kit-core/guarded-tool-factory.md");
            verificationFiles = ("docs/verification/agent-kit-core/guarded-tool-factory.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/guarded-tool-factory.yaml");
        }
    }
}
