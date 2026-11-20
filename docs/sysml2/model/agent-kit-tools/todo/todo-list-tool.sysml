package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TodoListTool {
        doc /* Publishes the todo_list tool, reporting the agent's task list as a structured object with an item count and the items in the order they were written down, each carrying its id, title, status and note, reporting an empty list as an empty list rather than a refusal. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Todo/TodoListTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoListToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/todo/todo-list-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/todo/todo-list-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/todo/todo-list-tool.yaml");
        }
    }
}
