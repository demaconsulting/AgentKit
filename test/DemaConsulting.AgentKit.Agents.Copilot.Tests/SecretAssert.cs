using System.Collections;
using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace DemaConsulting.AgentKit.Agents.Copilot.Tests;

/// <summary>
///     The one assertion that no known secret reaches a host: not in a failure's whole chain, not in
///     anything a logger received, not in a text such as a finding.
/// </summary>
/// <remarks>
///     <para>
///     A failure is walked through every <see cref="Exception.InnerException"/> and every inner
///     exception of an <see cref="AggregateException"/>. Each exception's message, stack trace, string
///     form, <see cref="Exception.Source"/>, <see cref="Exception.HelpLink"/>, every key and value of
///     its <see cref="Exception.Data"/>, and every other public readable property's text are checked.
///     Only public properties are read: they are what a host can observe.
///     </para>
///     <para>
///     A log is checked entry by entry — the formatted message, the event's name, the state's text,
///     every structured key and value, and the entry's exception by the walk above — and scope by
///     scope. A failing assertion names where the secret was found, never the secret.
///     </para>
/// </remarks>
internal static class SecretAssert
{
    /// <summary>
    ///     Asserts that no secret appears anywhere in a failure's chain or in a log.
    /// </summary>
    /// <param name="secrets">The secrets; at least one, none blank.</param>
    /// <param name="failure">The failure whose whole chain is checked, or <see langword="null"/>.</param>
    /// <param name="log">The log whose every entry and scope is checked, or <see langword="null"/>.</param>
    internal static void Nowhere(IReadOnlyCollection<string> secrets, Exception? failure = null, CapturingLogger? log = null)
    {
        Assert.NotEmpty(secrets);
        Assert.All(secrets, secret => Assert.False(string.IsNullOrWhiteSpace(secret), "A secret to look for is blank."));
        Assert.True(failure is not null || log is not null, "Nothing was given to check for secrets.");

        if (failure is not null)
        {
            CheckChain(failure, secrets, "the failure");
        }

        if (log is not null)
        {
            CheckLog(log, secrets);
        }
    }

    /// <summary>
    ///     Asserts that no secret appears anywhere in a failure's chain.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="secrets">The secrets.</param>
    internal static void NoSecret(Exception failure, params string[] secrets) => Nowhere(secrets, failure: failure);

    /// <summary>
    ///     Asserts that no secret appears anywhere in what a logger received.
    /// </summary>
    /// <param name="log">The log.</param>
    /// <param name="secrets">The secrets.</param>
    internal static void NoSecret(CapturingLogger log, params string[] secrets) => Nowhere(secrets, log: log);

    /// <summary>
    ///     Asserts that no secret appears in a text, such as a finding or a plan's string form.
    /// </summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <param name="secrets">The secrets.</param>
    internal static void NoSecretInText(string? text, params string[] secrets)
    {
        Assert.NotEmpty(secrets);
        Assert.All(secrets, secret => Assert.False(string.IsNullOrWhiteSpace(secret), "A secret to look for is blank."));
        Check(text, secrets, "the text");
    }

    /// <summary>
    ///     Checks a failure and every exception chained to it.
    /// </summary>
    /// <param name="failure">The failure.</param>
    /// <param name="secrets">The secrets.</param>
    /// <param name="where">Where the failure was found, for the assertion's message.</param>
    private static void CheckChain(Exception failure, IReadOnlyCollection<string> secrets, string where)
    {
        var visited = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(Exception Exception, int Depth)>();
        pending.Push((failure, 1));
        while (pending.TryPop(out var item))
        {
            var (current, depth) = item;
            if (!visited.Add(current))
            {
                continue;
            }

            var location = $"{where}, exception {depth} ({current.GetType().Name})";
            Check(current.Message, secrets, $"{location}, message");
            Check(current.StackTrace, secrets, $"{location}, stack trace");
            Check(current.ToString(), secrets, $"{location}, string form");
            Check(current.Source, secrets, $"{location}, source");
            Check(current.HelpLink, secrets, $"{location}, help link");
            foreach (DictionaryEntry entry in current.Data)
            {
                Check(Convert.ToString(entry.Key, CultureInfo.InvariantCulture), secrets, $"{location}, data key");
                Check(Convert.ToString(entry.Value, CultureInfo.InvariantCulture), secrets, $"{location}, data value");
            }

            CheckPublicProperties(current, secrets, location);

            if (current is AggregateException aggregate)
            {
                foreach (var each in aggregate.InnerExceptions)
                {
                    pending.Push((each, depth + 1));
                }
            }

            if (current.InnerException is { } inner)
            {
                pending.Push((inner, depth + 1));
            }
        }
    }

