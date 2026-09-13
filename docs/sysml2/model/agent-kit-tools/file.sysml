package AgentKitTools {
    part def File {
        doc /* The type-agnostic file tool family: policy-governed listing, copying, moving and deletion of files of any type, published as one pack under the file family prefix. */

        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/File/FileTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/file.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/file.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/file.yaml */

        part fileListTool : FileListTool;
        part fileCopyTool : FileCopyTool;
        part fileMoveTool : FileMoveTool;
        part fileDeleteTool : FileDeleteTool;
        part filePack : FilePack;
    }
}
