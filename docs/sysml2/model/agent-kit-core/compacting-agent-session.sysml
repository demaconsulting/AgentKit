package AgentKitCore {
    part def CompactingAgentSession {
        doc /* The session implementation that owns the sequencing: it records each turn in its own transcript, takes the window and the occupancy from the live provider session itself, and on crossing the rotation threshold consolidates, creates the replacement provider session from the preserved content, and only then disposes the one it replaced. It answers pressure in counts of turns - compacting one level terser when the window fills again quickly, discarding the oldest consolidated slot when it is already at its tersest, falling through to the oldest verbatim turn when no slot remains, and relaxing after a quiet stretch - and reports the level and any dropped material. Whether the live provider session has been released travels with that provider session, so a rotation replaces them together. A provider session it creates but cannot adopt is released rather than lost. A creation whose cleanup also fails still reports the original creation failure. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Core/CompactingAgentSession.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Core.Tests/CompactingAgentSessionTests.cs */
        comment designRef /* Design: docs/design/agent-kit-core/compacting-agent-session.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-core/compacting-agent-session.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-core/compacting-agent-session.yaml */
    }
}