package AgentKitTools {
    part def FilePack {
        doc /* Publishes the file tool family as one pack: the list, copy, move and delete tools in a fixed order, each governed by the supplied policy, requiring no host capability. The three tools that change the file system — copy, move and delete — are published only when the policy permits writing in some location; list consults only the read decision and is published under every policy. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FilePack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FilePackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-pack.yaml */
    }
}
