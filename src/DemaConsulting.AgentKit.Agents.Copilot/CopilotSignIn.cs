using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Copilot;
using Microsoft.Copilot.Rpc;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentKit.Agents.Copilot;

/// <summary>
///     Decides how the Copilot runtime AgentKit starts signs in to GitHub, and against which host.
/// </summary>
/// <remarks>
///     <para>
///     <b>The sources, in order.</b> The first that applies wins:
///     </para>
///     <list type="number">
///       <item><description>
///         The host's <see cref="CopilotRuntimeOptions.GitHubToken"/> or
///         <see cref="CopilotRuntimeOptions.GitHubTokenProvider"/>, asked once for the effective host.
///       </description></item>
///       <item><description>
///         A token in <c>COPILOT_GITHUB_TOKEN</c>, <c>GH_TOKEN</c> or <c>GITHUB_TOKEN</c>, which the
///         runtime reads itself. This comes before the Copilot CLI sign-in because the Copilot CLI and
///         the GitHub CLI both put these variables ahead of their stored sign-in, and because a
///         variable is a deliberate, per-process choice (CI, a container, a service account) that an
///         ambient per-user sign-in must not silently override.
///       </description></item>
///       <item><description>
///         The user's Copilot CLI sign-in, unless the host turned it off: the account's host and login
///         are read from the CLI's <c>config.json</c>, and its token is obtained through the Copilot CLI
///         itself (see below).
///       </description></item>
///       <item><description>
///         The runtime's own fallbacks: a sign-in stored in AgentKit's data directory, then the GitHub
///         CLI.
///       </description></item>
///     </list>
///     <para>
///     <b>The host stays with the source.</b> A token or token provider is used with the host the
///     host chose (<see cref="CopilotRuntimeOptions.GitHubHost"/>, else <c>COPILOT_GH_HOST</c>, else
///     <c>github.com</c>). The Copilot CLI sign-in is used with its account's host, and is skipped when
///     the host chose a different one. <c>COPILOT_GH_HOST</c> is given to the runtime as a bare host
///     name, the form the runtime reads — the environment's own value too, normalized, so the token
///     and the runtime's endpoint always agree. A <c>COPILOT_GH_HOST</c> that is not a valid host is
///     refused, unless the host passed <see cref="CopilotRuntimeOptions.GitHubHost"/>, which takes
///     precedence over it.
///     </para>
///     <para>
///     <b>Why a helper runtime reads the Copilot CLI's token.</b> The Copilot CLI keeps its token in
///     the system credential store, and the confined runtime cannot read it: the SDK forces the
///     keychain off for a runtime in <c>CopilotClientMode.Empty</c>, after the caller's environment,
///     so no supported option turns it back on. So AgentKit starts a short-lived second runtime — the
///     same executable, in <c>CopilotClientMode.CodingAgent</c>, the only mode that reads the store —
///     on a new temporary home that holds nothing but the account's host and login, asks it for that
///     account's token, and stops it. It never opens a session, so no MCP server, hook, agent, skill,
///     plugin or instruction can load, and the temporary home holds none. The token is then handed to
///     the confined runtime as its GitHub token; the confined runtime's data directory, mode and
///     environment are unchanged. AgentKit itself never reads the credential store, so the same code
///     works wherever the Copilot CLI does.
///     </para>
///     <para>
///     <b>Secrets.</b> A token is kept in memory and passed to the runtime only. Findings, log
///     entries, exception messages and <see cref="CopilotSignInPlan.ToString"/> name hosts, logins,
///     files and variable names, never a token. Runtime, SDK and exception text that AgentKit quotes
///     has every exact token it was given or obtained removed — the host's token, the host's
///     provider's tokens at start and for each session, the Copilot CLI's token, and the values of the
///     environment's token variables — whatever their shape, and then any text shaped like a GitHub
///     token. A runtime or SDK failure AgentKit keeps as an inner exception is a redacted copy
///     (<see cref="CopilotRedactedException"/>): its type name, message and stack trace, each
///     redacted, with every chained exception copied the same way and nothing else of the original.
///     Only the host's own token provider's exception is kept unchanged: the host's code raised it,
///     AgentKit quotes none of it, and the host can then recognize its own failure.
///     </para>
/// </remarks>
internal static partial class CopilotSignIn
{
    /// <summary>
    ///     The host the runtime signs in to when none is chosen.
    /// </summary>
    internal const string DefaultHost = "https://github.com";

    /// <summary>
    ///     The largest Copilot CLI <c>config.json</c> AgentKit reads.
    /// </summary>
    internal const int ConfigurationSizeLimit = 1024 * 1024;

    /// <summary>
    ///     The longest login accepted from the Copilot CLI's configuration.
    /// </summary>
    internal const int MaximumLoginLength = 100;

    /// <summary>
    ///     The prefix of the temporary home the helper runtime runs on.
    /// </summary>
    internal const string HelperHomePrefix = "agentkit-copilot-signin-";

    /// <summary>
    ///     The scheme every normalized host carries.
    /// </summary>
    private const string HttpsPrefix = "https://";

    /// <summary>
    ///     The heading of the runtime fallbacks' step.
    /// </summary>
    private const string FallbackHeading =
        "Runtime fallbacks (a sign-in stored in AgentKit's data directory, then the GitHub CLI)";

    /// <summary>
    ///     The refusal of a <c>COPILOT_GH_HOST</c> that is not a valid GitHub host. It does not quote
    ///     the value, which may carry a user name and password.
    /// </summary>
    internal const string InvalidEnvironmentHostMessage =
        "The COPILOT_GH_HOST environment variable is not a valid GitHub host, so the Copilot runtime was not "
        + "started. Set it to a host name such as 'example.ghe.com', or an https URL such as "
        + "'https://example.ghe.com', without a path, query, user name or port; unset it to sign in to "
        + "github.com; or pass CopilotRuntimeOptions' gitHubHost, which takes precedence over it.";

    /// <summary>
    ///     The environment variables a GitHub token can come from, in the runtime's order.
    /// </summary>
    internal static readonly string[] TokenVariables = ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN"];

    /// <summary>
    ///     The environment variables whose values are secrets, redacted from any text AgentKit quotes:
    ///     the token variables and the GitHub Enterprise token variables.
    /// </summary>
    private static readonly string[] SecretVariables =
        ["COPILOT_GITHUB_TOKEN", "GH_TOKEN", "GITHUB_TOKEN", "GH_ENTERPRISE_TOKEN", "GITHUB_ENTERPRISE_TOKEN"];

    /// <summary>
    ///     The variables withheld from the helper runtime, beyond those the confined runtime's
    ///     environment already withholds.
    /// </summary>
    /// <remarks>
    ///     Every token variable, so the helper cannot sign in from the environment, and the host
    ///     variables, so it reads the stored account on its own host.
    /// </remarks>
    private static readonly string[] HelperWithheldVariables =
    [
        "COPILOT_GITHUB_TOKEN",
        "GH_TOKEN",
        "GITHUB_TOKEN",
        "GH_ENTERPRISE_TOKEN",
        "GITHUB_ENTERPRISE_TOKEN",
        "COPILOT_GH_HOST",
        "GH_HOST",
    ];

    /// <summary>
    ///     Logs which source the runtime signed in with.
    /// </summary>
    private static readonly Action<ILogger, CopilotSignInSource, string, string, Exception?> SignedIn =
        LoggerMessage.Define<CopilotSignInSource, string, string>(
            LogLevel.Information,
            new EventId(1, "CopilotSignedIn"),
            "The Copilot runtime signed in via {Source} as {Host} / {Login}.");

    /// <summary>
    ///     Logs a runtime that signed in on another account than the Copilot CLI's.
    /// </summary>
    private static readonly Action<ILogger, string, string, CopilotSignInSource, string, string, Exception?> OtherAccount =
        LoggerMessage.Define<string, string, CopilotSignInSource, string, string>(
            LogLevel.Warning,
            new EventId(2, "CopilotSignedInAsAnotherAccount"),
            "The Copilot runtime signed in to {Host} as {Login} via {Source}, but the Copilot CLI is signed in to "
            + "{CliHost} as {CliLogin}. To choose the account, pass CopilotRuntimeOptions' gitHubHost, or run "
            + "'copilot login --host <host>'.");

