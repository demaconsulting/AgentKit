namespace DemaConsulting.AgentKit.Core;

/// <summary>
///     Which models a catalog listing returns.
/// </summary>
/// <remarks>
///     The query is applied to a cached listing, so filtering a list many ways costs one read of
///     the provider. An application that shows only vision-capable models states
///     <c>Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls</c> here, and states the
///     same requirement again to <see cref="ModelCatalogExtensions.SelectAsync"/> at session start,
///     so a model that loses the capability between the two is refused rather than run.
/// </remarks>
public sealed record ModelQuery
{
    /// <summary>
    ///     Gets the capabilities a model must report to be listed.
    /// </summary>
    public ModelCapabilities Require { get; init; }

    /// <summary>
    ///     Gets a value indicating whether models AgentKit cannot run are listed too.
    /// </summary>
    /// <remarks>
    ///     <see langword="false"/> by default, so a picker shows only runnable models. Set it to show
    ///     the others grayed out with <see cref="ModelInfo.UnusableReason"/>.
    /// </remarks>
    public bool IncludeUnusable { get; init; }

    /// <summary>
    ///     Tests whether a model satisfies this query.
    /// </summary>
    /// <param name="model">The model to test. Must not be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the model is listed under this query.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public bool Matches(ModelInfo model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return (IncludeUnusable || model.IsUsable) && model.Supports(Require);
    }
}

/// <summary>
///     The models one provider offers the signed-in user or the configured server.
/// </summary>
/// <remarks>
///     <para>
///     Implementations are expected to be safe for concurrent use and to cache what they read, since
///     a picker lists models far more often than a provider's offering changes.
///     <see cref="CachingModelCatalog"/> supplies that for the catalogs AgentKit ships and for a host
///     catalog that derives from it.
///     </para>
///     <para>
///     <b>Failures.</b> A read that fails propagates the provider's own exception — for example a
///     <see cref="System.Net.Http.HttpRequestException"/> from an HTTP-based provider — and is not
///     cached. An <see cref="OperationCanceledException"/> may come from the caller's token or from the
///     provider itself, such as a client timeout. A caller tells them apart by checking its own
///     token's <see cref="CancellationToken.IsCancellationRequested"/>.
///     </para>
/// </remarks>
public interface IModelCatalog
{
    /// <summary>
    ///     Gets the provider this catalog describes, as <see cref="ModelInfo.Provider"/> names it.
    /// </summary>
    string ProviderName { get; }

    /// <summary>
    ///     Lists the models the provider offers that satisfy a query, in the provider's order.
    /// </summary>
    /// <param name="query">The query, or <see langword="null"/> for every usable model.</param>
    /// <param name="cancellationToken">Cancels the read when one is needed.</param>
    /// <returns>The matching models. Never <see langword="null"/>.</returns>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    Task<IReadOnlyList<ModelInfo>> ListAsync(ModelQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Finds one model by its identifier, whether or not it is usable.
    /// </summary>
    /// <param name="modelId">The model identifier. Must not be <see langword="null"/> or blank.</param>
    /// <param name="cancellationToken">Cancels the read when one is needed.</param>
    /// <returns>The model, or <see langword="null"/> when the provider does not offer it.</returns>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    Task<ModelInfo?> GetAsync(string modelId, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Discards what was cached and reads the provider again now.
    /// </summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>A task that completes when the fresh listing is in place.</returns>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    Task RefreshAsync(CancellationToken cancellationToken = default);
}

