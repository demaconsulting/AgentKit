package AgentKitCore_RealPathResolver {
    private import SoftwareItemMetadata::*;

    part def RealPathResolver {
        doc /* Resolves a path to an absolute, normalized location, with relative segments collapsed, so that containment is judged between locations spelled the same way. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/RealPathResolver.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/RealPathResolverTests.cs");
            designFiles = ("docs/design/agent-kit-core/real-path-resolver.md");
            verificationFiles = ("docs/verification/agent-kit-core/real-path-resolver.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/real-path-resolver.yaml");
        }
    }
}