    /// <summary>
    ///     Logs why a Copilot CLI account that was found was not used.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> CopilotCliNotUsed =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(3, "CopilotCliSignInNotUsed"),
            "The Copilot CLI sign-in was not used: {Finding}");

    /// <summary>
    ///     Logs a temporary home that could not be deleted.
    /// </summary>
    private static readonly Action<ILogger, string, Exception?> HelperHomeLeft =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, "CopilotSignInHomeNotDeleted"),
            "AgentKit could not delete the temporary Copilot sign-in folder '{Path}'. It holds the Copilot CLI "
            + "account's host and login and the helper runtime's logs; delete it by hand.");

    /// <summary>
    ///     Gets how long the helper runtime may take to start and report the token.
    /// </summary>
    /// <remarks>
    ///     Long enough for a first start that unpacks the CLI and for a credential-store prompt the user
    ///     answers; short enough that a prompt nobody answers does not hold the start forever.
    /// </remarks>
    internal static TimeSpan HelperTimeout { get; } = TimeSpan.FromSeconds(60);

    /// <summary>
    ///     Gets the lifetime AgentKit reports for a provider's token whose lifetime is not known: the
    ///     usual lifetime of a GitHub user token.
    /// </summary>
    internal static TimeSpan DefaultTokenLifetime { get; } = TimeSpan.FromHours(8);

    /// <summary>
    ///     Normalizes a GitHub host a host passed as an option.
    /// </summary>
    /// <param name="host">The host: a name such as <c>example.ghe.com</c>, or an https URL without a path.</param>
    /// <param name="parameterName">The option's parameter name, for the exception.</param>
    /// <returns>The host as <c>https://name</c> in lower case, or <see langword="null"/> when blank.</returns>
    /// <exception cref="ArgumentException">The host is not an https host name or URL without a path, query, fragment, user name or port.</exception>
    internal static string? NormalizeHost(string? host, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return null;
        }

        return TryNormalizeHost(host, out var normalized)
            ? normalized
            : throw new ArgumentException(
                $"The GitHub host '{host}' is not valid. Pass a host name such as 'example.ghe.com', or an https URL "
                + "such as 'https://example.ghe.com', without a path, query, user name or port.",
                parameterName);
    }

    /// <summary>
    ///     Normalizes a GitHub host, without throwing.
    /// </summary>
    /// <remarks>
    ///     A bare name must be a DNS host name; a URL must be https with nothing but the host (a single
    ///     trailing slash is allowed). The result is lower case, so hosts compare ordinally.
    /// </remarks>
    /// <param name="host">The host.</param>
    /// <param name="normalized">The host as <c>https://name</c>, or <see langword="null"/> when it is not valid.</param>
    /// <returns><see langword="true"/> when the host is valid.</returns>
    internal static bool TryNormalizeHost(string? host, out string? normalized)
    {
        normalized = null;
        var text = host?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        // A bare host name becomes an https URL; anything else must already be one
        var schemeEnd = text.IndexOf("://", StringComparison.Ordinal);
        var authority = schemeEnd < 0 ? text : text[(schemeEnd + 3)..];
        if (schemeEnd >= 0 && !text.StartsWith(HttpsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Only the host, optionally followed by one slash: no path, query, fragment, user name or port
        if (authority.EndsWith('/'))
        {
            authority = authority[..^1];
        }

        if (authority.Length == 0
            || authority.IndexOfAny(['/', '?', '#', '@', ':', '\\']) >= 0
            || Uri.CheckHostName(authority) != UriHostNameType.Dns)
        {
            return false;
        }

        normalized = HttpsPrefix + authority.ToLowerInvariant();
        return true;
    }

    /// <summary>
    ///     Gets the bare host name of a normalized host, the form the runtime reads from
    ///     <c>COPILOT_GH_HOST</c>.
    /// </summary>
    /// <param name="normalizedHost">A host <see cref="TryNormalizeHost"/> produced.</param>
    /// <returns>The host name, such as <c>example.ghe.com</c>.</returns>
    internal static string HostName(string normalizedHost) => normalizedHost[HttpsPrefix.Length..];

    /// <summary>
    ///     Determines whether two hosts, in any accepted form, are the same.
    /// </summary>
    /// <param name="first">One host.</param>
    /// <param name="second">The other host.</param>
    /// <returns><see langword="true"/> when both are valid and equal.</returns>
    internal static bool SameHost(string? first, string? second) =>
        TryNormalizeHost(first, out var a) && TryNormalizeHost(second, out var b) && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>
    ///     Resolves how the runtime signs in.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Follows the source order and host rules in the class remarks. Every step that does not
    ///     apply leaves a finding, so a runtime that still is not signed in can be explained.
    ///     </para>
    ///     <para>
    ///     Side effects: may call the host's token provider once; may read the Copilot CLI's
    ///     <c>config.json</c>; may create, run and delete the helper runtime's temporary home. Never
    ///     changes the process environment or the user's Copilot CLI home.
    ///     </para>
    /// </remarks>
    /// <param name="options">The host's options.</param>
    /// <param name="dataDirectory">The prepared data directory's full path.</param>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="launch">What the confined runtime will run; the helper runs the same executable.</param>
    /// <param name="context">The machine the resolution reads and the helper it starts.</param>
    /// <param name="cancellationToken">Cancels the resolution.</param>
    /// <returns>The sign-in plan.</returns>
    /// <exception cref="InvalidOperationException">
    ///     The host's token provider failed or returned no token; or <c>COPILOT_GH_HOST</c> is set to
    ///     something that is not a valid GitHub host and no <see cref="CopilotRuntimeOptions.GitHubHost"/>
    ///     was given.
    /// </exception>
    /// <exception cref="OperationCanceledException">The resolution was canceled.</exception>
    internal static async Task<CopilotSignInPlan> ResolveAsync(
        CopilotRuntimeOptions options,
        string dataDirectory,
        IDictionary hostEnvironment,
        CopilotRuntimeLaunch launch,
        CopilotSignInContext context,
        CancellationToken cancellationToken)
    {
        // The host the host chose, if any: its option, else the environment's COPILOT_GH_HOST, which
        // must then be a valid host. Either is handed to the runtime as a bare name, so the token and
        // the runtime's endpoint always agree; the option, when given, overrides the environment's.
        var requestedHost = options.GitHubHost ?? ReadEnvironmentHost(hostEnvironment);
        var overrideHostName = requestedHost is null ? null : HostName(requestedHost);

        // Every token this resolution is given or obtains, so text it quotes can never carry one
        var secrets = new CopilotSignInSecrets();
        foreach (var name in SecretVariables)
        {
            secrets.Add(ReadVariable(hostEnvironment, name));
        }

        secrets.Add(options.GitHubToken);

        // Step 1: a token or token provider the host supplies
        if (options.GitHubToken is not null)
        {
            return HostSupplied(
                CopilotSignInSource.GitHubToken,
                options.GitHubToken,
                overrideHostName,
                requestedHost,
                provider: null,
                secrets,
                $"gitHubToken supplied; AgentKit passed it to the runtime (host {requestedHost ?? DefaultHost}).");
        }

        if (options.GitHubTokenProvider is { } provider)
        {
            var host = requestedHost ?? DefaultHost;
            var token = await AskProviderAsync(provider, host, cancellationToken).ConfigureAwait(false);
            secrets.Add(token);
            return HostSupplied(
                CopilotSignInSource.GitHubTokenProvider,
                token,
                overrideHostName,
                requestedHost,
                provider,
                secrets,
                $"gitHubTokenProvider supplied; it returned a token for {host}, which AgentKit passed to the runtime.");
        }

        // The Copilot CLI account is looked up whenever the host allows it, so later steps can name it
        var cliLookup = options.UseCopilotCliSignIn
            ? LookUpCopilotCliAccount(hostEnvironment, context.UserProfile, dataDirectory)
            : null;

        // Step 2: a token in the environment, which the runtime reads itself
        var setVariables = TokenVariables.Where(name => !string.IsNullOrWhiteSpace(ReadVariable(hostEnvironment, name))).ToList();
        if (setVariables.Count > 0)
        {
            var precedence = $"not tried: {string.Join(", ", setVariables)} in the environment takes precedence";
            return new CopilotSignInPlan
            {
                Source = CopilotSignInSource.EnvironmentVariable,
                HostName = overrideHostName,
                RequestedHost = requestedHost,
                CliAccount = cliLookup?.Account,
                HostSuppliedFinding = "not supplied.",
                EnvironmentFinding = $"{string.Join(", ", setVariables)} set; the runtime signs in with it.",
                CopilotCliFinding = cliLookup is null
                    ? "turned off by the host (useCopilotCliSignIn: false)."
                    : $"{precedence}{DescribeFound(cliLookup)}.",
                FallbackFinding = "not tried: an earlier source applied.",
                Secrets = secrets,
            };
        }

        // Step 3: the user's Copilot CLI sign-in
        var (cliToken, cliFinding) = cliLookup is null
            ? (null, "turned off by the host (useCopilotCliSignIn: false).")
            : await TryCopilotCliAsync(cliLookup, requestedHost, hostEnvironment, launch, context, secrets, options.Logger, cancellationToken)
                .ConfigureAwait(false);
        if (cliToken is not null)
        {
            secrets.Add(cliToken);
            return new CopilotSignInPlan
            {
                Source = CopilotSignInSource.CopilotCli,
                Token = cliToken,
                HostName = HostName(cliLookup!.Account!.Host),
                RequestedHost = requestedHost,
                CliAccount = cliLookup.Account,
                HostSuppliedFinding = "not supplied.",
                EnvironmentFinding = "none set.",
                CopilotCliFinding = cliFinding,
                FallbackFinding = "not tried: an earlier source applied.",
                Secrets = secrets,
            };
        }

        if (options.Logger is { } logger && cliLookup?.Account is not null)
        {
            CopilotCliNotUsed(logger, cliFinding, null);
        }

        // Step 4: the runtime's own fallbacks
        var gh = CopilotCli.FindOnPath(context.Search, "gh");
        return new CopilotSignInPlan
        {
            Source = null,
            HostName = overrideHostName,
            RequestedHost = requestedHost,
            CliAccount = cliLookup?.Account,
            HostSuppliedFinding = "not supplied.",
            EnvironmentFinding = "none set.",
            CopilotCliFinding = cliFinding,
            FallbackFinding = gh is null
                ? $"left to the runtime; the GitHub CLI ('{context.Search.ExecutableName("gh")}') was not found on PATH."
                : $"left to the runtime; the GitHub CLI was found at '{gh}' (signed in to {requestedHost ?? DefaultHost} or not).",
            Secrets = secrets,
        };
    }

    /// <summary>
    ///     Reads the GitHub host the host's environment chose in <c>COPILOT_GH_HOST</c>, refusing one
    ///     that is not valid.
    /// </summary>
    /// <remarks>
    ///     A blank value is unset. The value is validated as <see cref="CopilotRuntimeOptions.GitHubHost"/>
    ///     is, and a rejected one is not quoted: it may carry a user name and password.
    /// </remarks>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <returns>The host, normalized, or <see langword="null"/> when none is set.</returns>
    /// <exception cref="InvalidOperationException"><c>COPILOT_GH_HOST</c> is not a valid GitHub host.</exception>
    internal static string? ReadEnvironmentHost(IDictionary hostEnvironment)
    {
        var value = ReadVariable(hostEnvironment, "COPILOT_GH_HOST");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return TryNormalizeHost(value, out var normalized)
            ? normalized
            : throw new InvalidOperationException(InvalidEnvironmentHostMessage);
    }

    /// <summary>
    ///     Builds the plan for a token the host supplied.
    /// </summary>
    /// <param name="source">The kind of host-supplied source.</param>
    /// <param name="token">The token.</param>
    /// <param name="overrideHostName">The host name to give the runtime, or <see langword="null"/> to leave its environment's.</param>
    /// <param name="requestedHost">The host the host chose, or <see langword="null"/>.</param>
    /// <param name="provider">The host's token provider, for sessions; <see langword="null"/> for a token.</param>
    /// <param name="secrets">The tokens the resolution knows, redacted from any text it quotes.</param>
    /// <param name="finding">What the step found.</param>
    /// <returns>The plan.</returns>
    private static CopilotSignInPlan HostSupplied(
        CopilotSignInSource source,
        string token,
        string? overrideHostName,
        string? requestedHost,
        Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>>? provider,
        CopilotSignInSecrets secrets,
        string finding) =>
        new()
        {
            Source = source,
            Token = token,
            HostName = overrideHostName,
            RequestedHost = requestedHost,
            Provider = provider,
            HostSuppliedFinding = finding,
            EnvironmentFinding = "not consulted: a token from the host takes precedence.",
            CopilotCliFinding = "not tried: a token from the host takes precedence.",
            FallbackFinding = "not tried: a token from the host takes precedence.",
            Secrets = secrets,
        };

    /// <summary>
    ///     Asks the host's token provider for the token the runtime starts with.
    /// </summary>
    /// <param name="provider">The host's provider.</param>
    /// <param name="host">The host the token is for.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The token.</returns>
    /// <exception cref="InvalidOperationException">The provider failed or returned no token; a failure is the inner exception.</exception>
    /// <exception cref="OperationCanceledException">The provider was canceled. It propagates unchanged.</exception>
    internal static async Task<string> AskProviderAsync(
        Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>> provider,
        string host,
        CancellationToken cancellationToken)
    {
        CopilotGitHubToken? result;
        try
        {
            result = await provider(new CopilotGitHubTokenRequest(host, isRefresh: false), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"The GitHub token provider (CopilotRuntimeOptions' gitHubTokenProvider) failed when AgentKit asked it "
                + $"for a token for {host}, so the Copilot runtime was not started. The inner exception says why.",
                failure);
        }

        return result?.Token ?? throw new InvalidOperationException(
            $"The GitHub token provider (CopilotRuntimeOptions' gitHubTokenProvider) returned no token for {host}, so "
            + "the Copilot runtime was not started. Return a CopilotGitHubToken, or throw when no token is available.");
    }

    /// <summary>
    ///     Builds the session-level token provider that asks the host's provider for each session's
    ///     tokens.
    /// </summary>
    /// <remarks>
    ///     The SDK rejects a session whose provider fails, rather than falling back to another
    ///     sign-in, so a failure is never hidden. A token of unknown lifetime is reported as
    ///     <see cref="DefaultTokenLifetime"/>. A lifetime is reported in whole seconds, rounded up, so
    ///     a lifetime of more than one hour is never reported as one hour. Each token is recorded in
    ///     <paramref name="secrets"/>, so text quoted later never carries it.
    /// </remarks>
    /// <param name="provider">The host's provider.</param>
    /// <param name="secrets">The sign-in's secrets, which each token the provider returns joins.</param>
    /// <returns>The SDK's session token provider.</returns>
    internal static Func<GitHubTokenProviderArgs, Task<GitHubTokenProviderResult>> CreateSessionTokenProvider(
        Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>> provider,
        CopilotSignInSecrets secrets) =>
        async args =>
        {
            var host = TryNormalizeHost(args.Host, out var normalized) ? normalized! : args.Host;
            var token = await provider(
                new CopilotGitHubTokenRequest(host, args.Reason == GitHubTokenRequestReason.Refresh),
                CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    $"The GitHub token provider (CopilotRuntimeOptions' gitHubTokenProvider) returned no token for {host}.");
            secrets.Add(token.Token);

            return GitHubTokenProviderResult.FromToken(new GitHubToken
            {
                AccessToken = token.Token,
                ExpiresIn = WholeSecondsRoundedUp(token.ExpiresIn ?? DefaultTokenLifetime),
            });
        };

    /// <summary>
    ///     Converts a lifetime to whole seconds, rounding a fraction of a second up.
    /// </summary>
    /// <remarks>
    ///     Rounding up keeps a lifetime of more than one hour above 3600 seconds, the runtime's
    ///     minimum; truncating would report one hour and one tick as exactly one hour. Integer
    ///     arithmetic on ticks, so the result is exact and cannot overflow.
    /// </remarks>
    /// <param name="lifetime">The lifetime.</param>
    /// <returns>The lifetime in seconds, rounded up.</returns>
    internal static long WholeSecondsRoundedUp(TimeSpan lifetime) =>
        (lifetime.Ticks / TimeSpan.TicksPerSecond) + (lifetime.Ticks % TimeSpan.TicksPerSecond > 0 ? 1 : 0);

    /// <summary>
    ///     Finds the user's Copilot CLI home: <c>COPILOT_HOME</c> from the host's environment, else
    ///     <c>.copilot</c> in the user's profile.
    /// </summary>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="userProfile">The user's profile folder, or <see langword="null"/> when unknown.</param>
    /// <returns>The home's full path, or <see langword="null"/> when it cannot be known.</returns>
    /// <exception cref="ArgumentException"><c>COPILOT_HOME</c> is not a valid path on this platform.</exception>
    /// <exception cref="NotSupportedException"><c>COPILOT_HOME</c> is not a valid path on this platform.</exception>
    /// <exception cref="PathTooLongException"><c>COPILOT_HOME</c> is longer than this platform allows.</exception>
    /// <exception cref="System.Security.SecurityException">The path cannot be made full.</exception>
    internal static string? FindCopilotHome(IDictionary hostEnvironment, string? userProfile)
    {
        var configured = ReadVariable(hostEnvironment, "COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return string.IsNullOrWhiteSpace(userProfile) ? null : Path.Combine(userProfile, ".copilot");
    }

    /// <summary>
    ///     Looks up the account the user last signed in to the Copilot CLI with.
    /// </summary>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="userProfile">The user's profile folder, or <see langword="null"/> when unknown.</param>
    /// <param name="dataDirectory">
    ///     AgentKit's data directory. A home whose full path equals it is not read; the paths are
    ///     compared as text and links are not resolved, a known limitation the link-confinement
    ///     follow-up tracks.
    /// </param>
    /// <returns>
    ///     The account and where it came from, or why there is none — including a <c>COPILOT_HOME</c>
    ///     that is not a valid path, which is named but not quoted, since it may hold anything.
    /// </returns>
    internal static CopilotCliAccountLookup LookUpCopilotCliAccount(
        IDictionary hostEnvironment,
        string? userProfile,
        string dataDirectory)
    {
        string? home;
        try
        {
            home = FindCopilotHome(hostEnvironment, userProfile);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return new CopilotCliAccountLookup(
                null,
                null,
                $"COPILOT_HOME is not a valid path ({ex.GetType().Name}), so the Copilot CLI's configuration could not be located.");
        }

        if (home is null)
        {
            return new CopilotCliAccountLookup(null, null, "the user's Copilot CLI home could not be located (no COPILOT_HOME and no user profile).");
        }

        // A home whose path is AgentKit's own data directory is not read. The paths are compared as
        // text: a link to the data directory is not resolved (tracked in the link-confinement follow-up).
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(Path.TrimEndingDirectorySeparator(home), Path.TrimEndingDirectorySeparator(dataDirectory), comparison))
        {
            return new CopilotCliAccountLookup(null, null, $"COPILOT_HOME names AgentKit's own data directory '{home}', which holds no Copilot CLI sign-in.");
        }

        return ReadCopilotCliAccount(Path.Combine(home, "config.json"));
    }

    /// <summary>
    ///     Reads the signed-in account from a Copilot CLI <c>config.json</c>: its host and login only.
    /// </summary>
    /// <remarks>
    ///     The file may carry comments and trailing commas. <c>lastLoggedInUser</c> is the account;
    ///     when it is missing, a single entry in <c>loggedInUsers</c> is. Every other key is ignored.
    ///     The host must be an https host and the login a non-empty name of at most
    ///     <see cref="MaximumLoginLength"/> characters without control characters. A file larger than
    ///     <see cref="ConfigurationSizeLimit"/> is not read.
    /// </remarks>
    /// <param name="configurationPath">The file's full path.</param>
    /// <returns>The account, or why there is none.</returns>
    internal static CopilotCliAccountLookup ReadCopilotCliAccount(string configurationPath)
    {
        byte[] content;
        try
        {
            var info = new FileInfo(configurationPath);
            if (!info.Exists)
            {
                return new CopilotCliAccountLookup(null, configurationPath, $"no Copilot CLI configuration at '{configurationPath}' (the Copilot CLI is not signed in).");
            }

            if (info.Length > ConfigurationSizeLimit)
            {
                return new CopilotCliAccountLookup(null, configurationPath, $"'{configurationPath}' is larger than {ConfigurationSizeLimit} bytes, so AgentKit did not read it.");
            }

            content = File.ReadAllBytes(configurationPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new CopilotCliAccountLookup(null, configurationPath, $"'{configurationPath}' could not be read ({ex.GetType().Name}).");
        }

        try
        {
            using var document = JsonDocument.Parse(
                content,
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return ReadAccountEntry(document.RootElement, configurationPath);
        }
        catch (JsonException)
        {
            return new CopilotCliAccountLookup(null, configurationPath, $"'{configurationPath}' is not valid JSON.");
        }
    }

    /// <summary>
    ///     Builds the content of the helper runtime's <c>config.json</c>: the account entry and nothing
    ///     else.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The UTF-8 JSON.</returns>
    internal static byte[] BuildHelperConfiguration(CopilotCliAccount account)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("loggedInUsers");
            WriteAccount(writer, account);
            writer.WriteEndArray();
            writer.WritePropertyName("lastLoggedInUser");
            WriteAccount(writer, account);
            writer.WriteEndObject();
        }

        return buffer.ToArray();
    }

    /// <summary>
    ///     Builds the helper runtime's environment.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The confined runtime's environment (the host's <c>COPILOT_</c> configuration withheld), minus
    ///     every token and host variable, plus <c>GH_CONFIG_DIR</c> naming an empty folder.
    ///     </para>
    ///     <para>
    ///     <c>PATH</c> keeps only rooted folders that hold no GitHub CLI. An empty <c>GH_CONFIG_DIR</c>
    ///     does not hide the GitHub CLI's sign-in where the GitHub CLI keeps its token in the system
    ///     credential store (observed on Windows), and a GitHub CLI account in the helper makes the
    ///     SDK's account list fail to deserialize (a defect in <c>Microsoft.Copilot.SDK</c> 2.0.1).
    ///     A folder that also holds <c>node</c> is kept when the CLI is a <c>.js</c> script, which
    ///     needs it; the GitHub CLI may then stay visible, and the step falls through with a finding.
    ///     </para>
    /// </remarks>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="gitHubCliConfiguration">The empty folder <c>GH_CONFIG_DIR</c> names.</param>
    /// <param name="search">The platform and file system, to find the GitHub CLI and <c>node</c>.</param>
    /// <param name="cliPath">The CLI the helper runs, or <see langword="null"/> for the bundled runtime.</param>
    /// <returns>The helper's environment.</returns>
    internal static Dictionary<string, string> BuildHelperEnvironment(
        IDictionary hostEnvironment,
        string gitHubCliConfiguration,
        CopilotCliSearchContext search,
        string? cliPath)
    {
        var environment = CopilotRuntime.BuildRuntimeEnvironment(hostEnvironment);
        foreach (var name in HelperWithheldVariables)
        {
            environment.Remove(name);
        }

        environment["GH_CONFIG_DIR"] = gitHubCliConfiguration;
        if (environment.TryGetValue("PATH", out var path))
        {
            var needsNode = cliPath?.EndsWith(".js", StringComparison.OrdinalIgnoreCase) == true;
            var gh = search.ExecutableName("gh");
            var node = search.ExecutableName("node");
            var kept = path
                .Split(search.PathListSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(entry => CopilotCli.IsSearchableDirectory(entry.Trim('"'), search.Platform))
                .Where(entry =>
                    !search.FileExists(Path.Combine(entry.Trim('"'), gh))
                    || (needsNode && search.FileExists(Path.Combine(entry.Trim('"'), node))));
            environment["PATH"] = string.Join(search.PathListSeparator, kept);
        }

        return environment;
    }

    /// <summary>
    ///     Builds the helper runtime's client options.
    /// </summary>
    /// <remarks>
    ///     <c>CopilotClientMode.CodingAgent</c> is the only mode in which the runtime reads the system
    ///     credential store. No token, handler or other option is set: the helper only starts, reports
    ///     its account and stops.
    /// </remarks>
    /// <param name="cliPath">The CLI to run, or <see langword="null"/> for the bundled runtime.</param>
    /// <param name="environment">The helper's environment.</param>
    /// <param name="helperHome">The helper's temporary home.</param>
    /// <returns>The client options.</returns>
    internal static CopilotClientOptions BuildHelperOptions(
        string? cliPath,
        Dictionary<string, string> environment,
        string helperHome) =>
        new()
        {
            Mode = CopilotClientMode.CodingAgent,
            Runtime = RuntimeConnection.ForSpawnedStdio(path: cliPath, environment: environment, baseDirectory: helperHome),
        };

    /// <summary>
    ///     Obtains the Copilot CLI account's token through a short-lived helper runtime.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     The helper runs on a new temporary home holding only the account entry, never opens a
    ///     session, and is stopped and its home deleted whatever happens. Its status is checked before
    ///     its account list is read — signed in through a stored sign-in (<c>user</c>) as exactly the
    ///     account — which also keeps the SDK 2.0.1 account-list defect from being reached; the read
    ///     itself is guarded all the same.
    ///     </para>
    ///     <para>
    ///     A helper that fails, times out or reports another account is a finding, never an exception:
    ///     the next source applies. Only the caller's cancellation propagates.
    ///     </para>
    /// </remarks>
    /// <param name="account">The account.</param>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="cliPath">The CLI to run, or <see langword="null"/> for the bundled runtime.</param>
    /// <param name="context">The machine and the helper factory.</param>
    /// <param name="secrets">The tokens the resolution knows, redacted from the helper's text.</param>
    /// <param name="logger">The host's logger, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The token, or <see langword="null"/> with why there is none.</returns>
    /// <exception cref="OperationCanceledException">The caller canceled.</exception>
    internal static async Task<(string? Token, string? Problem)> ReadCopilotCliTokenAsync(
        CopilotCliAccount account,
        IDictionary hostEnvironment,
        string? cliPath,
        CopilotSignInContext context,
        CopilotSignInSecrets secrets,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        // The temporary home: the account entry, and an empty folder for the GitHub CLI's configuration
        string home;
        try
        {
            home = context.CreateHelperHome();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"AgentKit could not create a temporary folder for the Copilot CLI ({ex.GetType().Name}).");
        }

        try
        {
            var gitHubCliConfiguration = Path.Combine(home, "gh");
            Directory.CreateDirectory(gitHubCliConfiguration);
            await File.WriteAllBytesAsync(Path.Combine(home, "config.json"), BuildHelperConfiguration(account), cancellationToken)
                .ConfigureAwait(false);

            var environment = BuildHelperEnvironment(hostEnvironment, gitHubCliConfiguration, context.Search, cliPath);
            return await RunHelperAsync(account, BuildHelperOptions(cliPath, environment, home), context, secrets, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, $"AgentKit could not prepare a temporary folder for the Copilot CLI ({ex.GetType().Name}).");
        }
        finally
        {
            if (!context.DeleteDirectory(home) && logger is not null)
            {
                HelperHomeLeft(logger, home, null);
            }
        }
    }

    /// <summary>
    ///     Deletes a helper home by its exact path, retrying briefly while a stopped process releases
    ///     its files.
    /// </summary>
    /// <param name="path">The helper home's full path.</param>
    /// <returns><see langword="true"/> when the folder is gone.</returns>
    internal static bool DeleteHelperHome(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A runtime that just stopped can hold its log files for a moment, on Windows
                Thread.Sleep(TimeSpan.FromMilliseconds(200));
            }
        }

        return !Directory.Exists(path);
    }

    /// <summary>
    ///     Composes the explanation for a runtime that started but is not signed in, from what each
    ///     source found.
    /// </summary>
    /// <param name="plan">The sign-in plan the runtime started with.</param>
    /// <param name="authentication">The runtime's authentication status.</param>
    /// <returns>
    ///     The explanation. It carries no token: every token the plan knows, and any text shaped like
    ///     a GitHub token, is redacted from the runtime's status message and from the whole text.
    /// </returns>
    internal static string ExplainUnauthenticated(CopilotSignInPlan plan, GetAuthStatusResponse authentication)
    {
        var status = string.IsNullOrWhiteSpace(authentication.StatusMessage)
            ? string.Empty
            : $" (the runtime reports: {plan.Redact(authentication.StatusMessage.Trim())})";
        var message = new StringBuilder()
            .Append("The Copilot runtime started but is not signed in to GitHub").Append(status)
            .Append(", so AgentKit stopped it. AgentKit tried these sign-in sources, in order:")
            .AppendLine()
            .Append("  1. gitHubToken / gitHubTokenProvider: ").Append(plan.HostSuppliedFinding).AppendLine()
            .Append("  2. ").Append(string.Join(", ", TokenVariables)).Append(": ").Append(plan.EnvironmentFinding).AppendLine()
            .Append("  3. Copilot CLI sign-in: ").Append(plan.CopilotCliFinding).AppendLine()
            .Append("  4. ").Append(FallbackHeading).Append(": ").Append(plan.FallbackFinding).AppendLine();

        // A token the runtime did not accept: say which, and the host it needs
        switch (plan.Source)
        {
            case CopilotSignInSource.GitHubToken or CopilotSignInSource.GitHubTokenProvider or CopilotSignInSource.CopilotCli:
                message.Append($"The runtime did not accept the token from step {(plan.Source == CopilotSignInSource.CopilotCli ? 3 : 1)}");
                message.Append(plan.RequestedHost is null && plan.Source != CopilotSignInSource.CopilotCli
                    ? ": a GitHub Enterprise Cloud token also needs its host (gitHubHost or COPILOT_GH_HOST). "
                    : ". ");
                break;
            case CopilotSignInSource.EnvironmentVariable:
                message.Append("The runtime did not accept the environment token. Unset it so AgentKit uses the next ")
                    .Append("source, or pass gitHubToken (with gitHubHost for a GitHub Enterprise Cloud account). ");
                break;
        }

        return plan.Redact(message
            .Append("To sign in: run 'copilot login' (add '--host https://<name>.ghe.com' for GitHub Enterprise Cloud), ")
            .Append("run 'gh auth login' with gh on PATH, or pass gitHubToken or gitHubTokenProvider, with gitHubHost for a ")
            .Append("GitHub Enterprise Cloud account. GitHub refuses a GitHub Enterprise Cloud token used without its host ")
            .Append("with 401 Bad credentials.")
            .ToString());
    }

    /// <summary>
    ///     Classifies the kind of sign-in a started runtime reports.
    /// </summary>
    /// <param name="authType">The runtime's <c>authType</c>.</param>
    /// <param name="plan">The plan the runtime started with.</param>
    /// <returns>The source; <see cref="CopilotSignInSource.Unknown"/> for a kind AgentKit does not know.</returns>
    internal static CopilotSignInSource Classify(string? authType, CopilotSignInPlan plan) => authType switch
    {
        "token" when plan.Source is CopilotSignInSource.GitHubToken or CopilotSignInSource.GitHubTokenProvider or CopilotSignInSource.CopilotCli
            => plan.Source.Value,
        "token-provider" => CopilotSignInSource.GitHubTokenProvider,
        "env" => CopilotSignInSource.EnvironmentVariable,
        "user" => CopilotSignInSource.DataDirectory,
        "gh-cli" => CopilotSignInSource.GitHubCli,
        _ => CopilotSignInSource.Unknown,
    };

    /// <summary>
    ///     Logs how a started runtime signed in, and warns when it is not the Copilot CLI's account.
    /// </summary>
    /// <param name="logger">The host's logger, or <see langword="null"/>.</param>
    /// <param name="plan">The plan the runtime started with.</param>
    /// <param name="source">The source the runtime used.</param>
    /// <param name="authentication">The runtime's authentication status.</param>
    internal static void Report(
        ILogger? logger,
        CopilotSignInPlan plan,
        CopilotSignInSource source,
        GetAuthStatusResponse authentication)
    {
        if (logger is null)
        {
            return;
        }

        var host = authentication.Host ?? "(unknown host)";
        var login = authentication.Login ?? "(unknown login)";
        SignedIn(logger, source, host, login, null);

        // A runtime fallback or environment token on another account than the Copilot CLI's
        if (plan.CliAccount is { } cli
            && source != CopilotSignInSource.CopilotCli
            && (!SameHost(cli.Host, authentication.Host)
                || !string.Equals(cli.Login, authentication.Login, StringComparison.OrdinalIgnoreCase)))
        {
            OtherAccount(logger, host, login, source, cli.Host, cli.Login, null);
        }
    }

    /// <summary>
    ///     Removes anything shaped like a GitHub token from a text, so a message from the runtime or the
    ///     SDK can be quoted safely.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with token-shaped values redacted.</returns>
    internal static string Redact(string text) => TokenShape().Replace(text, "$1<redacted>");

    /// <summary>
    ///     Removes every token a sign-in knows, and any text shaped like a GitHub token, from a text
    ///     AgentKit is about to quote.
    /// </summary>
    /// <param name="plan">
    ///     The sign-in whose tokens are removed, or <see langword="null"/> to remove token-shaped text
    ///     only (a client built outside <see cref="CopilotRuntime.StartAsync"/>, such as a test's).
    /// </param>
    /// <param name="text">The text, such as a runtime's error message, or <see langword="null"/>.</param>
    /// <returns>The text with the secrets redacted; empty for a <see langword="null"/> text.</returns>
    internal static string Redact(CopilotSignInPlan? plan, string? text) =>
        text is null ? string.Empty : plan?.Redact(text) ?? Redact(text);

    /// <summary>
    ///     Makes the redacted copy of a runtime or SDK failure that AgentKit keeps as an inner exception
    ///     in place of the original.
    /// </summary>
    /// <remarks>
    ///     Always a copy, never the original, whether or not a secret was spotted: properties AgentKit
    ///     cannot inspect, such as an RPC error's data, never pass through. See
    ///     <see cref="CopilotRedactedException"/> for what the copy carries.
    /// </remarks>
    /// <param name="failure">The failure the runtime or the SDK raised.</param>
    /// <param name="plan">
    ///     The sign-in whose tokens are removed, or <see langword="null"/> to remove token-shaped text
    ///     only.
    /// </param>
    /// <returns>The redacted copy, with every chained exception copied the same way.</returns>
    internal static CopilotRedactedException RedactFailure(Exception failure, CopilotSignInPlan? plan) =>
        CopilotRedactedException.Copy(failure, text => Redact(plan, text), depth: 1);

    /// <summary>
    ///     Tries the Copilot CLI sign-in: checks the account against the requested host and reads its
    ///     token.
    /// </summary>
    /// <param name="lookup">The account lookup.</param>
    /// <param name="requestedHost">The host the host chose, or <see langword="null"/>.</param>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="launch">What the confined runtime will run.</param>
    /// <param name="context">The machine and the helper factory.</param>
    /// <param name="secrets">The tokens the resolution knows, redacted from the helper's text.</param>
    /// <param name="logger">The host's logger, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>The token, or <see langword="null"/>; and the step's finding.</returns>
    private static async Task<(string? Token, string Finding)> TryCopilotCliAsync(
        CopilotCliAccountLookup lookup,
        string? requestedHost,
        IDictionary hostEnvironment,
        CopilotRuntimeLaunch launch,
        CopilotSignInContext context,
        CopilotSignInSecrets secrets,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (lookup.Account is not { } account)
        {
            return (null, lookup.Problem ?? "no signed-in account found.");
        }

        var found = $"{account.Host} / {account.Login} found in '{lookup.ConfigurationPath}'";
        if (requestedHost is not null && !SameHost(requestedHost, account.Host))
        {
            return (null, $"{found}, but {requestedHost} was requested (gitHubHost or COPILOT_GH_HOST), so it was not used.");
        }

        var (token, problem) = await ReadCopilotCliTokenAsync(account, hostEnvironment, launch.CliPath, context, secrets, logger, cancellationToken)
            .ConfigureAwait(false);
        return token is null
            ? (null, $"{found}, but {problem}")
            : (token, $"{found}; AgentKit obtained its token through the Copilot CLI and passed it to the runtime.");
    }

    /// <summary>
    ///     Runs the helper runtime and reads the account's token from it.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="options">The helper's client options.</param>
    /// <param name="context">The helper factory and timeout.</param>
    /// <param name="secrets">The tokens the resolution knows, redacted from the helper's text.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The token, or <see langword="null"/> with why there is none.</returns>
    /// <exception cref="OperationCanceledException">The caller canceled.</exception>
    private static async Task<(string? Token, string? Problem)> RunHelperAsync(
        CopilotCliAccount account,
        CopilotClientOptions options,
        CopilotSignInContext context,
        CopilotSignInSecrets secrets,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(context.HelperTimeout);
        ICopilotSignInHelper? helper = null;
        try
        {
            // Created inside the guard: a client that cannot be constructed is a finding, not a failed start
            helper = context.CreateHelper(options);

            // Signed in through a stored sign-in as exactly this account, before the account list is read
            await helper.StartAsync(timeout.Token).ConfigureAwait(false);
            var status = await helper.GetAuthStatusAsync(timeout.Token).ConfigureAwait(false);
            if (!status.IsAuthenticated)
            {
                var reported = string.IsNullOrWhiteSpace(status.StatusMessage) ? "not signed in" : secrets.Redact(status.StatusMessage.Trim());
                return (null, $"the Copilot CLI could not read that account's token from the system credential store (it reports: {reported}).");
            }

            if (!string.Equals(status.AuthType, "user", StringComparison.Ordinal)
                || !SameHost(status.Host, account.Host)
                || !string.Equals(status.Login, account.Login, StringComparison.OrdinalIgnoreCase))
            {
                return (null, $"the Copilot CLI reported another sign-in ({status.AuthType ?? "unknown"}: {status.Host ?? "unknown host"} / {status.Login ?? "unknown login"}) instead of that account's.");
            }

            IList<AccountAllUsers> users;
            try
            {
                users = await helper.GetAllUsersAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (null, $"the SDK could not read the Copilot CLI's account list ({ex.GetType().Name}).");
            }

            var token = users
                .Where(user => user.AuthInfo is AuthInfoUser entry
                    && SameHost(entry.Host, account.Host)
                    && string.Equals(entry.Login, account.Login, StringComparison.OrdinalIgnoreCase))
                .Select(user => user.Token)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return token is null
                ? (null, "the Copilot CLI returned no token for that account.")
                : (token, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, $"the Copilot CLI did not report the account within {context.HelperTimeout.TotalSeconds:0} seconds (a credential-store prompt nobody answered?).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (null, $"the Copilot CLI could not be run to read it ({ex.GetType().Name}: {secrets.Redact(ex.Message)}).");
        }
        finally
        {
            if (helper is not null)
            {
                try
                {
                    await helper.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Intentionally swallowed: stopping the helper must not replace the step's outcome.
                }
            }
        }
    }

    /// <summary>
    ///     Reads the account entry from a parsed Copilot CLI configuration.
    /// </summary>
    /// <param name="root">The configuration's root element.</param>
    /// <param name="configurationPath">The file's full path, for findings.</param>
    /// <returns>The account, or why there is none.</returns>
    private static CopilotCliAccountLookup ReadAccountEntry(JsonElement root, string configurationPath)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return new CopilotCliAccountLookup(null, configurationPath, $"'{configurationPath}' is not a JSON object.");
        }

        // The last signed-in account; failing that, the only signed-in account
        JsonElement entry;
        if (root.TryGetProperty("lastLoggedInUser", out var last) && last.ValueKind == JsonValueKind.Object)
        {
            entry = last;
        }
        else if (root.TryGetProperty("loggedInUsers", out var all)
            && all.ValueKind == JsonValueKind.Array
            && all.GetArrayLength() == 1
            && all[0].ValueKind == JsonValueKind.Object)
        {
            entry = all[0];
        }
        else
        {
            return new CopilotCliAccountLookup(null, configurationPath, $"no signed-in account in '{configurationPath}' (run 'copilot login').");
        }

        var host = entry.TryGetProperty("host", out var hostValue) && hostValue.ValueKind == JsonValueKind.String ? hostValue.GetString() : null;
        var login = entry.TryGetProperty("login", out var loginValue) && loginValue.ValueKind == JsonValueKind.String ? loginValue.GetString() : null;
        if (!TryNormalizeHost(host, out var normalized)
            || string.IsNullOrWhiteSpace(login)
            || login.Length > MaximumLoginLength
            || login.Any(char.IsControl))
        {
            return new CopilotCliAccountLookup(null, configurationPath, $"the signed-in account in '{configurationPath}' has no valid host and login.");
        }

        return new CopilotCliAccountLookup(new CopilotCliAccount(normalized!, login), configurationPath, null);
    }

    /// <summary>
    ///     Writes one account entry.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="account">The account.</param>
    private static void WriteAccount(Utf8JsonWriter writer, CopilotCliAccount account)
    {
        writer.WriteStartObject();
        writer.WriteString("host", account.Host);
        writer.WriteString("login", account.Login);
        writer.WriteEndObject();
    }

    /// <summary>
    ///     Describes the account a lookup found, for a step that did not use it.
    /// </summary>
    /// <param name="lookup">The lookup.</param>
    /// <returns>A clause naming the account, or empty when none was found.</returns>
    private static string DescribeFound(CopilotCliAccountLookup lookup) =>
        lookup.Account is { } account ? $" ({account.Host} / {account.Login} found in '{lookup.ConfigurationPath}')" : string.Empty;

    /// <summary>
    ///     Reads a variable from the host's environment with this platform's name comparison.
    /// </summary>
    /// <param name="hostEnvironment">The host's environment.</param>
    /// <param name="name">The variable's name.</param>
    /// <returns>The value, or <see langword="null"/> when unset.</returns>
    private static string? ReadVariable(IDictionary hostEnvironment, string name)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        foreach (DictionaryEntry entry in hostEnvironment)
        {
            if (entry.Key is string key && string.Equals(key, name, comparison))
            {
                return entry.Value as string;
            }
        }

        return null;
    }

    /// <summary>
    ///     Matches a GitHub token's shape: a known prefix followed by its body.
    /// </summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\b(gho_|ghp_|ghu_|ghs_|ghr_|github_pat_)[A-Za-z0-9_]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TokenShape();
}

