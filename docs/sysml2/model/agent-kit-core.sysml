package AgentKitCore {
    doc
    /*
     * AgentKitCore provides the core abstractions and agent runtime used to compose
     * AI agent systems in .NET, including the provider seam, tool and permission
     * model, and context-window management.
     */

    part def AgentKitCoreSystem {
        doc /* The AgentKit Core system as a whole. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/AgentKitCoreTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core.yaml */

        part demo : Demo;
        part realPathResolver : RealPathResolver;
        part pathRule : PathRule;
        part pathPolicy : PathPolicy;
    }
}
