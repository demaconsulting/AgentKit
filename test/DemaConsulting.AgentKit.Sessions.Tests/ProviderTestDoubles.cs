namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     A provider-session factory that yields scripted sessions in order, so a test can arrange a
///     good first session and a failing replacement.
/// </summary>
/// <param name="makers">The session factories, in creation order.</param>
internal sealed class ScriptedProviderSessionFactory(params Func<ProviderSessionSeed, IProviderSession>[] makers)
    : IProviderSessionFactory
{
    private int _index;

    /// <summary>
    ///     Gets every session created so far, oldest first.
    /// </summary>
    public List<IProviderSession> Created { get; } = [];

    /// <inheritdoc/>
    public Task<IProviderSession> CreateAsync(
        ProviderSessionSeed seed,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seed);
        cancellationToken.ThrowIfCancellationRequested();

        if (_index >= makers.Length)
        {
            throw new InvalidOperationException("The scripted factory ran out of sessions.");
        }

        var session = makers[_index++](seed);
        Created.Add(session);
        return Task.FromResult(session);
    }
}

/// <summary>
///     A provider session whose usage report throws, standing in for an adapter whose split is
///     arithmetically impossible — the condition the session must release rather than orphan.
/// </summary>
/// <param name="throwOnDispose">Whether releasing the session also fails.</param>
internal sealed class UsageThrowingProviderSession(bool throwOnDispose) : IProviderSession, IContextUsageReporter
{
    /// <summary>
    ///     Gets a value indicating whether the session was disposed.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <inheritdoc/>
    public ContextUsage? CurrentUsage => throw new InvalidOperationException("The adapter reported an impossible split.");

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderTurn("answer"));

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (throwOnDispose)
        {
            throw new IOException("The provider session could not be released.");
        }

        IsDisposed = true;
        return default;
    }
}

/// <summary>
///     A provider session whose disposal always fails, so a test can prove disposal propagates the
///     failure and stays retryable.
/// </summary>
internal sealed class DisposeThrowingProviderSession : IProviderSession
{
    /// <summary>
    ///     Gets how many times release was attempted.
    /// </summary>
    public int DisposeAttempts { get; private set; }

    /// <inheritdoc/>
    public Task<ProviderTurn> SendAsync(string message, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ProviderTurn("answer"));

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeAttempts++;
        throw new IOException("The provider session could not be released.");
    }
}
