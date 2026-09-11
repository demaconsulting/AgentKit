package AgentKitCore {
    doc
    /*
     * AgentKitCore provides the small, stable contract every other AgentKit package
     * depends upon: policy primitives that bound where a tool may act and how a path a
     * model states is interpreted, guarded tool construction, tool result helpers,
     * the tool-pack contract, and provider-independent delivery of tool-returned images.
     */

    part def AgentKitCoreSystem {
        doc /* The AgentKit Core system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/AgentKitCoreTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core.yaml */

        part realPathResolver : RealPathResolver;
        part pathRule : PathRule;
        part pathPolicy : PathPolicy;
        part toolLimits : ToolLimits;
        part toolResult : ToolResult;
        part toolName : ToolName;
        part guardedToolFactory : GuardedToolFactory;
        part toolPack : ToolPack;
        part toolPackBuilder : ToolPackBuilder;
        part imagePromotingChatClient : ImagePromotingChatClient;
    }
}
