package AgentKitCore_ModelSettingsChatClient {
    private import SoftwareItemMetadata::*;
    private import AgentKitCore_ModelSettings::*;

    part def ModelSettingsChatClient {
        doc /* Applies a validated model selection to every request an IChatClient pipeline sends - the model, the provider-neutral reasoning effort and the output limit - without overriding a value the caller set or writing to the caller's options, and refuses at construction a selection carrying a context tier or an effort with no provider-neutral equivalent, which a chat client cannot apply, or a context window that no client beneath reports applying at the same size through the IContextWindowChatClient interface this unit declares. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Core/ModelSettingsChatClient.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/ModelSettingsChatClientTests.cs");
            designFiles = ("docs/design/agent-kit-core/model-settings-chat-client.md");
            verificationFiles = ("docs/verification/agent-kit-core/model-settings-chat-client.md");
            requirementFiles = ("docs/reqstream/agent-kit-core/model-settings-chat-client.yaml");
        }
    }

    // The decorator applies a validated selection, which shows the settings were checked against the model.
    dependency ModelSettingsChatClient to ModelSettings;
}