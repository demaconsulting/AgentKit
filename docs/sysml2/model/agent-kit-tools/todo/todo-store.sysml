package AgentKitTools {
    part def TodoStore {
        doc /* The flat task list belonging to one agent, shared internally by the list, set and remove tools of one composition: named items in insertion order, an existing identifier updating in place, an unknown identifier reported as a miss, four ordinally compared statuses, thread-safe, in memory for the lifetime of the tools built from it, and reachable only from the pack that allocates it so a delegated agent cannot write into its parent's list. */

        comment sourceRef /* Source: src/DemaConsulting.AgentKit.Tools/Todo/TodoStore.cs */
        comment testRef /* Test: test/DemaConsulting.AgentKit.Tools.Tests/Todo/TodoStoreTests.cs */
        comment designRef /* Design: docs/design/agent-kit-tools/todo/todo-store.md */
        comment verificationRef /* Verification: docs/verification/agent-kit-tools/todo/todo-store.md */
        comment reqRef /* Requirements: docs/reqstream/agent-kit-tools/todo/todo-store.yaml */
    }
}
