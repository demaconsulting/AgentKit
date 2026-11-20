package AgentKitTools {
    private import SoftwareItemMetadata::*;

    part def TodoRemoveTool {
        doc /* Publishes the todo_remove tool, dropping the step carrying an id and reporting the new size of the list, refusing an unknown id with a plain statement of fact naming the identifiers the list does hold and prescribing no other tool, and refusing every malformed request as a returned denial rather than throwing. */

        metadata info : SoftwareInfo {
            sourceFiles = ("src/DemaConsulting.AgentKit.Tools/Todo/TodoRemoveTool.cs");
            testFiles = ("test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoRemoveToolTests.cs");
            designFiles = ("docs/design/agent-kit-tools/todo/todo-remove-tool.md");
            verificationFiles = ("docs/verification/agent-kit-tools/todo/todo-remove-tool.md");
            requirementFiles = ("docs/reqstream/agent-kit-tools/todo/todo-remove-tool.yaml");
        }
    }
}
