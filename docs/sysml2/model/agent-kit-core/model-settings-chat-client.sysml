package AgentKitCore {
    private import SoftwareItemMetadata::*;

    part def ModelSettingsChatClient {
        doc /* Applies a validated model selection to every request an IChatClient pipeline sends - the model, the provider-neutral reasoning effort and the output limit - without overriding a value the caller set or writing to the caller's options, and refuses at construction a selection carrying a context tier or an effort with no provider-neutral equivalent, which a chat client cannot apply. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ModelSettingsChatClient.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ModelSettingsChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-core/model-settings-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-core/model-settings-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/model-settings-chat-client.yaml");
        }
    }

    // The decorator applies a validated selection, which is what proves the settings fit the model.
    dependency ModelSettingsChatClient to ModelSettings;
}