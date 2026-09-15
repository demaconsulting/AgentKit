namespace DemaConsulting.AgentKit.Sessions.Tests;

/// <summary>
///     Unit tests for <see cref="AgentSessionResponse"/> and the <see cref="IAgentSession"/>
///     contract: what one turn reports back about the window and about compaction.
/// </summary>
public class AgentSessionTests
{
    /// <summary>
    ///     Proves an ordinary turn reports no rotation and no saturation, so an application that
    ///     never looks is never misled and one that does look sees a clean turn as clean.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_OrdinaryTurn_ReportsNoCompaction()
    {
        // Arrange / Act: a turn that needed no compaction
        var response = new AgentSessionResponse("answer", ContextUsage.FromProvider(100, 1000), false);

        // Assert: the answer survives and nothing is reported about compaction
        Assert.Equal("answer", response.Text);
        Assert.False(response.RotationOccurred);
        Assert.Empty(response.Saturations);
        Assert.False(response.IsSaturated);
    }

    /// <summary>
    ///     Proves a saturating rotation is visible on the response: a saturated agent must be
    ///     distinguishable from a healthy one, or the condition is invisible to the application.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_SaturatedRotation_SurfacesTheSignals()
    {
        // Arrange: a rotation that could not reduce what it was given
        var signal = new SaturationSignal(1, 900, 880, SaturationReason.NoRedundancy);

        // Act: report the turn
        var response = new AgentSessionResponse(
            "answer", ContextUsage.FromEstimate(900, 1000), true, [signal]);

        // Assert: the rotation and its saturation are both visible
        Assert.True(response.RotationOccurred);
        Assert.True(response.IsSaturated);
        Assert.Same(signal, response.Saturations[0]);
    }

    /// <summary>
    ///     Proves the saturation list a response hands out is its own copy and cannot be written
    ///     through. A response is documented as immutable, and a caller able to add or remove a
    ///     signal would change <c>IsSaturated</c> after the turn it describes.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Saturations_CannotBeCastAndMutated()
    {
        // Arrange: a response built from a mutable list of signals
        var signals = new List<SaturationSignal>
        {
            new(1, 900, 880, SaturationReason.NoRedundancy)
        };
        var response = new AgentSessionResponse(
            "answer", ContextUsage.FromEstimate(900, 1000), true, signals);

        // Act: mutate the caller's list afterwards
        signals.Clear();

        // Assert: the response still reports the saturation, and refuses a write through its list
        Assert.True(response.IsSaturated);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<SaturationSignal>)response.Saturations).Clear());
    }

    /// <summary>
    ///     Proves a missing answer is refused; a null response text would surface as a failure in the
    ///     application that displayed it rather than in the adapter that produced it.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AgentSessionResponse(null!, ContextUsage.FromProvider(1, 10), false));
    }

    /// <summary>
    ///     Proves a missing usage figure is refused: every turn must be able to say what the window
    ///     looks like, or the rotation decision has nothing to rest on.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_NullUsage_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AgentSessionResponse("answer", null!, false));
    }

    /// <summary>
    ///     Proves a null saturation signal is refused rather than carried, because a null in the list
    ///     would fail in the application inspecting it.
    /// </summary>
    [Fact]
    public void AgentSessionResponse_Construct_NullSaturationSignal_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new AgentSessionResponse("answer", ContextUsage.FromProvider(1, 10), true, [null!]));
    }

    /// <summary>
    ///     Proves a saturation signal refuses figures that could only come from a defect, so the one
    ///     report an application acts on cannot itself be nonsense.
    /// </summary>
    [Fact]
    public void SaturationSignal_Construct_InvalidFigures_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SaturationSignal(0, 100, 100, SaturationReason.NoRedundancy));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SaturationSignal(1, -1, 100, SaturationReason.NoRedundancy));
    }

    /// <summary>
    ///     Proves a rotation outcome refuses a missing layout, because the layout is what a fresh
    ///     provider session is seeded from and there is no recovery from its absence.
    /// </summary>
    [Fact]
    public void RotationOutcome_Construct_NullLayout_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RotationOutcome(null!, [], 0));
    }
}