    /// <summary>
    ///     Checks the text of every public readable instance property of an exception that the walk
    ///     does not already cover, such as an RPC error's own data.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="secrets">The secrets.</param>
    /// <param name="location">Where the exception is, for the assertion's message.</param>
    private static void CheckPublicProperties(Exception exception, IReadOnlyCollection<string> secrets, string location)
    {
        foreach (var property in exception.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead
                || property.GetIndexParameters().Length > 0
                || property.Name is nameof(Exception.Data) or nameof(Exception.InnerException) or nameof(Exception.TargetSite)
                || typeof(Exception).IsAssignableFrom(property.PropertyType))
            {
                continue;
            }

            object? value;
            try
            {
                value = property.GetValue(exception);
            }
            catch (TargetInvocationException)
            {
                // A getter that throws exposes nothing
                continue;
            }

            if (value is null or Exception or IEnumerable<Exception> or MethodBase)
            {
                continue;
            }

            Check(Convert.ToString(value, CultureInfo.InvariantCulture), secrets, $"{location}, property '{property.Name}'");
        }
    }

    /// <summary>
    ///     Checks every entry and scope a logger received.
    /// </summary>
    /// <param name="log">The log.</param>
    /// <param name="secrets">The secrets.</param>
    private static void CheckLog(CapturingLogger log, IReadOnlyCollection<string> secrets)
    {
        var entries = log.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            var location = $"log entry {index + 1}";
            Check(entry.Message, secrets, $"{location}, message");
            Check(entry.EventId.Name, secrets, $"{location}, event name");
            Check(entry.StateText, secrets, $"{location}, state");
            CheckPairs(entry.State, secrets, location);
            if (entry.Exception is not null)
            {
                CheckChain(entry.Exception, secrets, location);
            }
        }

        var scopes = log.Scopes;
        for (var index = 0; index < scopes.Count; index++)
        {
            var location = $"log scope {index + 1}";
            Check(scopes[index].Text, secrets, $"{location}, text");
            CheckPairs(scopes[index].State, secrets, location);
        }
    }

    /// <summary>
    ///     Checks structured log values.
    /// </summary>
    /// <param name="pairs">The keys and values.</param>
    /// <param name="secrets">The secrets.</param>
    /// <param name="location">Where the values are, for the assertion's message.</param>
    private static void CheckPairs(IReadOnlyList<KeyValuePair<string, object?>> pairs, IReadOnlyCollection<string> secrets, string location)
    {
        foreach (var (key, value) in pairs)
        {
            Check(key, secrets, $"{location}, state key");
            Check(Convert.ToString(value, CultureInfo.InvariantCulture), secrets, $"{location}, state value '{key}'");
        }
    }

    /// <summary>
    ///     Checks one text, failing with where it was found and which secret, by position only.
    /// </summary>
    /// <param name="text">The text, or <see langword="null"/>.</param>
    /// <param name="secrets">The secrets.</param>
    /// <param name="location">Where the text is.</param>
    private static void Check(string? text, IReadOnlyCollection<string> secrets, string location)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var position = 0;
        foreach (var secret in secrets)
        {
            position++;
            if (text.Contains(secret, StringComparison.Ordinal))
            {
                Assert.Fail($"Secret {position} of {secrets.Count} was found in {location}.");
            }
        }
    }
}

/// <summary>
///     A logger that keeps everything it receives — each entry's level, event, message, state and
///     exception, and each scope — so a test can prove what reached it.
/// </summary>
/// <remarks>
///     Thread-safe: the runtime's diagnostics may log from any thread. Every level is enabled.
/// </remarks>
internal sealed class CapturingLogger : ILogger
{
    /// <summary>
    ///     Guards the lists.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     The entries, in order.
    /// </summary>
    private readonly List<CapturedLogEntry> _entries = [];

    /// <summary>
    ///     The scopes, in order.
    /// </summary>
    private readonly List<CapturedLogScope> _scopes = [];

    /// <summary>
    ///     Gets a snapshot of the entries, in order.
    /// </summary>
    public IReadOnlyList<CapturedLogEntry> Entries
    {
        get
        {
            lock (_gate)
            {
                return [.. _entries];
            }
        }
    }

    /// <summary>
    ///     Gets a snapshot of the scopes, in order.
    /// </summary>
    public IReadOnlyList<CapturedLogScope> Scopes
    {
        get
        {
            lock (_gate)
            {
                return [.. _scopes];
            }
        }
    }

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        var scope = new CapturedLogScope(Convert.ToString(state, CultureInfo.InvariantCulture), Pairs(state));
        lock (_gate)
        {
            _scopes.Add(scope);
        }

        return NoScope.Instance;
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var entry = new CapturedLogEntry(
            logLevel,
            eventId,
            formatter(state, exception),
            Convert.ToString(state, CultureInfo.InvariantCulture),
            Pairs(state),
            exception);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>
    ///     Snapshots a state's structured keys and values.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The pairs; empty when the state has none.</returns>
    private static List<KeyValuePair<string, object?>> Pairs(object? state) =>
        state is IEnumerable<KeyValuePair<string, object?>> pairs ? [.. pairs] : [];

    /// <summary>
    ///     The scope a captured scope returns: disposing it does nothing.
    /// </summary>
    private sealed class NoScope : IDisposable
    {
        /// <summary>
        ///     Gets the single instance.
        /// </summary>
        public static NoScope Instance { get; } = new();

        /// <inheritdoc/>
        public void Dispose()
        {
            // Nothing to release
        }
    }
}

/// <summary>
///     A log entry a <see cref="CapturingLogger"/> received.
/// </summary>
/// <param name="Level">The level.</param>
/// <param name="EventId">The event.</param>
/// <param name="Message">The message the caller's formatter produced.</param>
/// <param name="StateText">The state's string form.</param>
/// <param name="State">The state's structured keys and values.</param>
/// <param name="Exception">The entry's exception, or <see langword="null"/>.</param>
internal sealed record CapturedLogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    string? StateText,
    IReadOnlyList<KeyValuePair<string, object?>> State,
    Exception? Exception);

/// <summary>
///     A scope a <see cref="CapturingLogger"/> received.
/// </summary>
/// <param name="Text">The scope state's string form.</param>
/// <param name="State">The scope state's structured keys and values.</param>
internal sealed record CapturedLogScope(string? Text, IReadOnlyList<KeyValuePair<string, object?>> State);
