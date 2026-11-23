## CopilotSignIn

![AgentKit Copilot Agents Structure](AgentKitAgentsCopilotView.svg)

The `CopilotSignIn` unit decides how the Copilot runtime AgentKit starts signs in to GitHub, and
against which host. It orders the sign-in sources, keeps the host with the source used, reuses the
user's Copilot CLI sign-in through a short-lived helper runtime without sharing anything but the
account entry, asks a host's token provider, and explains a runtime that no source signed in.
`CopilotSignInSource` is the public enumeration a started runtime reports its source with.

### Purpose

**A user signed in only with the Copilot CLI expects an application built on it to sign in too.**
Before this unit, the confined runtime could sign in only from a token, the environment, its own
data directory or the GitHub CLI. A user who ran only `copilot login` was refused, and a user whose
GitHub CLI was signed in to `github.com` while the Copilot CLI was signed in to a GitHub Enterprise
Cloud host was silently signed in to `github.com`.

**The confined runtime cannot read the Copilot CLI's token itself.** The Copilot CLI keeps its token in the system
credential store. The confined runtime runs in `CopilotClientMode.Empty`, and for every runtime it spawns in that mode
the SDK sets the variable that disables the system keychain after the caller's environment, so no supported option turns
the credential store back on. This was confirmed live: an Empty-mode runtime on a home holding only the account entry,
with that variable set to `0` in its environment and the GitHub CLI hidden, reports `Not authenticated`. The
alternatives — running the confined runtime in CodingAgent mode, AgentKit spawning the CLI itself over TCP, or a wrapper
executable that removes the variable — each break the documented confinement (CodingAgent mode turns off the SDK's safe
session defaults and gives a long-lived runtime the whole credential store) and were rejected.

**So a second runtime reads it, briefly, and shares only the login.** AgentKit starts the same
executable once more, in the only mode that reads the credential store, on a new temporary home
holding nothing but the account's host and login, asks it for that account's token, and stops it.
It never opens a session, so no MCP server, hook, agent, skill, plugin or instruction can load, and
its home holds none. The token is handed to the confined runtime as its GitHub token, with the
account's host; the confined runtime's mode, environment and data directory are unchanged. AgentKit
never reads the credential store itself, so the same code works wherever the Copilot CLI does.

**The order of the sources.** The first that applies wins:

1. The host's `gitHubToken` or `gitHubTokenProvider` — the most deliberate choice.
2. `COPILOT_GITHUB_TOKEN`, `GH_TOKEN` or `GITHUB_TOKEN` in the host's environment, which the runtime
   reads itself. This comes before the Copilot CLI sign-in because the Copilot CLI (`copilot login
   --help`) and the GitHub CLI put these variables ahead of their stored sign-in, so the runtime signs
   in as `copilot` would in the same shell; because a variable is a deliberate per-process choice (CI,
   a container, a service account) that an ambient per-user sign-in must not silently override; and
   because it costs no extra CLI start. A stray variable is made visible, not hidden: the source is
   reported, and a refusal names the variable and the account the Copilot CLI step would have used.
3. The user's Copilot CLI sign-in, unless the host turns it off — the user's Copilot-specific choice
   of account and host, so it comes before the GitHub CLI.
4. The runtime's own fallbacks: a sign-in stored in AgentKit's data directory, then the GitHub CLI.

**Possible future work: signing in from the application.** The runtime offers a device-flow sign-in
that stores its result in the data directory. It is not offered: the runtime then stores the token in
plain text in the data directory, and every sign-in mints a new OAuth token, of which GitHub keeps a
limited number per user, application and scope.

### Data Model

`CopilotSignInSource` (public enumeration): `Unknown` (not read, or a kind of sign-in AgentKit does
not know), `GitHubToken`, `GitHubTokenProvider`, `EnvironmentVariable`, `CopilotCli`,
`DataDirectory`, `GitHubCli`.

`CopilotSignInPlan` (internal class; a class rather than a record so no generated `ToString` or
equality prints or compares the token):

- **`Source`** (`CopilotSignInSource?`) — what AgentKit chose: `GitHubToken`, `GitHubTokenProvider`
  or `CopilotCli` when it hands the runtime a token; `EnvironmentVariable` when the runtime reads the
  environment's token; `null` when the runtime's own fallbacks decide.
