namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the caching catalog base, the listing query and the session-start gate.
/// </summary>
/// <remarks>
///     The read is scripted and counted, and the clock is moved by hand, so caching, expiry,
///     single-flight and failure handling are observed from outside without a provider or a wait.
/// </remarks>
public class ModelCatalogTests
{
    /// <summary>
    ///     Proves nothing is read until the catalog is first asked.
    /// </summary>
    [Fact]
    public void ModelCatalog_Construct_ReadsNothingUntilAsked()
    {
        // Arrange / Act
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()]);

        // Assert
        Assert.Equal(0, catalog.Reads);
        Assert.Equal("test", catalog.ProviderName);
    }

    /// <summary>
    ///     Proves a second query within the lifetime is served from the cache.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_WithinLifetime_ReadsOnce()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()], clock);

        // Act: two listings and a lookup, four minutes apart
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(4));
        await catalog.ListAsync(new ModelQuery { Require = ModelCapabilities.Vision }, TestContext.Current.CancellationToken);
        await catalog.GetAsync("vision-large", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, catalog.Reads);
    }

    /// <summary>
    ///     Proves a listing older than the default five-minute lifetime is read again.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_AfterLifetime_ReadsAgain()
    {
        // Arrange
        var clock = new ManualTimeProvider();
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()], clock);
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act: just past five minutes
        clock.Advance(CachingModelCatalog.DefaultLifetime + TimeSpan.FromMilliseconds(1));
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(5), CachingModelCatalog.DefaultLifetime);
        Assert.Equal(2, catalog.Reads);
    }

    /// <summary>
    ///     Proves callers that arrive while a read is in flight share it rather than starting their own.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_ConcurrentCallers_ShareOneRead()
    {
        // Arrange: a read that does not finish until released
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new ScriptedModelCatalog(async (_, _) =>
        {
            await release.Task;
            return [ModelTestData.VisionModel()];
        });

        // Act: twenty callers, then the read completes
        var callers = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken), TestContext.Current.CancellationToken))
            .ToArray();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        release.SetResult();
        var results = await Task.WhenAll(callers);

        // Assert: one read served everyone
        Assert.Equal(1, catalog.Reads);
        Assert.All(results, models => Assert.Single(models));
    }

    /// <summary>
    ///     Proves a failed read is not cached: the failure reaches the caller and the next query reads
    ///     again.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_ReadFails_IsNotCachedAndNextQueryRetries()
    {
        // Arrange: the first read fails, the second succeeds
        var catalog = new ScriptedModelCatalog((read, _) => read == 1
            ? Task.FromException<IReadOnlyList<ModelInfo>>(new InvalidOperationException("provider down"))
            : Task.FromResult<IReadOnlyList<ModelInfo>>([ModelTestData.VisionModel()]));

        // Act
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken));
        var models = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("provider down", failure.Message);
        Assert.Single(models);
        Assert.Equal(2, catalog.Reads);
    }

    /// <summary>
    ///     Proves the cancellation of the caller whose read it was reaches only that caller, and a
    ///     waiter reads again for itself.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_ReadingCallerCancels_WaiterReadsForItself()
    {
        // Arrange: the first read waits for its token; later reads succeed at once
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new ScriptedModelCatalog(async (read, token) =>
        {
            if (read == 1)
            {
                firstStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            return [ModelTestData.VisionModel()];
        });
        using var cancel = new CancellationTokenSource();

        // Act: the first caller starts the read, a second joins it, then the first cancels
        var first = catalog.ListAsync(cancellationToken: cancel.Token);
        await firstStarted.Task;
        var second = catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        await cancel.CancelAsync();

        // Assert: the first is canceled, the second succeeds on a read of its own
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Single(await second);
        Assert.Equal(2, catalog.Reads);
    }

    /// <summary>
    ///     Proves a read the provider cancels on its own - a client timeout, with no caller canceled -
    ///     is a failure: it reaches the reading caller and every waiter from one read, and is not cached.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_ProviderTimeout_ReachesEveryWaiterWithOneRead()
    {
        // Arrange: the first read times out once released, with no caller's token canceled; later
        // reads succeed at once
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var catalog = new ScriptedModelCatalog(async (read, _) =>
        {
            if (read == 1)
            {
                firstStarted.SetResult();
                await release.Task;
                throw new TaskCanceledException("provider timed out");
            }

            return [ModelTestData.VisionModel()];
        });

        // Act: one caller starts the read, three join it, then the provider times out
        var owner = catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        await firstStarted.Task;
        var waiters = Enumerable.Range(0, 3)
            .Select(_ => catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken))
            .ToArray();
        release.SetResult();

        // Assert: every caller receives the timeout from the one read
        var ownerFailure = await Assert.ThrowsAsync<TaskCanceledException>(() => owner);
        Assert.Equal("provider timed out", ownerFailure.Message);
        foreach (var waiter in waiters)
        {
            var failure = await Assert.ThrowsAsync<TaskCanceledException>(() => waiter);
            Assert.Equal("provider timed out", failure.Message);
        }

        Assert.Equal(1, catalog.Reads);

        // Assert: the failure was not cached; the next query reads again
        Assert.Single(await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(2, catalog.Reads);
    }

    /// <summary>
    ///     Proves a refresh reads again at once, within the lifetime.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_RefreshAsync_WithinLifetime_ReadsAgain()
    {
        // Arrange: the second read reports a model the first did not
        var catalog = new ScriptedModelCatalog((read, _) => Task.FromResult<IReadOnlyList<ModelInfo>>(read == 1
            ? [ModelTestData.TextModel()]
            : [ModelTestData.TextModel(), ModelTestData.VisionModel()]));
        await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act
        await catalog.RefreshAsync(TestContext.Current.CancellationToken);
        var models = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, catalog.Reads);
        Assert.Equal(2, models.Count);
    }

    /// <summary>
    ///     Proves the requirement filters the listing and unusable models are hidden unless asked for.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_Query_FiltersByRequirementAndUsability()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog(
            [ModelTestData.TextModel(), ModelTestData.VisionModel(), ModelTestData.UnusableModel()]);

        // Act
        var all = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);
        var vision = await catalog.ListAsync(new ModelQuery { Require = ModelCapabilities.Vision | ModelCapabilities.ToolCalls }, TestContext.Current.CancellationToken);
        var everything = await catalog.ListAsync(new ModelQuery { IncludeUnusable = true }, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["text-small", "vision-large"], all.Select(model => model.Id));
        Assert.Equal(["vision-large"], vision.Select(model => model.Id));
        Assert.Equal(["text-small", "vision-large", "embedder"], everything.Select(model => model.Id));
    }

    /// <summary>
    ///     Proves the provider's order is preserved rather than re-sorted.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_ProviderOrder_IsPreserved()
    {
        // Arrange: an order no sort would produce
        var catalog = new ScriptedModelCatalog(
            [ModelTestData.VisionModel("zeta"), ModelTestData.VisionModel("alpha"), ModelTestData.VisionModel("mu")]);

        // Act
        var models = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(["zeta", "alpha", "mu"], models.Select(model => model.Id));
    }

    /// <summary>
    ///     Proves a listing is read-only, and an attempt to alter it leaves the cached listing whole.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_ListAsync_Result_IsReadOnlyAndDoesNotAlterTheCache()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel("a"), ModelTestData.VisionModel("b")]);
        var first = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Act: try to clear the listing through a mutable interface, then list again
        Assert.False(first is ModelInfo[] or List<ModelInfo>);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelInfo>)first).Clear());
        var second = await catalog.ListAsync(cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the second listing is complete and came from the cache
        Assert.Equal(["a", "b"], second.Select(model => model.Id));
        Assert.Equal(1, catalog.Reads);
    }

    /// <summary>
    ///     Proves a lookup finds an unusable model, so validation can say why it cannot be used.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_GetAsync_UnusableModel_IsFound()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog([ModelTestData.UnusableModel()]);

        // Act
        var model = await catalog.GetAsync("embedder", TestContext.Current.CancellationToken);
        var missing = await catalog.GetAsync("nothing", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(model);
        Assert.Null(missing);
    }

    /// <summary>
    ///     Proves the session-start gate refuses a model the provider no longer offers.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_SelectAsync_UnknownModel_IsRefusedAsNotAvailable()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()]);

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(
            () => catalog.SelectAsync(new ModelSettings("gone"), cancellationToken: TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("gone", failure.ModelId);
        Assert.Equal(
            "Model 'gone' is not available from provider 'test'. It may have been removed or you may no longer have access.",
            Assert.Single(failure.Problems));
    }

    /// <summary>
    ///     Proves the session-start gate re-enforces the application's requirement on a persisted choice.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_SelectAsync_ModelLostARequiredCapability_IsRefused()
    {
        // Arrange: the model was chosen for vision, and the provider now reports it without
        var persisted = new ModelSettings("vision-large");
        var catalog = new ScriptedModelCatalog(
            [ModelTestData.VisionModel() with { Capabilities = ModelCapabilities.ToolCalls }]);

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(
            () => catalog.SelectAsync(persisted, ModelCapabilities.Vision, TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(failure.Problems, problem => problem.Contains("does not support vision", StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves valid settings produce a selection carrying the model and the requirement.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_SelectAsync_ValidSettings_ProducesSelection()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()]);
        var settings = new ModelSettings("vision-large") { ReasoningEffort = "high" };

        // Act
        var selection = await catalog.SelectAsync(settings, ModelCapabilities.Vision, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("vision-large", selection.Model.Id);
        Assert.Equal(ModelCapabilities.Vision, selection.Required);
        Assert.Equal(HostCapabilities.Vision, selection.HostCapabilities);
    }

    /// <summary>
    ///     Proves a catalog that accepts another spelling of an identifier selects by its own spelling.
    /// </summary>
    [Fact]
    public async Task ModelCatalog_SelectAsync_AlternateSpelling_SelectsTheCatalogsIdentifier()
    {
        // Arrange: a catalog that compares identifiers ignoring case
        var catalog = new CaseInsensitiveCatalog([ModelTestData.VisionModel("Vision-Large")]);

        // Act
        var selection = await catalog.SelectAsync(new ModelSettings("vision-large"), cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Vision-Large", selection.Settings.ModelId);
    }

    /// <summary>
    ///     Proves a non-positive lifetime is refused at construction.
    /// </summary>
    [Fact]
    public void ModelCatalog_Construct_NonPositiveLifetime_Throws()
    {
        // Arrange / Act / Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ScriptedModelCatalog((_, _) => Task.FromResult<IReadOnlyList<ModelInfo>>([]), lifetime: TimeSpan.Zero));
    }

    /// <summary>
    ///     A catalog that compares identifiers ignoring case, standing in for a provider with more
    ///     than one spelling of a model's name.
    /// </summary>
    /// <param name="models">The models every read returns.</param>
    private sealed class CaseInsensitiveCatalog(IReadOnlyList<ModelInfo> models) : CachingModelCatalog("test")
    {
        /// <inheritdoc/>
        protected override Task<IReadOnlyList<ModelInfo>> FetchAsync(CancellationToken cancellationToken) =>
            Task.FromResult(models);

        /// <inheritdoc/>
        protected override bool IdEquals(string requested, string catalogId) =>
            string.Equals(requested, catalogId, StringComparison.OrdinalIgnoreCase);
    }
}
