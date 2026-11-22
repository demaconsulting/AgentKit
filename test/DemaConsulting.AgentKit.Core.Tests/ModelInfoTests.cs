namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the model description: capability tests and the derivation of host
///     capabilities from a model.
/// </summary>
public class ModelInfoTests
{
    /// <summary>
    ///     Proves a model satisfies a requirement only when it reports every required flag.
    /// </summary>
    [Fact]
    public void ModelInfo_Supports_EveryRequiredFlagReported_IsSatisfiedOtherwiseNot()
    {
        // Arrange: a vision model and a text-only model
        var vision = ModelTestData.VisionModel();
        var text = ModelTestData.TextModel();

        // Act / Assert: vision with tools is satisfied only by the model reporting both
        Assert.True(vision.Supports(ModelCapabilities.Vision | ModelCapabilities.ToolCalls));
        Assert.False(text.Supports(ModelCapabilities.Vision | ModelCapabilities.ToolCalls));
        Assert.True(text.Supports(ModelCapabilities.ToolCalls));
    }

    /// <summary>
    ///     Proves requiring nothing is satisfied by every model, including one reporting nothing.
    /// </summary>
    [Fact]
    public void ModelInfo_Supports_NothingRequired_IsAlwaysSatisfied()
    {
        // Arrange: a model reporting no capability at all
        var model = ModelTestData.UnusableModel();

        // Act / Assert
        Assert.True(model.Supports(ModelCapabilities.None));
    }

    /// <summary>
    ///     Proves a model that accepts images contributes the vision host capability, and nothing else.
    /// </summary>
    [Fact]
    public void ModelInfo_ToHostCapabilities_VisionModel_YieldsVisionOnly()
    {
        // Arrange: every model capability at once
        const ModelCapabilities everything = ModelCapabilities.ToolCalls | ModelCapabilities.Vision | ModelCapabilities.Reasoning;

        // Act
        var host = everything.ToHostCapabilities();

        // Assert: delegation stays the application's to declare
        Assert.Equal(HostCapabilities.Vision, host);
    }

    /// <summary>
    ///     Proves a model that cannot see images contributes no host capability.
    /// </summary>
    [Fact]
    public void ModelInfo_ToHostCapabilities_TextModel_YieldsNone()
    {
        // Arrange / Act
        var host = (ModelCapabilities.ToolCalls | ModelCapabilities.Reasoning).ToHostCapabilities();

        // Assert
        Assert.Equal(HostCapabilities.None, host);
        Assert.Equal(HostCapabilities.None, ModelCapabilities.None.ToHostCapabilities());
    }

    /// <summary>
    ///     Proves every capability and adjustment member is zero or a single bit, so flags combine
    ///     without one silently satisfying another.
    /// </summary>
    [Fact]
    public void ModelInfo_FlagEnums_EveryMember_IsZeroOrASingleBit()
    {
        // Arrange: every declared member of both flag sets
        var values = Enum.GetValues<ModelCapabilities>().Select(value => (int)value)
            .Concat(Enum.GetValues<ModelAdjustments>().Select(value => (int)value));

        // Act / Assert
        Assert.All(values, value => Assert.True(value == 0 || (value & (value - 1)) == 0, $"{value} is not a single bit"));
    }

    /// <summary>
    ///     Proves a model built by hand defaults to usable, with empty lists rather than missing ones.
    /// </summary>
    [Fact]
    public void ModelInfo_Construct_ByHand_DefaultsToUsableWithEmptyLists()
    {
        // Arrange / Act: only the required members
        var model = new ModelInfo { Provider = "test", Id = "m", DisplayName = "M" };

        // Assert
        Assert.True(model.IsUsable);
        Assert.Empty(model.ReasoningEfforts);
        Assert.Empty(model.ContextTiers);
        Assert.Empty(model.Notices);
        Assert.Equal(ModelCapabilities.None, model.Capabilities);
    }
}
