## CopilotClientStarter

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotClientStarter` class starts a host's GitHub Copilot `CopilotClient`, turning a runtime that
cannot load into an error that says why and what to do.

### Purpose

**A Copilot client that cannot load its runtime fails opaquely, for one of two common reasons.**

- **An ambient `COPILOT_CLI_PATH`.** In-process hosting loads the runtime library from the
  application's output, where `Microsoft.Copilot.SDK` provisions it at build time — unless the
  `COPILOT_CLI_PATH` environment variable is set, in which case it loads the library from beside that
  path instead. There is no per-client override. Every shell the Copilot CLI starts sets the variable
  to the CLI's own installation, which does not ship the in-process runtime library, so an application
  launched from such a shell fails to start with an error about a missing library that names neither
  the variable nor the remedy.
- **A runtime that was never provisioned.** The SDK provisions its runtime only into a project that
  references the package directly. A host application that reaches the SDK only transitively, through
  AgentKit, has no runtime in its output.

This class names the likely cause in both cases, with the remedies.

**It diagnoses and changes nothing.** The host owns the client and chooses its connection. This class
never alters the connection and never changes the process environment: pointing `COPILOT_CLI_PATH` at
a valid runtime is a supported configuration, so silently ignoring or clearing it would be wrong, and
changing the process environment from a library is a global side effect. Two alternatives were
rejected for the same reason: switching the host to a spawned-process connection automatically would
override the host's choice, and wrapping session-creation failures inside AgentKit would catch only a
client started implicitly and would misattribute failures that have nothing to do with starting.

**It is optional.** A host that starts the client itself loses only the diagnosis of that start. A host
that hands AgentKit a client it has not started gets the diagnosis anyway, because the production turn
channel starts the client through this class before the first session; only the start is wrapped,
never a session's creation, so no other failure is misattributed. The samples and the opt-in live suite
start their clients through it.

The class is static, holds no state, and is safe for concurrent use. It reads one environment
variable, and only once a start has already failed.

### Data Model

- **`CliPathVariable`** (`const string`) — `COPILOT_CLI_PATH`, the environment variable that redirects
  in-process hosting to another runtime.

The class has no fields.

### Key Methods

#### StartAsync(CopilotClient client, CancellationToken cancellationToken)

**Purpose:** The public entry point: start the host's client, explaining a failure to start.

**Algorithm:** Reject a null `client`, then delegate to the internal overload with the client's own
start and the process environment's reader.

**Preconditions:** `client` is not null.

**Postconditions:** The client has started; or an exception as below, with the client left not
started and still the host's to dispose.

#### StartAsync(Func&lt;CancellationToken, Task&gt; start, Func&lt;string, string?&gt; getEnvironmentVariable, CancellationToken)

**Purpose:** Run a start and explain its failure, with the start and the environment supplied.

**Algorithm:** Await the start. On an `OperationCanceledException`, let it propagate unchanged: a
cancellation is not a fault to explain. On any other exception, read `COPILOT_CLI_PATH` and throw an
`InvalidOperationException` whose message is `Explain` of that value and whose `InnerException` is the
original failure.

Internal, as the test seam: the SDK's client cannot be made to fail on demand without a broken
runtime, and a test must not change the process environment, so both are injected.

**Postconditions:** As the public overload.

#### Explain(string? cliPath)

**Purpose:** Compose the explanation for a client that failed to start.

**Algorithm:**

- **When `cliPath` is non-blank**, the message names the variable and its value, and explains that
  in-process hosting loads the runtime library from beside that path instead of the runtime
  `Microsoft.Copilot.SDK` provisioned into the application's output; that a Copilot CLI installation
  does not ship that library; and that every shell the Copilot CLI starts sets the variable. It gives
  three remedies: unset `COPILOT_CLI_PATH` for the process, point it at a runtime that ships the
  in-process library, or connect with `RuntimeConnection.ForSpawnedStdio` and an environment that
  omits it.
- **When it is null or blank**, the message states that the runtime is loaded from the application's
  output, and names the most common cause: a host application that does not reference
  `Microsoft.Copilot.SDK` directly, because the SDK provisions its runtime only into a project that
  references the package itself. The remedy is to add that reference.

Both messages end by pointing at the inner exception for the runtime's own error.

**Postconditions:** The explanation text.

### Error Handling

| Condition | Handling |
| --- | --- |
| Null `client` | `ArgumentNullException` propagates |
| Start canceled | `OperationCanceledException` propagates unchanged |
| Start failed, `COPILOT_CLI_PATH` set | `InvalidOperationException` naming the variable, value and remedies |
| Start failed, `COPILOT_CLI_PATH` unset | `InvalidOperationException` naming the missing direct SDK reference |

In both failure cases the original exception is the `InnerException`, so nothing the runtime reported
is lost. The class handles nothing locally: every failure propagates, explained or unchanged.

### Dependencies

- **Microsoft.Copilot.SDK** — supplies `CopilotClient` and its start; see *Microsoft.Copilot.SDK
  Design*.

### Callers

A host application calls `CopilotClientStarter.StartAsync` once, after constructing its client and
before handing it to `CopilotAgentFactory`, `CopilotProviderSessionFactory` or `CopilotSummarizer`.
Within this system, the production turn channel calls it before every session it opens or resumes,
because the client's RPC surface, which the channel uses to discover MCP servers, exists only once
the client has started. On a started client the start returns at once; on one the host has not
started, it starts the client exactly as a host would, so a runtime that cannot load is explained
rather than surfacing as a bare runtime error. The client is still the host's to start, to own and
to dispose; see *CopilotTurnChannel Unit Design*.