- **`Token`** (`string?`) — the token handed to the runtime, or `null`. Never logged or written.
- **`HostName`** (`string?`) — the bare host name the runtime's `COPILOT_GH_HOST` is set to, or `null`
  when no host was chosen, leaving the environment's value, which is then unset or blank.
- **`RequestedHost`** (`string?`) — the host the host chose (`gitHubHost`, else `COPILOT_GH_HOST`),
  normalized, or `null`.
- **`Provider`** — the host's token provider, which sessions ask, or `null`.
- **`CliAccount`** (`CopilotCliAccount?`) — the Copilot CLI account found, for the findings and the
  account warning.
- **`HostSuppliedFinding`**, **`EnvironmentFinding`**, **`CopilotCliFinding`**,
  **`FallbackFinding`** (`string`) — what each step found, in words that name hosts, logins, files and
  variable names only.
- **`ToString()`** — source, host name and `<redacted>` or `none` for the token.
- **`Secrets`** (`CopilotSignInSecrets`) — the exact tokens the sign-in knows. `ResolveAsync` and
  `FromOptions` fill it, and the runtime's session token provider adds each session token to it.
- **`Redact(text)`** — removes `Token`, every value in `Secrets` and any GitHub-token-shaped text from
  a text AgentKit is about to quote.

`CopilotSignInSecrets` (internal class): a thread-safe set of secret values, compared ordinally.
`Add(value)` ignores a `null`, empty or whitespace value, and records a value with surrounding
whitespace both as given and trimmed. `Redact(text, also)` replaces every known value (and `also`, when
given), longest first, with `<redacted>`, and then applies the GitHub-token-shape pattern. No value is
too short to be redacted: a secret's safety comes before the readability of the text it appears in.
The session token provider can add values while a start failure on another thread is being
redacted, so the set is read under a lock.

`CopilotRedactedException` (internal sealed exception, in `CopilotSignIn.cs`): the redacted copy of a
runtime or SDK failure that AgentKit keeps as an inner exception in place of the original.

- **`OriginalTypeName`** (`string`) — the original's full type name.
- **`Message`** — the original's message, redacted; a `null` message becomes empty.
- **`StackTrace`** (override) — the original's stack trace text, redacted; `null` when the original
  was never thrown.
- **`ToString()`** (override) — the layout of `Exception.ToString`, led by `OriginalTypeName`: the type
  name and message, then `--->` and the inner copy's string with the end-of-inner marker, then the
  stack trace.
- **`InnerException`** — a copy of the original's inner exception, made the same way, down to
  `MaximumDepth` (16) exceptions. For an `AggregateException` it is the copy of its first inner
  exception; the aggregate's own message lists every inner exception's message, and is redacted.
- **`HResult`** — copied. `Data`, `Source`, `HelpLink` and every property of the original's own type
  are not copied, because they may hold anything.

The copy is made every time, not only when a secret is spotted: that fails closed for properties
AgentKit cannot inspect, such as an RPC error's data. It is internal: a host catches the outer
exception, whose type never changes, and the copy is diagnostic text. It is also the cause of every
failure AgentKit relays (`RedactRelayed`).

`CopilotCliAccount` (internal record): `Host` (normalized `https://name`) and `Login`; never a token.

`CopilotCliAccountLookup` (internal record): the `Account` found or `null`, the `ConfigurationPath`
read, and the `Problem` when there is no account.

`ICopilotSignInHelper` (internal interface): `StartAsync`, `GetAuthStatusAsync`, `GetAllUsersAsync`
and disposal — the calls the helper runtime needs. `CopilotClientSignInHelper` implements it over a
`CopilotClient`, which it owns.

`CopilotSignInContext` (internal record): the machine the resolution reads — the `CopilotCli` search
context (platform, `PATH`, file system), the user's profile folder, a factory for the helper's
temporary home, a factory for the helper, a function that deletes a directory, and the helper's time limit.
`Current()` builds the production context: `Directory.CreateTempSubdirectory` with the prefix
`agentkit-copilot-signin-` (a unique folder, private to the user on Unix), a real `CopilotClient`,
`DeleteHelperHome` and a 60-second limit.