/// <summary>
///     How the runtime signs in: what AgentKit hands it, and what each sign-in source found.
/// </summary>
/// <remarks>
///     A class rather than a record, so no compiler-generated <c>ToString</c> or equality ever prints
///     or compares the token; <see cref="ToString"/> redacts it.
/// </remarks>
internal sealed class CopilotSignInPlan
{
    /// <summary>
    ///     Gets the source AgentKit chose: a token it hands the runtime
    ///     (<see cref="CopilotSignInSource.GitHubToken"/>, <see cref="CopilotSignInSource.GitHubTokenProvider"/>,
    ///     <see cref="CopilotSignInSource.CopilotCli"/>), <see cref="CopilotSignInSource.EnvironmentVariable"/>
    ///     for a token the runtime reads itself, or <see langword="null"/> when the runtime's own
    ///     fallbacks decide.
    /// </summary>
    internal CopilotSignInSource? Source { get; init; }

    /// <summary>
    ///     Gets the token AgentKit hands the runtime, or <see langword="null"/> for none.
    /// </summary>
    internal string? Token { get; init; }

    /// <summary>
    ///     Gets the bare host name the runtime's <c>COPILOT_GH_HOST</c> is set to, or
    ///     <see langword="null"/> when no host was chosen, leaving the environment's value, which is
    ///     then unset or blank.
    /// </summary>
    internal string? HostName { get; init; }

