using System.Reflection;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="IToolPack"/> contract and the
///     <see cref="HostCapabilities"/> enumeration.
/// </summary>
public class ToolPackTests
{
    /// <summary>
    ///     Proves that a pack reports the family prefix it declares.
    /// </summary>
    [Fact]
    public void ToolPack_FamilyPrefix_Implementation_ReportsItsDeclaredPrefix()
    {
        // Arrange: a pack declaring one family
        IToolPack pack = new StubToolPack("text_file");

        // Act: read the prefix through the contract
        var prefix = pack.FamilyPrefix;

        // Assert: the declaration is what a composer sees
        Assert.Equal("text_file", prefix);
    }

    /// <summary>
    ///     Proves that a pack reports the capabilities it declares it needs.
    /// </summary>
    [Fact]
    public void ToolPack_RequiredCapabilities_Implementation_ReportsItsDeclaredCapabilities()
    {
        // Arrange: a pack that cannot operate without vision
        IToolPack pack = new StubToolPack("image", HostCapabilities.Vision);

        // Act: read the requirement through the contract
        var required = pack.RequiredCapabilities;

        // Assert: the pack declares its need rather than deciding for itself
        Assert.Equal(HostCapabilities.Vision, required);
    }

    /// <summary>
    ///     Proves that the access policy a composer supplies reaches the pack creating tools.
    /// </summary>
    [Fact]
    public void ToolPack_CreateTools_SuppliedPolicy_IsHandedToThePack()
    {
        // Arrange: a pack and the policy its tools must observe
        var pack = new StubToolPack("text_file", tools: []);
        var policy = new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

        // Act: ask the pack for its tools
        pack.CreateTools(policy);

        // Assert: the pack builds its tools against the policy it was given, not one of its own
        Assert.Same(policy, pack.LastPolicy);
    }

    /// <summary>
    ///     Proves that the zero capability value is <see cref="HostCapabilities.None"/>.
    /// </summary>
    /// <remarks>
    ///     A flags enumeration needs a zero member, and naming it None is what lets a pack say
    ///     "I need nothing" without a separate flag meaning "unspecified".
    /// </remarks>
    [Fact]
    public void ToolPack_HostCapabilities_None_IsZero()
    {
        // Act: read the zero value
        var none = HostCapabilities.None;

        // Assert: the default of the enumeration is the no-capability value
        Assert.Equal(0, (int)none);
        Assert.Equal(HostCapabilities.None, default(HostCapabilities));
    }

    /// <summary>
    ///     Proves that requiring no capability is satisfied whatever the host declares.
    /// </summary>
    [Fact]
    public void ToolPack_HostCapabilities_NoneRequirement_IsSatisfiedByEveryDeclaration()
    {
        // Arrange: the requirement a pack that needs nothing declares
        const HostCapabilities required = HostCapabilities.None;

        // Act: test it against a host declaring nothing and a host declaring vision
        var barestHost = (HostCapabilities.None & required) == required;
        var visionHost = (HostCapabilities.Vision & required) == required;

        // Assert: "always available" needs no special case in the gating rule
        Assert.True(barestHost);
        Assert.True(visionHost);
    }

    /// <summary>
    ///     Proves that the capability enumeration is declared as a flags enumeration.
    /// </summary>
    /// <remarks>
    ///     Without the attribute the members would not be combinable and a combined value would
    ///     render as a bare number, so the attribute is part of the contract rather than
    ///     decoration.
    /// </remarks>
    [Fact]
    public void ToolPack_HostCapabilities_Enum_IsDeclaredAsFlags()
    {
        // Act: look for the attribute that makes the members combinable
        var attribute = typeof(HostCapabilities).GetCustomAttribute<FlagsAttribute>();

        // Assert: capabilities combine rather than exclude one another
        Assert.NotNull(attribute);
    }

    /// <summary>
    ///     Proves that every capability member is zero or a single bit.
    /// </summary>
    /// <remarks>
    ///     This is the guard on future members: a member that is not a power of two would
    ///     overlap another and silently satisfy a requirement the host never declared.
    /// </remarks>
    [Fact]
    public void ToolPack_HostCapabilities_EveryMember_IsZeroOrASingleBit()
    {
        // Act: examine every declared member
        var values = Enum.GetValues<HostCapabilities>();

        // Assert: no member overlaps another
        Assert.All(values, value =>
        {
            var bits = (int)value;
            Assert.True(bits == 0 || (bits & (bits - 1)) == 0);
        });
    }

    /// <summary>
    ///     Proves that the vision capability exists and is distinct from requiring nothing.
    /// </summary>
    [Fact]
    public void ToolPack_HostCapabilities_Vision_IsDefinedAndDistinctFromNone()
    {
        // Act: read the capability an image pack requires
        var vision = HostCapabilities.Vision;

        // Assert: declaring vision is a real declaration, not the absence of one
        Assert.True(Enum.IsDefined(vision));
        Assert.NotEqual(HostCapabilities.None, vision);
    }

    /// <summary>
    ///     Proves that the delegation capability exists, is distinct from requiring nothing, and is
    ///     a separate bit from vision, so a host can declare either without implying the other.
    /// </summary>
    [Fact]
    public void ToolPack_HostCapabilities_Delegation_IsDefinedAndIndependentOfVision()
    {
        // Act: read the capability a delegating pack requires
        var delegation = HostCapabilities.Delegation;

        // Assert: a real, independent declaration that composes with vision rather than replacing it
        Assert.True(Enum.IsDefined(delegation));
        Assert.NotEqual(HostCapabilities.None, delegation);
        Assert.NotEqual(HostCapabilities.Vision, delegation);
        Assert.Equal(HostCapabilities.None, delegation & HostCapabilities.Vision);

        // A host that declares both satisfies each requirement on its own
        var both = HostCapabilities.Vision | HostCapabilities.Delegation;
        Assert.Equal(delegation, both & delegation);
        Assert.Equal(HostCapabilities.Vision, both & HostCapabilities.Vision);
    }
}