Constants: `DefaultHost` (`https://github.com`), `ConfigurationSizeLimit` (1 MiB),
`MaximumLoginLength` (100), `HelperTimeout` (60 seconds, long enough for a first start and a
credential-store prompt the user answers), `DefaultTokenLifetime` (eight hours, the usual lifetime of
a GitHub user token), `TokenVariables` (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`),
`SecretVariables` (the token variables plus `GH_ENTERPRISE_TOKEN` and `GITHUB_ENTERPRISE_TOKEN`,
whose values are redacted), `InvalidEnvironmentHostMessage` (the refusal of an invalid
`COPILOT_GH_HOST`, which never quotes the value).

### Key Methods

#### ResolveAsync(options, dataDirectory, hostEnvironment, launch, context, cancellationToken)

**Purpose:** Decide how the runtime signs in.

**Algorithm:**

1. The requested host is `options.GitHubHost`, else `COPILOT_GH_HOST` from the host's environment
   (`ReadEnvironmentHost`): a blank value is unset, and a value `TryNormalizeHost` rejects — the
   same validation as the option's — refuses the start with `InvalidEnvironmentHostMessage`, an
   `InvalidOperationException` naming the variable, the accepted forms and the remedies (correct it,
   unset it for `github.com`, or pass `gitHubHost`, which takes precedence) without quoting the value,
   which may carry a user name and password. When the option is given, the environment's value is
   not read at all. The requested host, from either, becomes the plan's `HostName` as a bare
   lower-case name on every path but the Copilot CLI's (which uses its account's host, the same host
   when one was requested), so the runtime's `COPILOT_GH_HOST` is always exactly the host the token
   was resolved for. One `CopilotSignInSecrets` is created and seeded with the value of
   each `SecretVariables` variable that is set and with `options.GitHubToken`; every plan returned
   carries it as `Secrets`.
2. A host-supplied token: plan with that token and source `GitHubToken`; every later step reads "not
   tried: a token from the host takes precedence".
3. A token provider: ask it (`AskProviderAsync`) for the requested host, else `DefaultHost`; record
   its token as a secret; plan with its token, source `GitHubTokenProvider` and the provider for
   sessions.
4. When the host allows the Copilot CLI sign-in, look the account up (`LookUpCopilotCliAccount`), so
   later steps can name it.
5. Any non-blank token variable: plan with no token and source `EnvironmentVariable`; the Copilot CLI
   step names the variable that took precedence and the account it found.
6. The Copilot CLI sign-in: skipped with a finding when there is no account or when another host was
   requested; otherwise `ReadCopilotCliTokenAsync`. With a token, record it as a secret and plan with
   it, source `CopilotCli` and the account's bare host name.
7. Otherwise plan with no token, the requested host name, and a finding saying whether
   the GitHub CLI is on `PATH` (`CopilotCli.FindOnPath`, nothing is run).

**Postconditions:** The process environment and the user's Copilot CLI home are unchanged; any helper
home created is deleted.

#### LookUpCopilotCliAccount / ReadCopilotCliAccount

**Purpose:** Find the account the user last signed in to the Copilot CLI with — its host and login
only.

**Algorithm:** The home is `COPILOT_HOME` from the host's environment (made full), else `.copilot` in
the profile. A `COPILOT_HOME` the platform cannot make full — `Path.GetFullPath` throws
`ArgumentException`, `NotSupportedException`, `PathTooLongException` or `SecurityException`, for
example for a value holding a null character — is a `Problem` naming the variable and the exception
type, not the value, and the next source applies. A home whose full path equals the data directory's
(compared case-insensitively on Windows) is not read. `config.json` is read when it
exists and is at most `ConfigurationSizeLimit` bytes, parsed with comments and trailing commas
allowed; a file the platform cannot open (an I/O, access, argument or not-supported failure) is a
`Problem` too. `lastLoggedInUser` is the account; when it is missing, a `loggedInUsers` array of exactly one
entry is. The host must normalize (`TryNormalizeHost`) and the login must be non-blank, at most
`MaximumLoginLength` characters, with no control characters. Every other key is ignored. Each failure
is a `Problem` naming the file.

**Known limitation.** Links are not resolved when the homes are compared: a `COPILOT_HOME` that is a
symbolic link or junction to the data directory is not recognized as the data directory, so its
`config.json` would be read. The link-confinement follow-up tracks this.

#### ReadCopilotCliTokenAsync(account, hostEnvironment, cliPath, context, secrets, logger, cancellationToken)

**Purpose:** Obtain the account's token through the Copilot CLI itself.

**Algorithm:**

1. Create the temporary home; write `config.json` with exactly
   `{"loggedInUsers":[{"host":H,"login":L}],"lastLoggedInUser":{"host":H,"login":L}}`
   (`BuildHelperConfiguration`) and an empty `gh` folder.
2. Build the helper's environment (`BuildHelperEnvironment`): the confined runtime's environment
   (the host's `COPILOT_` configuration withheld), minus `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`,
   `GITHUB_TOKEN`, `GH_ENTERPRISE_TOKEN`, `GITHUB_ENTERPRISE_TOKEN`, `COPILOT_GH_HOST` and `GH_HOST`,
   plus `GH_CONFIG_DIR` naming the empty folder. `PATH` keeps only rooted folders that hold no GitHub
   CLI, because an empty `GH_CONFIG_DIR` does not hide the GitHub CLI's sign-in where it keeps its
   token in the system credential store (observed on Windows), and a GitHub CLI account in the helper
   makes the SDK's account list fail (see *Microsoft.Copilot.SDK Design*). A folder that also holds
   `node` is kept when the CLI is a `.js` script, which needs it.
3. Build the helper's options (`BuildHelperOptions`): `CopilotClientMode.CodingAgent`, the same
   executable spawned over stdio on the temporary home, and nothing else — no token, no handler.
4. Run the helper under a linked time limit: start it; read its status; continue only when it is
   authenticated through a stored sign-in (`authType` `user`) as exactly the account's host and login
   (login compared case-insensitively). Only then read the account list, guarded, and take the token
   of the `AuthInfoUser` entry with the account's host and login. The helper's status message and
   any exception message it quotes are redacted with the resolution's `secrets` (the known values,
   then token shapes).
5. Always dispose the helper, if one was created (stopping its process; the SDK bounds the stop),
   then delete the temporary home by its exact path (`DeleteHelperHome`, retrying briefly while a
   stopped process releases its files); a home that cannot be deleted is logged as a warning naming
   the path.

**Postconditions:** A token, or none with a problem. Never throws but for the caller's cancellation.

#### AskProviderAsync and CreateSessionTokenProvider

**Purpose:** Ask the host's token provider at start, and give each session a provider that asks it.

**Algorithm:** At start, call the provider with `CopilotGitHubTokenRequest(host, isRefresh: false)`.
An `OperationCanceledException` propagates; any other failure becomes an `InvalidOperationException`
naming the provider with the failure as cause; a `null` result becomes an `InvalidOperationException`.
For sessions, return an SDK `GitHub.TokenProvider` that maps the SDK's arguments to a request (the
host normalized, `IsRefresh` when the reason is `Refresh`) and the result to the SDK's token, with
`ExpiresIn` the token's lifetime, or `DefaultTokenLifetime`, in whole seconds rounded up (so a
lifetime of more than one hour is never reported as 3600 seconds). Each token it returns is recorded
in the plan's `Secrets`. The SDK refuses a session whose provider
fails rather than falling back to another sign-in.

#### ExplainUnauthenticated(CopilotSignInPlan plan, GetAuthStatusResponse authentication)

**Purpose:** Explain a runtime that no source signed in.

**Algorithm:** The runtime's status message, redacted with `plan.Redact`; then the four steps,
numbered, each with its finding; then, when a token was handed over and not accepted, which step's,
with the host hint when no host was chosen for a host-supplied token, or, for an environment token,
how to move on; then the remedies: `copilot login` (with `--host` for GitHub Enterprise Cloud),
`gh auth login`, `gitHubToken` or `gitHubTokenProvider` with `gitHubHost`, and that GitHub refuses a
GitHub Enterprise Cloud token used without its host with 401 Bad credentials. The whole composed text
is redacted with `plan.Redact` before it is returned, so no token the plan knows, of any shape,
appears.

#### Classify(string? authType, CopilotSignInPlan plan) and Report(logger, plan, source, authentication)

**Purpose:** Name the source the runtime used, and log it.

**Algorithm:** `token` is the plan's source when the plan handed a token over, else `Unknown`;
`token-provider` is `GitHubTokenProvider`; `env` is `EnvironmentVariable`; `user` is `DataDirectory`;
`gh-cli` is `GitHubCli`; anything else is `Unknown`. `Report` logs the source, host and login at
information level and, when a Copilot CLI account was found but the runtime signed in on another
account through another source, a warning naming both and how to choose.

#### NormalizeHost / TryNormalizeHost / HostName / SameHost

**Purpose:** One form for every host.

**Algorithm:** Trim; a bare name becomes `https://name`; a URL must use https; one trailing slash is
allowed; a path, query, fragment, user name, port or backslash is refused; the name must be a DNS host
name; the result is lower case. `HostName` strips the scheme. `NormalizeHost` throws
`ArgumentException` naming the option, without quoting the value, which may carry a user name and
password.

#### Redact(plan, text) and RedactFailure(failure, plan)

**Purpose:** Redact the runtime or SDK text that other units quote, and copy a failure they keep as a
cause.

**Algorithm:** `Redact(plan, text)` returns an empty string for a `null` text, `plan.Redact(text)`
when there is a plan, and the token-shape `Redact(text)` otherwise (a client built outside
`StartAsync`, such as a test's). `RedactFailure(failure, plan)` builds the `CopilotRedactedException`
copy of `failure` and its chain, redacting each message and stack trace with `Redact(plan, text)`.

**Known limitation.** Exact-value redaction cannot recognize a token the runtime transformed, for
example by URL-encoding or truncating it; the token-shape pattern still applies to such text.

#### RedactRelayed(failure, plan)

**Purpose:** Build the exception AgentKit relays to a host in place of a runtime or SDK failure, so no
exception that reaches a host holds the original or a token.

**Algorithm:** The first rule that matches applies:

1. A failure whose inner exception is already a `CopilotRedactedException` is returned unchanged. Only
   AgentKit builds that type, and always with the outer message already redacted — the start wrap,
   `ExplainListFailure`, or an earlier relay — so relaying twice changes nothing.
2. An `OperationCanceledException` (including `TaskCanceledException`) or an `ObjectDisposedException` with no inner
   exception and nothing in its message to redact is returned unchanged, keeping its type, `CancellationToken` and
   `ObjectName`. Otherwise it is rebuilt as a `TaskCanceledException` for a `TaskCanceledException`, an
   `OperationCanceledException` for any other cancellation (both with the same token), or an `ObjectDisposedException`,
   with the redacted message and `RedactFailure(failure, plan)` as its cause. Disposal is tested before rule 3 because
   `ObjectDisposedException` derives from `InvalidOperationException`.
3. An `InvalidOperationException` becomes a new `InvalidOperationException`.
4. An `IOException`, including a subclass, becomes a new `IOException`.
5. A `TimeoutException` becomes a new `TimeoutException`.
6. Anything else — such as the SDK's RPC error `Microsoft.Copilot.RemoteRpcException`, a `JsonException`
   or an `ArgumentException` — becomes a new `InvalidOperationException`.

Under rules 3 to 6 the message is `Redact(plan, failure.Message)` and the cause is
`RedactFailure(failure, plan)`, which names the original type. The original is never kept, whether or
not a secret was spotted, so properties AgentKit cannot inspect, such as an RPC error's data, never
pass through. A caller rethrows with `throw;` when the result is the failure itself, keeping its
stack, and throws the result otherwise. Keeping the kind means a host still catches what it caught
before; a type outside the base class library becomes `InvalidOperationException` because a host
cannot catch a type it does not reference.

**Postconditions:** The exception to throw; it never throws itself.

#### RelayAsync&lt;T&gt;(operation, plan)

**Purpose:** Run one runtime or SDK operation, such as opening a session, so its failure reaches the
caller only as `RedactRelayed` builds it.

**Algorithm:** Invoke the operation and await its task inside one guard, so a failure thrown
synchronously and one the task carries are treated alike. On a failure, call
`RedactRelayed(failure, plan)`: rethrow with `throw;` when it returns the failure itself, and throw
the result otherwise.

**Postconditions:** The operation's result, or a relayed failure.

### Error Handling

| Condition | Handling |
| --- | --- |
| Token provider fails or returns no token | `InvalidOperationException` naming the provider; failure as cause |
| `COPILOT_GH_HOST` invalid, no `gitHubHost` | `InvalidOperationException` naming the variable, not its value |
| Provider or caller canceled | `OperationCanceledException` propagates; helper stopped, home deleted |
| `COPILOT_HOME` not a valid path | Finding naming the variable, not its value; next source |
| No, unreadable, oversized or malformed Copilot CLI configuration; no or invalid account | Finding; next source |
| Copilot CLI account on another host than the one requested | Finding; next source |
| Helper home cannot be created or written | Finding; next source |
| Helper cannot be created or fails to start, or does not answer within the time limit | Finding; next source |
| Helper not signed in, or signed in as another account or kind | Finding; account list not read; next source |
| Account list cannot be read (SDK defect) | Finding; next source |
| No token for the account | Finding; next source |
| Helper home cannot be deleted | Warning logged naming the path; the start goes on |

Every message and finding AgentKit composes from runtime, SDK or exception text is redacted with the
plan's secrets and the token-shape pattern. A runtime or SDK failure AgentKit keeps as an inner
exception is a `CopilotRedactedException` copy (`RedactFailure`), never the original, and a runtime or
SDK failure AgentKit relays to a host is a new exception of the same kind built by `RedactRelayed`,
so no exception in the chain carries a token. Only the host provider's own failure is kept
unchanged as the cause of the provider refusal: the host's own code raised it, AgentKit quotes none
of it, and keeping it lets the host recognize its own failure type.

### Dependencies

- **CopilotRuntime** — the options, the launch, and the confined runtime's environment the helper's is
  built from; see *CopilotRuntime Unit Design*.
- **CopilotCli** — the `PATH` search rules, to find the GitHub CLI and `node`; see *CopilotCli Unit
  Design*.
- **Microsoft.Copilot.SDK** — the helper's client, its status and account-list reads, the session
  token provider types; see *Microsoft.Copilot.SDK Design*.

### Callers

`CopilotRuntime.StartAsync` calls `ResolveAsync`, then `Report`; `CopilotRuntime.VerifyStartedAsync`
calls `ExplainUnauthenticated`; the `CopilotRuntime` constructor calls `Classify` and
`CreateSessionTokenProvider`, passing the plan's `Secrets`; `CopilotRuntime.ExplainStartFailure`, on
`StartAsync`'s path and on the turn channel's discovery path, calls `Redact(plan, text)`, and
`CopilotRuntime.StartClientAsync` calls `RedactFailure` for the cause and `RedactRelayed` for a cancellation;
`CopilotRuntime.RelayDeleteSessionAsync` calls `RelayAsync` to delete a session; `CopilotModelCatalog`'s
`ExplainListFailure` calls `RedactFailure`; `CopilotAgent` (a session error) and
`CopilotProviderSession` (its refusals) call `Redact(plan, text)`; `CopilotAgent`,
`CopilotProviderSession` and `CopilotSummarizer` call `RedactRelayed` for a turn's failure;
`CopilotAgent`, `CopilotProviderSessionFactory` and `CopilotSummarizer` call `RelayAsync` to open a
session; `CopilotModelCatalog`'s `ExplainReadFailure` calls `RedactRelayed` for any other listing
failure; `CopilotRuntimeOptions` calls `NormalizeHost`.

### Untested Platforms

The helper has been verified live on Windows with the installed Copilot CLI. It is untested on macOS
(where reading the CLI's keychain item from the bundled runtime may prompt the user; the time limit
bounds a prompt nobody answers) and on Linux (where without a Secret Service the Copilot CLI may store
its token in plain text in its own home, which AgentKit deliberately does not read). It is untested
with the bundled runtime as the helper. Wherever it does not work, the step records why and the next
source applies.
