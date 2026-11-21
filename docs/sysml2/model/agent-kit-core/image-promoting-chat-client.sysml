package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ImagePromotingChatClient {
        doc /* Makes an image a tool returned visible to a provider whose tool-result channel cannot carry one, by promoting that image onto a user message inserted after the tool result and leaving a short placeholder in its place in a forwarded copy of the tool result, so no image bytes remain there; a text-only result, the caller's messages and the order of every other message are unchanged. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ImagePromotingChatClient.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ImagePromotingChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-core/image-promoting-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-core/image-promoting-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/image-promoting-chat-client.yaml");
        }
    }
}
