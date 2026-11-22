using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for model settings: every validation rule, binding to a selection, and the
///     defaults a picker starts from.
/// </summary>
public class ModelSettingsTests
{
    /// <summary>
    ///     Proves settings cannot name no model (rule V1).
    /// </summary>
    [Fact]
    public void ModelSettings_Construct_BlankModelId_Throws()
    {
        // Arrange / Act / Assert: at construction and through a with-expression alike
        var blank = Assert.Throws<ArgumentException>(() => new ModelSettings(" "));
        Assert.Contains("A model id is required.", blank.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new ModelSettings("m") with { ModelId = "" });
    }

    /// <summary>
    ///     Proves settings naming one model are refused against another (rule V2).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_DifferentModel_IsRefused()
    {
        // Arrange
        var settings = new ModelSettings("other");

        // Act
        var problems = settings.Validate(ModelTestData.VisionModel());

        // Assert
        Assert.Equal("Settings name model 'other' but were checked against model 'vision-large'.", Assert.Single(problems));
    }

    /// <summary>
    ///     Proves a model AgentKit cannot run is refused, with the provider's reason (rule V3).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_UnusableModel_IsRefusedWithItsReason()
    {
        // Arrange
        var settings = new ModelSettings("embedder");

        // Act
        var problems = settings.Validate(ModelTestData.UnusableModel());

        // Assert
        Assert.Equal("Model 'embedder' cannot be used: embedding-only model", Assert.Single(problems));
    }

    /// <summary>
    ///     Proves a model missing a required capability is refused, naming what is missing (rule V4).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_MissingRequiredCapability_IsRefused()
    {
        // Arrange
        var settings = new ModelSettings("text-small");

        // Act
        var problems = settings.Validate(ModelTestData.TextModel(), ModelCapabilities.Vision | ModelCapabilities.ToolCalls);

        // Assert
        Assert.Equal(
            "Model 'text-small' does not support vision, which this application requires.",
            Assert.Single(problems));
    }

    /// <summary>
    ///     Proves an effort the model does not offer is refused, listing those it does (rule V5).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_UnofferedEffort_IsRefusedListingTheSupportedOnes()
    {
        // Arrange
        var settings = new ModelSettings("vision-large") { ReasoningEffort = "medium" };

        // Act
        var problems = settings.Validate(ModelTestData.VisionModel());

        // Assert
        Assert.Equal(
            "Model 'vision-large' does not support reasoning effort 'medium'. Supported: low, high, max.",
            Assert.Single(problems));
    }

    /// <summary>
    ///     Proves an effort on a model that accepts none is refused (rule V5).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_EffortOnModelWithoutEfforts_IsRefused()
    {
        // Arrange
        var settings = new ModelSettings("text-small") { ReasoningEffort = "low" };

        // Act
        var problems = settings.Validate(ModelTestData.TextModel());

        // Assert
        Assert.Equal("Model 'text-small' does not support reasoning effort settings.", Assert.Single(problems));
    }

    /// <summary>
    ///     Proves a tier the model does not offer is refused, and a tier on a model without tiers too
    ///     (rule V6).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_UnofferedTier_IsRefused()
    {
        // Arrange
        var unoffered = new ModelSettings("vision-large") { ContextTier = "huge" };
        var noTiers = new ModelSettings("text-small") { ContextTier = "default" };

        // Act
        var first = unoffered.Validate(ModelTestData.VisionModel());
        var second = noTiers.Validate(ModelTestData.TextModel());

        // Assert
        Assert.Equal(
            "Model 'vision-large' does not offer context tier 'huge'. Offered: default, long_context.",
            Assert.Single(first));
        Assert.Equal("Model 'text-small' offers no context tiers.", Assert.Single(second));
    }

    /// <summary>
    ///     Proves a window of zero or fewer tokens is refused (rule V7).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_NonPositiveWindow_IsRefused()
    {
        // Arrange
        var settings = new ModelSettings("text-small") { ContextWindowTokens = 0 };

        // Act
        var problems = settings.Validate(ModelTestData.TextModel());

        // Assert
        Assert.Equal("A context window must be a positive number of tokens.", Assert.Single(problems));
    }

    /// <summary>
    ///     Proves a window on a model that accepts none is refused (rule V7).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_WindowOnModelWithoutWindowSetting_IsRefused()
    {
        // Arrange
        var model = ModelTestData.TextModel() with { Adjustments = ModelAdjustments.None };
        var settings = new ModelSettings("text-small") { ContextWindowTokens = 8192 };

        // Act
        var problems = settings.Validate(model);

        // Assert
        Assert.Equal("Model 'text-small' does not allow setting the context window.", Assert.Single(problems));
    }