    /// <summary>
    ///     Gets the host the host chose (<see cref="CopilotRuntimeOptions.GitHubHost"/> or
    ///     <c>COPILOT_GH_HOST</c>), normalized, or <see langword="null"/> when none was chosen.
    /// </summary>
    internal string? RequestedHost { get; init; }

    /// <summary>
    ///     Gets the host's token provider, which sessions ask for their tokens, or
    ///     <see langword="null"/>.
    /// </summary>
    internal Func<CopilotGitHubTokenRequest, CancellationToken, Task<CopilotGitHubToken>>? Provider { get; init; }

    /// <summary>
    ///     Gets the Copilot CLI account that was found, or <see langword="null"/>.
    /// </summary>
    internal CopilotCliAccount? CliAccount { get; init; }

    /// <summary>
    ///     Gets what the host-supplied step found.
    /// </summary>
    internal string HostSuppliedFinding { get; init; } = "not supplied.";

    /// <summary>
    ///     Gets what the environment step found.
    /// </summary>
    internal string EnvironmentFinding { get; init; } = "not read.";

    /// <summary>
    ///     Gets what the Copilot CLI step found.
    /// </summary>
    internal string CopilotCliFinding { get; init; } = "not read.";

    /// <summary>
    ///     Gets what is known about the runtime's own fallbacks.
    /// </summary>
    internal string FallbackFinding { get; init; } = "left to the runtime.";