/// <summary>
///     A model catalog that reads its provider lazily and caches the listing for a fixed lifetime,
///     shared by every query.
/// </summary>
/// <remarks>
///     <para>
///     <b>Read lazily, cached briefly.</b> Nothing is read until the first listing or lookup. The
///     listing is then kept for <see cref="DefaultLifetime"/> unless another lifetime is given, so a
///     picker that re-lists on every keystroke costs one provider call; a listing as old as the
///     lifetime is read again on the next query. <see cref="RefreshAsync"/> reads again at once.
///     </para>
///     <para>
///     <b>One read at a time, shared.</b> Callers that arrive while a read is in flight wait for it
///     rather than starting their own. A read that fails is not cached: every caller waiting on it
///     receives the failure, a caller that arrives as the failure is being reported receives it too,
///     and only a query after that reads again. A caller that cancels stops only its own wait; when
///     the caller whose read it was cancels, the read stops and the next waiter reads again for
///     itself.
///     </para>
///     <para>
///     <b>The provider's order is preserved,</b> and a query is applied to the cached listing when it
///     is read, so one read serves every filter.
///     </para>
///     <para>
///     Safe for concurrent use. The clock is injectable so expiry can be exercised without waiting.
///     </para>
/// </remarks>
public abstract class CachingModelCatalog : IModelCatalog
{
    /// <summary>
    ///     How long a listing is kept when no lifetime is given: five minutes.
    /// </summary>
    /// <remarks>
    ///     Long enough that a picker never waits on a provider twice in one interaction, short enough
    ///     that a model enabled or withdrawn by the provider appears or disappears without a restart.
    /// </remarks>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     Guards <see cref="_snapshot"/> and <see cref="_inFlight"/>.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    ///     How long a listing is kept.
    /// </summary>
    private readonly TimeSpan _lifetime;

    /// <summary>
    ///     The clock a listing's age is measured against.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The listing most recently read, or <see langword="null"/> before the first read or after a
    ///     refresh discarded it.
    /// </summary>
    private Snapshot? _snapshot;