    /// <summary>
    ///     Proves a window larger than the model's maximum is refused when no tier bounds it (rule V8).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_WindowAboveModelMaximum_IsRefused()
    {
        // Arrange: a model trained for 32,768 tokens
        var settings = new ModelSettings("text-small") { ContextWindowTokens = 32_769 };
        var atLimit = new ModelSettings("text-small") { ContextWindowTokens = 32_768 };

        // Act
        var problems = settings.Validate(ModelTestData.TextModel());

        // Assert: one more than the maximum is refused, the maximum itself is not
        Assert.Equal(
            "Model 'text-small' supports at most 32,768 tokens; a 32,769-token window is not supported.",
            Assert.Single(problems));
        Assert.Empty(atLimit.Validate(ModelTestData.TextModel()));
    }

    /// <summary>
    ///     Proves a window is held to the chosen tier, else the default tier (rule V9).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_WindowAboveTierWindow_IsRefused()
    {
        // Arrange: the default tier provides 128,000 and long context 1,000,000
        var inDefault = new ModelSettings("vision-large") { ContextWindowTokens = 200_000 };
        var inLong = new ModelSettings("vision-large") { ContextTier = "long_context", ContextWindowTokens = 200_000 };

        // Act
        var refused = inDefault.Validate(ModelTestData.VisionModel());
        var accepted = inLong.Validate(ModelTestData.VisionModel());

        // Assert
        Assert.Equal(
            "Model 'vision-large' provides at most 128,000 tokens in context tier 'default'; a 200,000-token window cannot be honored.",
            Assert.Single(refused));
        Assert.Empty(accepted);
    }

    /// <summary>
    ///     Proves a chosen tier that publishes no window leaves the window held to the default tier's.
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_ChosenTierWithoutWindow_IsHeldToTheDefaultTier()
    {
        // Arrange: a third tier with no published window
        var model = ModelTestData.VisionModel() with
        {
            ContextTiers =
            [
                new ModelContextTier("default", "Standard", 128_000),
                new ModelContextTier("extended", "extended", null),
            ],
        };
        var settings = new ModelSettings("vision-large") { ContextTier = "extended", ContextWindowTokens = 200_000 };

        // Act
        var problems = settings.Validate(model);

        // Assert
        Assert.Contains("in context tier 'default'", Assert.Single(problems), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unknown maximum is not enforced.
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_UnknownMaximum_IsNotEnforced()
    {
        // Arrange
        var model = ModelTestData.TextModel() with { ContextWindowTokens = null };
        var settings = new ModelSettings("text-small") { ContextWindowTokens = 10_000_000 };

        // Act / Assert
        Assert.Empty(settings.Validate(model));
    }

    /// <summary>
    ///     Proves an output limit is refused when not accepted, not positive, or above the model's
    ///     own (rule V10).
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_MaxOutput_IsCheckedAgainstTheModel()
    {
        // Arrange
        var notAccepted = new ModelSettings("text-small") { MaxOutputTokens = 100 };
        var notPositive = new ModelSettings("vision-large") { MaxOutputTokens = -1 };
        var tooLarge = new ModelSettings("vision-large") { MaxOutputTokens = 64_001 };

        // Act / Assert
        Assert.Equal("Model 'text-small' does not allow setting the maximum output.", Assert.Single(notAccepted.Validate(ModelTestData.TextModel())));
        Assert.Equal("A maximum output must be a positive number of tokens.", Assert.Single(notPositive.Validate(ModelTestData.VisionModel())));
        Assert.Equal("Model 'vision-large' allows at most 64,000 output tokens.", Assert.Single(tooLarge.Validate(ModelTestData.VisionModel())));
    }

    /// <summary>
    ///     Proves every problem is reported at once rather than only the first.
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_SeveralProblems_AreAllReported()
    {
        // Arrange: an unusable model asked for vision, an effort, a tier and a bad window
        var settings = new ModelSettings("embedder")
        {
            ReasoningEffort = "high",
            ContextTier = "default",
            ContextWindowTokens = -5,
        };

        // Act
        var problems = settings.Validate(ModelTestData.UnusableModel(), ModelCapabilities.Vision);

        // Assert
        Assert.Equal(5, problems.Count);
    }

    /// <summary>
    ///     Proves binding valid settings yields a selection carrying the neutral effort and the tier
    ///     window.
    /// </summary>
    [Fact]
    public void ModelSettings_Bind_ValidSettings_YieldsSelection()
    {
        // Arrange
        var settings = new ModelSettings("vision-large") { ReasoningEffort = "HIGH", ContextTier = "long_context" };

        // Act
        var selection = settings.Bind(ModelTestData.VisionModel(), ModelCapabilities.Vision);

        // Assert
        Assert.Same(settings, selection.Settings);
        Assert.Equal(ReasoningEffort.High, selection.StandardReasoningEffort);
        Assert.Equal(1_000_000, selection.ContextTierWindowTokens);
        Assert.Equal(HostCapabilities.Vision, selection.HostCapabilities);
    }

    /// <summary>
    ///     Proves an effort with no neutral equivalent binds with no neutral effort, and a
    ///     selection that names no tier reports the default tier's window.
    /// </summary>
    [Fact]
    public void ModelSettings_Bind_ProviderOnlyEffort_HasNoStandardEquivalent()
    {
        // Arrange
        var settings = new ModelSettings("vision-large") { ReasoningEffort = "max" };

        // Act
        var selection = settings.Bind(ModelTestData.VisionModel());

        // Assert
        Assert.Null(selection.StandardReasoningEffort);
        Assert.Equal(128_000, selection.ContextTierWindowTokens);
    }

    /// <summary>
    ///     Proves binding invalid settings throws, carrying every problem.
    /// </summary>
    [Fact]
    public void ModelSettings_Bind_InvalidSettings_ThrowsWithEveryProblem()
    {
        // Arrange
        var settings = new ModelSettings("text-small") { ReasoningEffort = "low", ContextTier = "default" };

        // Act
        var failure = Assert.Throws<ModelSettingsException>(() => settings.Bind(ModelTestData.TextModel()));

        // Assert
        Assert.Equal("text-small", failure.ModelId);
        Assert.Equal(2, failure.Problems.Count);
        Assert.StartsWith("Model settings for 'text-small' are not valid: ", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves the problems validation reports are read-only: the list cannot be cast back and
    ///     altered.
    /// </summary>
    [Fact]
    public void ModelSettings_Validate_Problems_AreReadOnly()
    {
        // Arrange: settings with a problem
        var settings = new ModelSettings("text-small") { ReasoningEffort = "low" };

        // Act
        var problems = settings.Validate(ModelTestData.TextModel());

        // Assert
        Assert.NotEmpty(problems);
        Assert.False(problems is List<string> or string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)problems).Add("x"));
    }

    /// <summary>
    ///     Proves the exception copies the problems it is given and exposes them read-only.
    /// </summary>
    [Fact]
    public void ModelSettings_Exception_Problems_AreCopiedAndReadOnly()
    {
        // Arrange: a mutable list of problems
        var source = new List<string> { "First problem." };
        var failure = new ModelSettingsException("m", source);

        // Act: change the source after the exception was built
        source.Add("Added later.");

        // Assert
        Assert.Equal("First problem.", Assert.Single(failure.Problems));
        Assert.False(failure.Problems is List<string> or string[]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)failure.Problems).Add("x"));
    }