    /// <summary>
    ///     Gets the tokens the sign-in knows, which <see cref="Redact"/> removes from quoted text.
    /// </summary>
    /// <remarks>
    ///     Shared with the runtime's session token provider, which adds each session token to it.
    /// </remarks>
    internal CopilotSignInSecrets Secrets { get; init; } = new();

    /// <summary>
    ///     Removes every token the sign-in knows, and any text shaped like a GitHub token, from a text
    ///     AgentKit is about to quote.
    /// </summary>
    /// <param name="text">The text, such as a runtime's status message or an exception's message.</param>
    /// <returns>The text with <see cref="Token"/>, every value in <see cref="Secrets"/> and token-shaped values redacted.</returns>
    internal string Redact(string text) => Secrets.Redact(text, Token);

    /// <summary>
    ///     Builds the plan the host's options alone describe, without reading the machine: the host's
    ///     token, if any, and the host's chosen host.
    /// </summary>
    /// <remarks>
    ///     For a client built outside <see cref="CopilotRuntime.StartAsync"/>, such as a unit test's;
    ///     a provider is not asked.
    /// </remarks>
    /// <param name="options">The host's options.</param>
    /// <returns>The plan.</returns>
    internal static CopilotSignInPlan FromOptions(CopilotRuntimeOptions options)
    {
        var secrets = new CopilotSignInSecrets();
        secrets.Add(options.GitHubToken);
        return new()
        {
            Source = options.GitHubToken is null ? null : CopilotSignInSource.GitHubToken,
            Token = options.GitHubToken,
            HostName = options.GitHubHost is null ? null : CopilotSignIn.HostName(options.GitHubHost),
            RequestedHost = options.GitHubHost,
            Provider = options.GitHubTokenProvider,
            Secrets = secrets,
        };
    }

