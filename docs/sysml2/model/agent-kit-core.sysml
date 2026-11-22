package AgentKitCore {
    private import SoftwareItemMetadata::*;
    private import AgentKitCore_RealPathResolver::*;
    private import AgentKitCore_PathRule::*;
    private import AgentKitCore_PathPolicy::*;
    private import AgentKitCore_ToolLimits::*;
    private import AgentKitCore_ToolResult::*;
    private import AgentKitCore_ToolOutputOptions::*;
    private import AgentKitCore_ToolOutputStore::*;
    private import AgentKitCore_ToolOutputSpill::*;
    private import AgentKitCore_ToolOutputRetention::*;
    private import AgentKitCore_ToolOutputReadTool::*;
    private import AgentKitCore_ToolOutputSearchTool::*;
    private import AgentKitCore_ToolName::*;
    private import AgentKitCore_GuardedToolFactory::*;
    private import AgentKitCore_ToolPack::*;
    private import AgentKitCore_ToolPackBuilder::*;
    private import AgentKitCore_ImagePromotingChatClient::*;
    private import AgentKitCore_AgentSession::*;
    private import AgentKitCore_AgentSessionOptions::*;
    private import AgentKitCore_ContextUsage::*;
    private import AgentKitCore_SessionTranscript::*;
    private import AgentKitCore_ContextLayout::*;
    private import AgentKitCore_RotationEngine::*;
    private import AgentKitCore_Summarizer::*;
    private import AgentKitCore_ProviderSession::*;
    private import AgentKitCore_InMemoryProviderSession::*;
    private import AgentKitCore_CompactingAgentSession::*;
    private import AgentKitCore_AgentKitChatReducer::*;
    private import AgentKitCore_ModelInfo::*;
    private import AgentKitCore_ModelCatalog::*;
    private import AgentKitCore_ModelSettings::*;
    private import AgentKitCore_ModelSettingsChatClient::*;

    doc
    /*
     * AgentKitCore provides the small, stable contract every other AgentKit package
     * depends upon: policy primitives that bound where a tool may act and how a path a
     * model states is interpreted, guarded tool construction, tool result helpers,
     * the tool-pack contract, provider-independent delivery of tool-returned images, and
     * opt-in retention of tool outputs too large to return, which hands the model a bounded
     * preview and an opaque reference it pages through with confined reader tools, through a
     * per-conversation retention handle whose retained outputs can be exported and restored,
     * and bounding of any tool, including one it did not build, by the same result ceilings.
     * It also owns the provider-agnostic session engine, because a kit of safe tools is
     * worth nothing to an agent that cannot run long enough to use them: the session keeps
     * its own transcript out of session, watches how much of the provider's context window
     * it occupies, and when the window fills consolidates older history into a round-robin
     * structure of tiered slots, creates a fresh provider session seeded with the preserved
     * content, and only then disposes the one it replaced. Rotation rather than in-place
     * reduction is the only mechanism both provider shapes support - one resends history
     * each turn, the other holds it server-side - so the behavior is identical on either.
     * For a host that owns its own chat history instead, it offers a chat reducer that applies
     * the same consolidation prompt to that history, at reduced fidelity.
     * It also defines the provider-neutral model catalog an application lists a provider's models
     * through, filtered by the capabilities it requires, and the model settings it validates against
     * the selected model before a session starts, from which the host's vision declaration is derived.
     */

    part def AgentKitCoreSystem {
        doc /* The AgentKit Core system as a whole. */

        metadata info : SoftwareInfo {
            testFiles = ("test/DemaConsulting.AgentKit.Core.Tests/AgentKitCoreTests.cs");
            designFiles = ("docs/design/agent-kit-core.md");
            verificationFiles = ("docs/verification/agent-kit-core.md");
            requirementFiles = ("docs/reqstream/agent-kit-core.yaml");
        }

        part realPathResolver : RealPathResolver;
        part pathRule : PathRule;
        part pathPolicy : PathPolicy;
        part toolLimits : ToolLimits;
        part toolResult : ToolResult;
        part toolOutputOptions : ToolOutputOptions;
        part toolOutputStore : ToolOutputStore;
        part toolOutputSpill : ToolOutputSpill;
        part toolOutputRetention : ToolOutputRetention;
        part toolOutputReadTool : ToolOutputReadTool;
        part toolOutputSearchTool : ToolOutputSearchTool;
        part toolName : ToolName;
        part guardedToolFactory : GuardedToolFactory;
        part toolPack : ToolPack;
        part toolPackBuilder : ToolPackBuilder;
        part imagePromotingChatClient : ImagePromotingChatClient;
        part agentSession : AgentSession;
        part agentSessionOptions : AgentSessionOptions;
        part contextUsage : ContextUsage;
        part sessionTranscript : SessionTranscript;
        part contextLayout : ContextLayout;
        part rotationEngine : RotationEngine;
        part summarizer : Summarizer;
        part providerSession : ProviderSession;
        part inMemoryProviderSession : InMemoryProviderSession;
        part compactingAgentSession : CompactingAgentSession;
        part agentKitChatReducer : AgentKitChatReducer;
        part modelInfo : ModelInfo;
        part modelCatalog : ModelCatalog;
        part modelSettings : ModelSettings;
        part modelSettingsChatClient : ModelSettingsChatClient;
    }
}
