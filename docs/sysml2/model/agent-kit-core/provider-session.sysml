package AgentKitCore_ProviderSession {
    private import SoftwareItemMetadata::*;

    part def ProviderSession {
        doc /* The whole interface between the compaction engine and a provider adapter: the seed a fresh session is created from, what one turn produced, the minimal disposable session contract - which also answers how much of its provider's window it occupies and out of how much - and the factory that produces a replacement session at every rotation. The engine never touches a provider API directly. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ProviderSession.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ProviderSessionTests.cs");
            designFiles = ("docs/design/agent-kit-core/provider-session.md");
            verificationFiles = ("docs/verification/agent-kit-core/provider-session.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/provider-session.yaml");
        }
    }
}