    /// <summary>
    ///     Returns a description of the plan that never contains the token.
    /// </summary>
    /// <returns>The source, host and whether a token is handed over.</returns>
    public override string ToString() =>
        $"CopilotSignInPlan {{ Source = {Source?.ToString() ?? "runtime fallbacks"}, HostName = {HostName ?? "(unchanged)"}, "
        + $"Token = {(Token is null ? "none" : "<redacted>")} }}";
}

/// <summary>
///     The exact tokens a sign-in knows, removed from any runtime, SDK or exception text AgentKit
///     quotes, whatever their shape.
/// </summary>
/// <remarks>
///     Thread-safe: the runtime's session token provider adds tokens while a start failure on another
///     thread may be redacting. Values are matched exactly (ordinal), longest first, so a token that
///     contains another is removed whole. A value is never too short to be removed: a secret's safety
///     comes before the readability of the text it appears in.
/// </remarks>
internal sealed class CopilotSignInSecrets
{
    /// <summary>
    ///     What a redacted value is replaced with.
    /// </summary>
    private const string Redacted = "<redacted>";

    /// <summary>
    ///     Guards <see cref="_values"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The known values.
    /// </summary>
    private readonly HashSet<string> _values = new(StringComparer.Ordinal);

    /// <summary>
    ///     Records a value as a secret.
    /// </summary>
    /// <remarks>
    ///     A <see langword="null"/>, empty or whitespace value is ignored. A value with surrounding
    ///     whitespace is recorded both as given and trimmed, since a runtime may quote either.
    /// </remarks>
    /// <param name="value">The value, such as a token.</param>
    internal void Add(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        lock (_gate)
        {
            _values.Add(value);
            _values.Add(value.Trim());
        }
    }

