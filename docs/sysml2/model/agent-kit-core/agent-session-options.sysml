package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def AgentSessionOptions {
        doc /* Everything an application configures about one session - instructions, tools, summarizer and the maximum number of most-recent turns kept verbatim. It carries no provider window: the window is a fact about the provider, stated where the provider is constructed and answered by the adapter thereafter. Validates the application's own numbers - a required summarizer, no null tool declaration and a positive verbatim tail - and makes no prediction, in a currency it cannot measure, about whether the arrangement will settle. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/AgentSessionOptions.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/AgentSessionOptionsTests.cs");
            designFiles = ("docs/design/agent-kit-core/agent-session-options.md");
            verificationFiles = ("docs/verification/agent-kit-core/agent-session-options.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/agent-session-options.yaml");
        }
    }
}