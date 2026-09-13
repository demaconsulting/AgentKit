package AgentKitTools {
    part def TextFileLineBuffers {
        doc /* The text-only cut/paste buffer store shared by the cut and paste tools of one composition: named slots that a cut captures into and a paste reads from without consuming, a later capture replacing a slot, thread-safe and living for the lifetime of the tool instances. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileLineBuffers.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileLineBuffersTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-line-buffers.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-line-buffers.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-line-buffers.yaml */
    }
}