    /// <summary>
    ///     The read in flight, which arriving callers wait on, or <see langword="null"/> when none is.
    /// </summary>
    /// <remarks>
    ///     A read's outcome is published before it is cleared from here, so a failed read stays
    ///     joinable until its owner releases it; a canceled read is treated as absent.
    /// </remarks>
    private Task<Snapshot>? _inFlight;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CachingModelCatalog"/> class.
    /// </summary>
    /// <param name="providerName">The provider's name. Must not be <see langword="null"/> or blank.</param>
    /// <param name="lifetime">
    ///     How long a listing is kept, or <see langword="null"/> for <see cref="DefaultLifetime"/>.
    ///     Must be positive when supplied.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock a listing's age is measured against, or <see langword="null"/> for the system
    ///     clock.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="providerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="providerName"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetime"/> is zero or negative.</exception>
    protected CachingModelCatalog(string providerName, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        if (lifetime is { } supplied && supplied <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), supplied, "A catalog lifetime must be positive.");
        }

        ProviderName = providerName;
        _lifetime = lifetime ?? DefaultLifetime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public string ProviderName { get; }

    /// <summary>
    ///     Gets or sets a test seam invoked on the joining caller's thread, outside the lock, each time
    ///     a caller joins a read in flight rather than starting one.
    /// </summary>
    /// <remarks>
    ///     <see langword="null"/> outside tests.
    /// </remarks>
    internal Action? OnReadJoined { get; set; }

    /// <summary>
    ///     Gets or sets a test seam invoked on the reading caller's thread, outside the lock, at the one
    ///     point where a read's outcome is settled but its waiters may not yet have observed it.
    /// </summary>
    /// <remarks>
    ///     That point is after a failure or cancellation has been published on the shared task and
    ///     before the read is released, or after a listing has been stored and the read released and
    ///     before the waiters are released. <see langword="null"/> outside tests.
    /// </remarks>
    internal Action? OnReadPublished { get; set; }

    /// <inheritdoc/>
    /// <remarks>
    ///     Reads the provider only when nothing fresh is cached. Performs network I/O on that read. Any
    ///     other failure of the provider's read propagates as the provider's own exception; see the
    ///     provider catalog's remarks.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The provider's read returned no listing, or a listing holding a null model.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    public async Task<IReadOnlyList<ModelInfo>> ListAsync(
        ModelQuery? query = null,
        CancellationToken cancellationToken = default)
    {
        var models = await GetModelsAsync(refresh: false, cancellationToken).ConfigureAwait(false);
        var filter = query ?? new ModelQuery();

        // A read-only view over a fresh array, so the caller can neither alter the cached listing
        // nor cast the result back to a mutable list.
        return Array.AsReadOnly(models.Where(filter.Matches).ToArray());
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     An unusable model is returned, so validation can say why it cannot be used rather than
    ///     only that it is missing. Identifiers are compared by <see cref="IdEquals"/>. Any other
    ///     failure of the provider's read propagates as the provider's own exception; see the provider
    ///     catalog's remarks.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="modelId"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelId"/> is blank.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The provider's read returned no listing, or a listing holding a null model.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    public async Task<ModelInfo?> GetAsync(string modelId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);

        var models = await GetModelsAsync(refresh: false, cancellationToken).ConfigureAwait(false);

        return models.FirstOrDefault(model => IdEquals(modelId, model.Id));
    }

    /// <inheritdoc/>
    /// <remarks>
    ///     The cached listing is discarded first, so a refresh that fails leaves nothing stale behind
    ///     and the next query reads again. A read already in flight is joined rather than duplicated. Any
    ///     other failure of the provider's read propagates as the provider's own exception; see the
    ///     provider catalog's remarks.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    ///     The provider's read returned no listing, or a listing holding a null model.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await GetModelsAsync(refresh: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Reads the provider's listing.
    /// </summary>
    /// <remarks>
    ///     Called by this class only, never concurrently with itself, and only when nothing fresh is
    ///     cached. The returned list is copied, so an implementation may return a list it reuses.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The models, in the order a picker should show them.</returns>
    protected abstract Task<IReadOnlyList<ModelInfo>> FetchAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Tests whether a requested identifier names a listed model.
    /// </summary>
    /// <remarks>
    ///     Ordinal by default. A provider that accepts more than one spelling of a model's name — Ollama
    ///     resolves a bare name to its <c>latest</c> tag — overrides this so a lookup by either
    ///     spelling finds the model.
    /// </remarks>
    /// <param name="requested">The identifier asked for.</param>
    /// <param name="catalogId">The identifier of a listed model.</param>
    /// <returns><see langword="true"/> when the two name the same model.</returns>
    protected virtual bool IdEquals(string requested, string catalogId) =>
        string.Equals(requested, catalogId, StringComparison.Ordinal);

    /// <summary>
    ///     Returns the cached listing when it is fresh, and otherwise reads it, sharing one read among
    ///     every caller that arrives while it is in flight.
    /// </summary>
    /// <param name="refresh"><see langword="true"/> to discard the cached listing first.</param>
    /// <param name="cancellationToken">Cancels this caller's wait, and the read when it is this caller's.</param>
    /// <returns>The listing.</returns>
    private async Task<IReadOnlyList<ModelInfo>> GetModelsAsync(bool refresh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (refresh)
        {
            lock (_gate)
            {
                _snapshot = null;
            }
        }

        while (true)
        {
            TaskCompletionSource<Snapshot>? owned = null;
            Task<Snapshot> read;

            // Decide under the lock whether the cache answers, a read is joined, or this caller reads.
            lock (_gate)
            {
                if (_snapshot is { } cached && IsFresh(cached))
                {
                    return cached.Models;
                }

                // A read its owner canceled is over for every other caller: each of them reads again.
                // A caller that finds one its owner has not yet released treats it as absent rather
                // than joining it, which would only bring it straight back here until the release.
                if (_inFlight is { IsCanceled: true })
                {
                    _inFlight = null;
                }

                // A failed read not yet released is joined, so the caller receives that failure; only
                // a caller arriving after the release starts a new read.
                if (_inFlight is null)
                {
                    owned = new TaskCompletionSource<Snapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _inFlight = owned.Task;
                }

                read = _inFlight;
            }

            if (owned is not null)
            {
                return (await ReadAsync(owned, cancellationToken).ConfigureAwait(false)).Models;
            }

            OnReadJoined?.Invoke();

            try
            {
                return (await read.WaitAsync(cancellationToken).ConfigureAwait(false)).Models;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && read.IsCanceled)
            {
                // The read this caller joined was canceled by the caller whose read it was. That
                // cancellation was not this caller's, so it reads again rather than inheriting it. A read
                // that failed - a provider timeout included - is faulted, not canceled, and propagates.
            }
        }
    }

    /// <summary>
    ///     Performs one read on behalf of every caller waiting on it, and publishes the outcome.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     A failure is published on the shared task before the read is released, so a caller that
    ///     arrives in between joins the read and receives its failure rather than starting a second
    ///     read; only a query after the release reads again. The shared task is canceled only when the
    ///     owner's own token was canceled; any other failure, a provider timeout included, faults it.
    ///     </para>
    ///     <para>
    ///     A listing is stored and the read released in one step, before the waiters are released.
    ///     There is no gap to close there: from the release on, a caller is served the stored listing
    ///     without a read, and a refresh arriving then reads again, as it should once this read has
    ///     finished.
    ///     </para>
    /// </remarks>
    /// <param name="completion">Completes the task the waiters joined.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The listing read.</returns>
    private async Task<Snapshot> ReadAsync(
        TaskCompletionSource<Snapshot> completion,
        CancellationToken cancellationToken)
    {
        Snapshot snapshot;
        try
        {
            var models = await FetchAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"The {ProviderName} catalog read returned no listing.");

            ModelInfo[] copy = [.. models];
            if (Array.Exists(copy, model => model is null))
            {
                throw new InvalidOperationException($"The {ProviderName} catalog read returned a null model.");
            }

            snapshot = new Snapshot(copy, _timeProvider.GetUtcNow());
        }
        catch (Exception exception)
        {
            // Failures are handed to the waiters and not cached. Publish first, then release: a caller
            // that arrives before the release joins this read and receives its outcome. Only the
            // owner's own token makes a read canceled; any other cancellation - an HTTP client's
            // timeout, say - is a failure of the read like any other, and every waiter receives it.
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                completion.SetCanceled(cancellationToken);
            }
            else
            {
                completion.SetException(exception);

                // Observed here so a failure no other caller happened to wait on is not reported
                // again later as an unobserved task exception.
                _ = completion.Task.Exception;
            }

            try
            {
                OnReadPublished?.Invoke();
            }
            finally
            {
                Release(completion.Task);
            }

            throw;
        }

        lock (_gate)
        {
            _snapshot = snapshot;
            ReleaseLocked(completion.Task);
        }

        try
        {
            OnReadPublished?.Invoke();
        }
        finally
        {
            completion.SetResult(snapshot);
        }

        return snapshot;
    }

    /// <summary>
    ///     Releases a read, unless a later read has already replaced it.
    /// </summary>
    /// <param name="read">The read to release.</param>
    private void Release(Task<Snapshot> read)
    {
        lock (_gate)
        {
            ReleaseLocked(read);
        }
    }

    /// <summary>
    ///     Clears the in-flight read when it is still <paramref name="read"/>. The caller holds the lock.
    /// </summary>
    /// <param name="read">The read to release.</param>
    private void ReleaseLocked(Task<Snapshot> read)
    {
        // A canceled read may already have been replaced by a caller that treated it as absent; that
        // caller's read must stay in flight.
        if (ReferenceEquals(_inFlight, read))
        {
            _inFlight = null;
        }
    }

    /// <summary>
    ///     Tests whether a listing is younger than the lifetime.
    /// </summary>
    /// <param name="snapshot">The listing.</param>
    /// <returns><see langword="true"/> when it may still be served.</returns>
    private bool IsFresh(Snapshot snapshot) => _timeProvider.GetUtcNow() - snapshot.ReadAt < _lifetime;

    /// <summary>
    ///     One listing and when it was read.
    /// </summary>
    /// <param name="Models">The models, in the provider's order.</param>
    /// <param name="ReadAt">When the read completed.</param>
    private sealed record Snapshot(ModelInfo[] Models, DateTimeOffset ReadAt);
}