    /// <summary>
    ///     Proves a selection can be created only by validation, never directly.
    /// </summary>
    [Fact]
    public void ModelSettings_ModelSelection_HasNoPublicConstructor()
    {
        // Arrange / Act
        var constructors = typeof(ModelSelection).GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        // Assert
        Assert.Empty(constructors);
    }

    /// <summary>
    ///     Proves a picker's starting settings carry the published defaults and invent nothing.
    /// </summary>
    [Fact]
    public void ModelSettings_DefaultsFor_CarriesPublishedDefaultsOnly()
    {
        // Arrange
        var withDefaults = ModelTestData.VisionModel();
        var withoutDefaults = ModelTestData.VisionModel() with { DefaultReasoningEffort = null };

        // Act
        var first = ModelSettings.DefaultsFor(withDefaults);
        var second = ModelSettings.DefaultsFor(withoutDefaults);

        // Assert
        Assert.Equal("low", first.ReasoningEffort);
        Assert.Equal("default", first.ContextTier);
        Assert.Null(first.ContextWindowTokens);
        Assert.Null(second.ReasoningEffort);
        Assert.Empty(second.Validate(withoutDefaults));
    }

    /// <summary>
    ///     Proves settings survive a round trip through JSON, which is how an application persists them.
    /// </summary>
    [Fact]
    public void ModelSettings_Serialize_RoundTrips()
    {
        // Arrange
        var settings = new ModelSettings("vision-large") { ReasoningEffort = "high", ContextWindowTokens = 8192 };

        // Act
        var restored = JsonSerializer.Deserialize<ModelSettings>(JsonSerializer.Serialize(settings));

        // Assert
        Assert.Equal(settings, restored);
    }
}
