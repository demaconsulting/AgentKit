using Microsoft.Extensions.AI;

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

    /// <summary>
    ///     Proves the lists a description is built from are copied, so changing them afterwards does
    ///     not change the description.
    /// </summary>
    [Fact]
    public void ModelInfo_Lists_SourceMutatedAfterInit_InstanceUnchanged()
    {
        // Arrange: mutable sources for every list
        var efforts = new List<ModelReasoningEffort> { new("low", "Low", ReasoningEffort.Low) };
        var tiers = new List<ModelContextTier> { new("default", "Standard", 128_000) };
        var notices = new List<ModelNotice> { new(ModelNoticeKind.Info, "Preview model.") };
        var model = new ModelInfo
        {
            Provider = "test",
            Id = "m",
            DisplayName = "M",
            ReasoningEfforts = efforts,
            ContextTiers = tiers,
            Notices = notices,
        };

        // Act: change every source after the description was built
        efforts.Add(new ModelReasoningEffort("high", "High", ReasoningEffort.High));
        tiers.Clear();
        notices[0] = new ModelNotice(ModelNoticeKind.Warning, "Changed.");

        // Assert: the description still holds what it was built with
        Assert.Equal("low", Assert.Single(model.ReasoningEfforts).Id);
        Assert.Equal("default", Assert.Single(model.ContextTiers).Id);
        Assert.Equal("Preview model.", Assert.Single(model.Notices).Message);
    }

    /// <summary>
    ///     Proves the lists a description exposes cannot be cast back to a mutable list and altered.
    /// </summary>
    [Fact]
    public void ModelInfo_Lists_CannotBeCastBackAndMutated()
    {
        // Arrange: a description holding an entry in every list
        var model = new ModelInfo
        {
            Provider = "test",
            Id = "m",
            DisplayName = "M",
            ReasoningEfforts = [new ModelReasoningEffort("low", "Low", ReasoningEffort.Low)],
            ContextTiers = [new ModelContextTier("default", "Standard", 128_000)],
            Notices = [new ModelNotice(ModelNoticeKind.Info, "Preview model.")],
        };
        var effort = new ModelReasoningEffort("high", "High", ReasoningEffort.High);
        var tier = new ModelContextTier("long_context", "Long context", 1_000_000);
        var notice = new ModelNotice(ModelNoticeKind.Warning, "Changed.");

        // Act / Assert: no list is an array or a list, and none accepts a change through IList
        Assert.False(model.ReasoningEfforts is ModelReasoningEffort[] or List<ModelReasoningEffort>);
        Assert.False(model.ContextTiers is ModelContextTier[] or List<ModelContextTier>);
        Assert.False(model.Notices is ModelNotice[] or List<ModelNotice>);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelReasoningEffort>)model.ReasoningEfforts).Add(effort));
        Assert.Throws<NotSupportedException>(() => ((IList<ModelReasoningEffort>)model.ReasoningEfforts)[0] = effort);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelContextTier>)model.ContextTiers).Add(tier));
        Assert.Throws<NotSupportedException>(() => ((IList<ModelContextTier>)model.ContextTiers)[0] = tier);
        Assert.Throws<NotSupportedException>(() => ((IList<ModelNotice>)model.Notices).Add(notice));
        Assert.Throws<NotSupportedException>(() => ((IList<ModelNotice>)model.Notices)[0] = notice);
    }

    /// <summary>
    ///     Proves a list replaced through a <see langword="with"/> expression is copied too, and the
    ///     original description is unaffected.
    /// </summary>
    [Fact]
    public void ModelInfo_With_ReplacedList_IsCopied()
    {
        // Arrange: a description, and a mutable source for a replacement list
        var model = ModelTestData.VisionModel();
        var originalNotices = model.Notices.Count;
        var source = new List<ModelNotice> { new(ModelNoticeKind.Info, "Replaced.") };

        // Act: replace the notices, then change the source
        var copy = model with { Notices = source };
        source.Add(new ModelNotice(ModelNoticeKind.Warning, "Added later."));

        // Assert: the copy holds what it was given, and the original is untouched
        Assert.Equal("Replaced.", Assert.Single(copy.Notices).Message);
        Assert.Equal(originalNotices, model.Notices.Count);
    }

    /// <summary>
    ///     Proves the additional-properties dictionary is copied when set and exposed read-only.
    /// </summary>
    [Fact]
    public void ModelInfo_AdditionalProperties_SourceMutatedAfterInit_InstanceUnchanged()
    {
        // Arrange: a description built from a mutable dictionary
        var source = new AdditionalPropertiesDictionary { ["family"] = "gemma" };
        var model = new ModelInfo { Provider = "test", Id = "m", DisplayName = "M", AdditionalProperties = source };

        // Act: change the source after the description was built
        source["family"] = "changed";
        source["extra"] = 1;

        // Assert: the description is unchanged, keeps case-insensitive keys, and refuses a change
        var properties = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(model.AdditionalProperties);
        Assert.Equal("gemma", properties["FAMILY"]);
        Assert.Single(properties);
        Assert.IsNotType<AdditionalPropertiesDictionary>(properties);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, object?>)properties)["extra"] = 1);
    }

    /// <summary>
    ///     Proves a list holding a null entry is refused, naming the list.
    /// </summary>
    [Fact]
    public void ModelInfo_Lists_NullEntry_IsRefused()
    {
        // Arrange: a list with a null entry
        var notices = new List<ModelNotice> { null! };

        // Act / Assert
        var exception = Assert.Throws<ArgumentException>(
            () => new ModelInfo { Provider = "test", Id = "m", DisplayName = "M", Notices = notices });
        Assert.Equal("Notices", exception.ParamName);
    }

    /// <summary>
    ///     Proves a null list is taken as empty rather than left missing.
    /// </summary>
    [Fact]
    public void ModelInfo_Lists_Null_IsEmpty()
    {
        // Arrange / Act: every list set to null, as a deserializer may
        var model = new ModelInfo
        {
            Provider = "test",
            Id = "m",
            DisplayName = "M",
            ReasoningEfforts = null!,
            ContextTiers = null!,
            Notices = null!,
        };

        // Assert
        Assert.Empty(model.ReasoningEfforts);
        Assert.Empty(model.ContextTiers);
        Assert.Empty(model.Notices);
    }
}