    /// <summary>
    ///     Removes every known value, and then any text shaped like a GitHub token, from a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="also">One more value to remove, such as the plan's token, or <see langword="null"/>.</param>
    /// <returns>The text with the secrets redacted.</returns>
    internal string Redact(string text, string? also = null)
    {
        List<string> values;
        lock (_gate)
        {
            values = [.. _values];
        }

        if (!string.IsNullOrWhiteSpace(also))
        {
            values.Add(also);
            values.Add(also.Trim());
        }

        foreach (var value in values.Distinct(StringComparer.Ordinal).OrderByDescending(value => value.Length))
        {
            text = text.Replace(value, Redacted, StringComparison.Ordinal);
        }

        return CopilotSignIn.Redact(text);
    }
}

/// <summary>
///     A redacted copy of a runtime or SDK failure, kept as an inner exception in place of the
///     original so no exception in the chain carries a token.
/// </summary>
/// <remarks>
///     <para>
///     The copy carries the original's type name (<see cref="OriginalTypeName"/>, which also leads
///     <see cref="ToString"/>), its message and its stack trace text, each redacted, and its
///     <see cref="Exception.HResult"/>. Its <see cref="Exception.InnerException"/> is a copy of the
///     original's, made the same way, down to <see cref="MaximumDepth"/> exceptions; for an
///     <see cref="AggregateException"/> that is its first inner exception, and the aggregate's own
///     message, which lists every inner exception's message, is redacted too.
///     </para>
///     <para>
///     Nothing else is copied: not <see cref="Exception.Data"/>, <see cref="Exception.Source"/>,
///     <see cref="Exception.HelpLink"/> or any property of the original's own type, because AgentKit
///     cannot know what they hold. Internal, because a host catches the outer exception, whose type
///     never changes; the copy is diagnostic text.
///     </para>
/// </remarks>
// Internal on purpose: a host never catches this type, only the outer exception that carries it, so
// making it public would add API surface and a versioning commitment no host needs.
#pragma warning disable S3871
internal sealed class CopilotRedactedException : Exception
#pragma warning restore S3871
{
    /// <summary>
    ///     The most exceptions of a chain that are copied, the first included.
    /// </summary>
    internal const int MaximumDepth = 16;

    /// <summary>
    ///     The original's stack trace text, redacted, or <see langword="null"/> when it was never thrown.
    /// </summary>
    private readonly string? _stackTrace;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CopilotRedactedException"/> class.
    /// </summary>
    /// <param name="originalTypeName">The original's full type name.</param>
    /// <param name="message">The original's message, redacted.</param>
    /// <param name="stackTrace">The original's stack trace text, redacted, or <see langword="null"/>.</param>
    /// <param name="innerException">The copy of the original's inner exception, or <see langword="null"/>.</param>
    private CopilotRedactedException(string originalTypeName, string message, string? stackTrace, Exception? innerException)
        : base(message, innerException)
    {
        OriginalTypeName = originalTypeName;
        _stackTrace = stackTrace;
    }

    /// <summary>
    ///     Gets the original exception's full type name, such as <c>System.IO.FileNotFoundException</c>.
    /// </summary>
    internal string OriginalTypeName { get; }

    /// <summary>
    ///     Gets the original exception's stack trace text, redacted.
    /// </summary>
    public override string? StackTrace => _stackTrace;

    /// <summary>
    ///     Describes the copy in the layout of <see cref="Exception.ToString"/>, led by the original's
    ///     type name.
    /// </summary>
    /// <returns>The type name, message, inner exceptions and stack trace, all redacted.</returns>
    public override string ToString()
    {
        var text = new StringBuilder(OriginalTypeName);
        if (Message.Length > 0)
        {
            text.Append(": ").Append(Message);
        }

        if (InnerException is { } inner)
        {
            text.Append(" ---> ").Append(inner.ToString()).AppendLine().Append("   --- End of inner exception stack trace ---");
        }

        if (_stackTrace is not null)
        {
            text.AppendLine().Append(_stackTrace);
        }

        return text.ToString();
    }

    /// <summary>
    ///     Copies a failure and its chain, redacted.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="redact">Removes the secrets from a text.</param>
    /// <param name="depth">The failure's position in the chain, the first being 1.</param>
    /// <returns>The copy.</returns>
    internal static CopilotRedactedException Copy(Exception failure, Func<string?, string> redact, int depth)
    {
        // An aggregate's first inner exception is the one chained; its message lists them all
        var next = failure is AggregateException { InnerExceptions.Count: > 0 } aggregate
            ? aggregate.InnerExceptions[0]
            : failure.InnerException;
        var inner = next is not null && depth < MaximumDepth ? Copy(next, redact, depth + 1) : null;
        var stackTrace = failure.StackTrace;
        var type = failure.GetType();
        return new CopilotRedactedException(
            type.FullName ?? type.Name,
            redact(failure.Message),
            stackTrace is null ? null : redact(stackTrace),
            inner)
        {
            HResult = failure.HResult,
        };
    }
}

