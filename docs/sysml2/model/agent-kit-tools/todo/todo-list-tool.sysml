package AgentKitTools {
    part def TodoListTool {
        doc /* Publishes the todo_list tool, reporting the agent's task list as a structured object with an item count and the items in the order they were written down, each carrying its id, title, status and note, reporting an empty list as an empty list rather than a refusal. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Todo/TodoListTool.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoListToolTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo/todo-list-tool.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo/todo-list-tool.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo/todo-list-tool.yaml */
    }
}