/// <summary>
///     The session-start gate: looks a persisted model choice up in a catalog and validates it in one
///     call.
/// </summary>
public static class ModelCatalogExtensions
{
    /// <summary>
    ///     Finds the model settings name and validates the settings against it, producing the
    ///     selection a provider factory accepts.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///     Call this every time a session starts from persisted settings. The model may have been
    ///     withdrawn, may have lost a capability the application requires, or may no longer accept an
    ///     effort, tier or window the settings name; each of those fails here, with every problem
    ///     listed, rather than as a provider error part-way through a conversation.
    ///     </para>
    ///     <para>
    ///     The selection's settings carry the catalog's own spelling of the model identifier, so a
    ///     bare Ollama name chosen as <c>qwen3</c> is selected as <c>qwen3:latest</c>.
    ///     </para>
    ///     <para>
    ///     A failure of the catalog's read propagates as <see cref="IModelCatalog.GetAsync"/> raises it.
    ///     Because <see cref="ModelSettingsException"/> derives from <see cref="InvalidOperationException"/>,
    ///     catch it before any <see cref="InvalidOperationException"/> handler.
    ///     </para>
    /// </remarks>
    /// <param name="catalog">The catalog the model is offered by. Must not be <see langword="null"/>.</param>
    /// <param name="settings">The settings to apply. Must not be <see langword="null"/>.</param>
    /// <param name="required">The capabilities the application requires of the model.</param>
    /// <param name="cancellationToken">Cancels the catalog read when one is needed.</param>
    /// <returns>The validated selection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> or <paramref name="settings"/> is <see langword="null"/>.</exception>
    /// <exception cref="ModelSettingsException">
    ///     The provider does not offer the model, or the settings are not valid for it. The exception
    ///     lists every problem, each suitable to show verbatim.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    ///     <paramref name="cancellationToken"/> was canceled, or the provider's read was canceled without it,
    ///     for example by a client timeout; the caller's own token tells the two apart.
    /// </exception>
    /// <example>
    ///     <code>
    ///     public async Task&lt;ModelSelection?&gt; StartAsync(
    ///         IModelCatalog catalog,
    ///         ModelSettings persisted,
    ///         CancellationToken cancellationToken)
    ///     {
    ///         try
    ///         {
    ///             // The application lists only vision models, and says so again here.
    ///             return await catalog.SelectAsync(
    ///                 persisted,
    ///                 ModelCapabilities.Vision | ModelCapabilities.ToolCalls,
    ///                 cancellationToken);
    ///         }
    ///         catch (ModelSettingsException exception)
    ///         {
    ///             foreach (var problem in exception.Problems)
    ///             {
    ///                 Console.WriteLine(problem);
    ///             }
    ///
    ///             return null;
    ///         }
    ///     }
    ///     </code>
    /// </example>
    public static async Task<ModelSelection> SelectAsync(
        this IModelCatalog catalog,
        ModelSettings settings,
        ModelCapabilities required = ModelCapabilities.None,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(settings);

        var model = await catalog.GetAsync(settings.ModelId, cancellationToken).ConfigureAwait(false)
            ?? throw new ModelSettingsException(
                settings.ModelId,
                [
                    $"Model '{settings.ModelId}' is not available from provider '{catalog.ProviderName}'. "
                    + "It may have been removed or you may no longer have access.",
                ]);

        // The catalog may accept a spelling of the identifier other than its own; the selection
        // records the catalog's, so the provider is asked for exactly the model that was validated.
        var canonical = string.Equals(settings.ModelId, model.Id, StringComparison.Ordinal)
            ? settings
            : settings with { ModelId = model.Id };

        return canonical.Bind(model, required);
    }
}
