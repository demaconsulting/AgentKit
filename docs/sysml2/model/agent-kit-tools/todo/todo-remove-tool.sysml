package AgentKitTools {
    part def TodoRemoveTool {
        doc /* Publishes the todo_remove tool, dropping the step carrying an id and reporting the new size of the list, refusing an unknown id with a plain statement of fact naming the identifiers the list does hold and prescribing no other tool, and refusing every malformed request as a returned denial rather than throwing. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Todo/TodoRemoveTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoRemoveToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo/todo-remove-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo/todo-remove-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo/todo-remove-tool.yaml */
    }
}