/// <summary>
///     The account a user signed in to the Copilot CLI with: its host and login, never a token.
/// </summary>
/// <param name="Host">The normalized host, such as <c>https://example.ghe.com</c>.</param>
/// <param name="Login">The login.</param>
internal sealed record CopilotCliAccount(string Host, string Login);

/// <summary>
///     The result of looking up the Copilot CLI's account.
/// </summary>
/// <param name="Account">The account, or <see langword="null"/> when none was found.</param>
/// <param name="ConfigurationPath">The configuration file read, or <see langword="null"/> when none could be located.</param>
/// <param name="Problem">Why there is no account, or <see langword="null"/> when there is one.</param>
internal sealed record CopilotCliAccountLookup(CopilotCliAccount? Account, string? ConfigurationPath, string? Problem);

/// <summary>
///     The client a helper runtime is reached through: just the calls the sign-in makes.
/// </summary>
/// <remarks>
///     A seam: production wraps a <see cref="CopilotClient"/>; tests pass a fake, so every outcome is
///     testable without a runtime.
/// </remarks>
internal interface ICopilotSignInHelper : IAsyncDisposable
{
    /// <summary>
    ///     Starts the helper runtime.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A task that completes when the runtime has started.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the helper runtime's authentication status.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The status.</returns>
    Task<GetAuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Reads the accounts the helper runtime holds, with their tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts.</returns>
    Task<IList<AccountAllUsers>> GetAllUsersAsync(CancellationToken cancellationToken);
}

/// <summary>
///     A helper runtime reached through a real <see cref="CopilotClient"/>.
/// </summary>
/// <param name="client">The client, which this helper owns and disposes.</param>
internal sealed class CopilotClientSignInHelper(CopilotClient client) : ICopilotSignInHelper
{
    /// <inheritdoc/>
    public Task StartAsync(CancellationToken cancellationToken) => client.StartAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<GetAuthStatusResponse> GetAuthStatusAsync(CancellationToken cancellationToken) =>
        client.GetAuthStatusAsync(cancellationToken);

    /// <inheritdoc/>
    public Task<IList<AccountAllUsers>> GetAllUsersAsync(CancellationToken cancellationToken) =>
        client.Rpc.Account.GetAllUsersAsync(cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => client.DisposeAsync();
}

/// <summary>
///     The machine sign-in resolution reads, and how it starts and cleans up a helper runtime.
/// </summary>
/// <remarks>
///     A seam: production uses <see cref="Current"/>; tests pass fakes, so every source and failure
///     is testable without a Copilot CLI, a credential store or a GitHub account.
/// </remarks>
/// <param name="Search">The platform, <c>PATH</c> and file system, to find the GitHub CLI and <c>node</c>.</param>
/// <param name="UserProfile">The user's profile folder, or <see langword="null"/> when unknown.</param>
/// <param name="CreateHelperHome">Creates a new, private, empty temporary folder and returns its full path.</param>
/// <param name="CreateHelper">Creates a helper runtime's client from its options.</param>
/// <param name="DeleteDirectory">Deletes a helper home by its exact path; returns whether it is gone.</param>
/// <param name="HelperTimeout">How long the helper may take.</param>
internal sealed record CopilotSignInContext(
    CopilotCliSearchContext Search,
    string? UserProfile,
    Func<string> CreateHelperHome,
    Func<CopilotClientOptions, ICopilotSignInHelper> CreateHelper,
    Func<string, bool> DeleteDirectory,
    TimeSpan HelperTimeout)
{
    /// <summary>
    ///     Builds the context of the current machine.
    /// </summary>
    /// <returns>
    ///     The context: this machine's search context and profile folder, a temporary folder from
    ///     <see cref="Directory.CreateTempSubdirectory(string)"/> (private to the user on Unix), a real
    ///     <see cref="CopilotClient"/>, and <see cref="CopilotSignIn.DeleteHelperHome"/>.
    /// </returns>
    internal static CopilotSignInContext Current()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new CopilotSignInContext(
            CopilotCliSearchContext.Current(),
            string.IsNullOrEmpty(profile) ? null : profile,
            () => Directory.CreateTempSubdirectory(CopilotSignIn.HelperHomePrefix).FullName,
            options => new CopilotClientSignInHelper(new CopilotClient(options)),
            CopilotSignIn.DeleteHelperHome,
            CopilotSignIn.HelperTimeout);
    }
}
