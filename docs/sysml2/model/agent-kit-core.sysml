package AgentKitCore {
    private import SoftwareItemMetadata::*;

    doc
    /*
     * AgentKitCore provides the small, stable contract every other AgentKit package
     * depends upon: policy primitives that bound where a tool may act and how a path a
     * model states is interpreted, guarded tool construction, tool result helpers,
     * the tool-pack contract, provider-independent delivery of tool-returned images, and
     * opt-in retention of tool outputs too large to return, which hands the model a bounded
     * preview and an opaque reference it pages through with confined reader tools.
     * It also owns the provider-agnostic session engine, because a kit of safe tools is
     * worth nothing to an agent that cannot run long enough to use them: the session keeps
     * its own transcript out of session, watches how much of the provider's context window
     * it occupies, and when the window fills consolidates older history into a round-robin
     * structure of tiered slots, creates a fresh provider session seeded with the preserved
     * content, and only then disposes the one it replaced. Rotation rather than in-place
     * reduction is the only mechanism both provider shapes support - one resends history
     * each turn, the other holds it server-side - so the behavior is identical on either.
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
    }
}
