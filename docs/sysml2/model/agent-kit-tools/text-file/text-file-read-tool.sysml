package AgentKitTools {
    part def TextFileReadTool {
        doc /* Publishes the text_file_read tool, returning a paged, line-numbered window of one file the access policy permits the agent to read, opening with a "path lines A-B of N" header. It pages a file larger than the read ceiling by streaming to the requested window, refusing only an unranged read of such a file with a refusal that names the total line count and directs the model to page with startLine and lineCount, and refusing a missing, non-permitted, directory or binary request with a plain statement of the refusal that names no other tool, redirecting only a binary file whose type an image tool can read to image_read. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/TextFile/TextFileReadTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/TextFile/TextFileReadToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/text-file/text-file-read-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/text-file/text-file-read-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/text-file/text-file-read-tool.yaml */
    }
}
