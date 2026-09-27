package AgentKitTools {
    part def FilePack {
        doc /* Publishes the file tool family as one pack: the list, copy, move and delete tools for a file, and the create, move and delete tools for a directory, in a fixed order, each governed by the supplied policy, requiring no host capability. The six tools that change the file system — copy, move and delete a file, and create, move and delete a directory — are published only when the policy permits writing in some location; list consults only the read decision and is published under every policy. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/File/FilePack.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FilePackTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file/file-pack.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file/file-pack.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file/file-pack.yaml */
    }
}
