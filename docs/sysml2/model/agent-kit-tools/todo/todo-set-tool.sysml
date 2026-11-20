package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TodoSetTool {
        doc /* Publishes the todo_set tool, adding a step or replacing the step already carrying that id in place, defaulting the status to pending, accepting exactly the four published statuses, returning a plain statement of the id, the recorded status and the size of the list, and refusing every malformed request as a returned denial rather than throwing. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Todo/TodoSetTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoSetToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/todo/todo-set-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/todo/todo-set-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/todo/todo-set-tool.yaml");
        }
    }
}
