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
  to leave the environment's value.
- **`RequestedHost`** (`string?`) — the host the host chose (`gitHubHost`, else `COPILOT_GH_HOST`),
  normalized, or `null`.
- **`Provider`** — the host's token provider, which sessions ask, or `null`.
- **`CliAccount`** (`CopilotCliAccount?`) — the Copilot CLI account found, for the findings and the
  account warning.
- **`HostSuppliedFinding`**, **`EnvironmentFinding`**, **`CopilotCliFinding`**,
  **`FallbackFinding`** (`string`) — what each step found, in words that name hosts, logins, files and
  variable names only.
- **`ToString()`** — source, host name and `<redacted>` or `none` for the token.

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
a GitHub user token), `TokenVariables` (`COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`).

### Key Methods

#### ResolveAsync(options, dataDirectory, hostEnvironment, launch, context, cancellationToken)

**Purpose:** Decide how the runtime signs in.

**Algorithm:**

1. The requested host is `options.GitHubHost`, else a valid `COPILOT_GH_HOST` from the host's
   environment. Only `options.GitHubHost` overrides the runtime's `COPILOT_GH_HOST`; the environment's
   value already passes through.
2. A host-supplied token: plan with that token and source `GitHubToken`; every later step reads "not
   tried: a token from the host takes precedence".
3. A token provider: ask it (`AskProviderAsync`) for the requested host, else `DefaultHost`; plan with
   its token, source `GitHubTokenProvider` and the provider for sessions.
4. When the host allows the Copilot CLI sign-in, look the account up (`LookUpCopilotCliAccount`), so
   later steps can name it.
5. Any non-blank token variable: plan with no token and source `EnvironmentVariable`; the Copilot CLI
   step names the variable that took precedence and the account it found.
6. The Copilot CLI sign-in: skipped with a finding when there is no account or when another host was
   requested; otherwise `ReadCopilotCliTokenAsync`. With a token, plan with it, source `CopilotCli` and
   the account's bare host name.
7. Otherwise plan with no token, the requested host name from the option, and a finding saying whether
   the GitHub CLI is on `PATH` (`CopilotCli.FindOnPath`, nothing is run).

**Postconditions:** The process environment and the user's Copilot CLI home are unchanged; any helper
home created is deleted.

#### LookUpCopilotCliAccount / ReadCopilotCliAccount

**Purpose:** Find the account the user last signed in to the Copilot CLI with — its host and login
only.

**Algorithm:** The home is `COPILOT_HOME` from the host's environment (made full), else `.copilot` in
the profile. A home equal to AgentKit's data directory is not read. `config.json` is read when it
exists and is at most `ConfigurationSizeLimit` bytes, parsed with comments and trailing commas
allowed. `lastLoggedInUser` is the account; when it is missing, a `loggedInUsers` array of exactly one
entry is. The host must normalize (`TryNormalizeHost`) and the login must be non-blank, at most
`MaximumLoginLength` characters, with no control characters. Every other key is ignored. Each failure
is a `Problem` naming the file.

#### ReadCopilotCliTokenAsync(account, hostEnvironment, cliPath, context, logger, cancellationToken)

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
   of the `AuthInfoUser` entry with the account's host and login.
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
`ExpiresIn` the token's lifetime or `DefaultTokenLifetime`. The SDK refuses a session whose provider
fails rather than falling back to another sign-in.

#### ExplainUnauthenticated(CopilotSignInPlan plan, GetAuthStatusResponse authentication)

**Purpose:** Explain a runtime that no source signed in.

**Algorithm:** The runtime's status message (token-shaped text redacted); then the four steps,
numbered, each with its finding; then, when a token was handed over and not accepted, which step's,
with the host hint when no host was chosen for a host-supplied token, or, for an environment token,
how to move on; then the remedies: `copilot login` (with `--host` for GitHub Enterprise Cloud),
`gh auth login`, `gitHubToken` or `gitHubTokenProvider` with `gitHubHost`, and that GitHub refuses a
GitHub Enterprise Cloud token used without its host with 401 Bad credentials. No token appears.

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
`ArgumentException` naming the option.

### Error Handling

| Condition | Handling |
| --- | --- |
| Token provider fails or returns no token | `InvalidOperationException` naming the provider; failure as cause |
| Provider or caller canceled | `OperationCanceledException` propagates; helper stopped, home deleted |
| No, unreadable, oversized or malformed Copilot CLI configuration; no or invalid account | Finding; next source |
| Copilot CLI account on another host than the one requested | Finding; next source |
| Helper home cannot be created or written | Finding; next source |
| Helper cannot be created or fails to start, or does not answer within the time limit | Finding; next source |
| Helper not signed in, or signed in as another account or kind | Finding; account list not read; next source |
| Account list cannot be read (SDK defect) | Finding; next source |
| No token for the account | Finding; next source |
| Helper home cannot be deleted | Warning logged naming the path; the start goes on |

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
`CreateSessionTokenProvider`; `CopilotRuntimeOptions` calls `NormalizeHost`.

### Untested Platforms

The helper has been verified live on Windows with the installed Copilot CLI. It is untested on macOS
(where reading the CLI's keychain item from the bundled runtime may prompt the user; the time limit
bounds a prompt nobody answers) and on Linux (where without a Secret Service the Copilot CLI may store
its token in plain text in its own home, which AgentKit deliberately does not read). It is untested
with the bundled runtime as the helper. Wherever it does not work, the step records why and the next
source applies.
