package AgentKitAgentsChatClient_ToolPackContextProvider {
    private import SoftwareItemMetadata::*;

    part def ToolPackContextProvider {
        doc /* A Microsoft Agent Framework context provider that contributes AgentKit tools - composed from tool packs, or from any function list bounded by the result ceilings - and optional instructions to every run of a ChatClientAgent, composing them once per conversation with that conversation's own large-output retention, persisting the retention's snapshot in the session state bag after every run so references survive session serialization, and refusing any agent that is not a chat-client agent. Its options type is documented as a supporting value type in its own unit doc. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Agents.ChatClient/ToolPackContextProvider.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Agents.ChatClient.Tests/ToolPackContextProviderTests.cs");
            designFiles = ("docs/design/agent-kit-agents-chat-client/tool-pack-context-provider.md");
            verificationFiles = ("docs/verification/agent-kit-agents-chat-client/tool-pack-context-provider.md");
            requirementFiles = ("docs/reqstream/agent-kit-agents-chat-client/tool-pack-context-provider.yaml");
        }
    }
}
