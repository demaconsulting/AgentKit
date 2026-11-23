package AgentKitCore {
    private import SoftwareItemMetadata::*;
    public import AgentKitCore_RealPathResolver::*;
    public import AgentKitCore_PathRule::*;
    public import AgentKitCore_PathPolicy::*;
    public import AgentKitCore_ToolLimits::*;
    public import AgentKitCore_ToolResult::*;
    public import AgentKitCore_ToolOutputOptions::*;
    public import AgentKitCore_ToolOutputStore::*;
    public import AgentKitCore_ToolOutputSpill::*;
    public import AgentKitCore_ToolOutputRetention::*;
    public import AgentKitCore_ToolOutputReadTool::*;
    public import AgentKitCore_ToolOutputSearchTool::*;
    public import AgentKitCore_ToolName::*;
    public import AgentKitCore_GuardedToolFactory::*;
    public import AgentKitCore_ToolPack::*;
    public import AgentKitCore_ToolPackBuilder::*;
    public import AgentKitCore_ImagePromotingChatClient::*;
    public import AgentKitCore_AgentSession::*;
    public import AgentKitCore_AgentSessionOptions::*;
    public import AgentKitCore_ContextUsage::*;
    public import AgentKitCore_SessionTranscript::*;
    public import AgentKitCore_ContextLayout::*;
    public import AgentKitCore_RotationEngine::*;
    public import AgentKitCore_Summarizer::*;
    public import AgentKitCore_ProviderSession::*;
    public import AgentKitCore_InMemoryProviderSession::*;
    public import AgentKitCore_CompactingAgentSession::*;
    public import AgentKitCore_AgentKitChatReducer::*;
    public import AgentKitCore_ModelInfo::*;
    public import AgentKitCore_ModelCatalog::*;
    public import AgentKitCore_ModelSettings::*;
    public import AgentKitCore_ModelSettingsChatClient::*;

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
