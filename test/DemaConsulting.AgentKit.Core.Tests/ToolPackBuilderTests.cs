using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     Unit tests for the <see cref="ToolPackBuilder"/> class.
/// </summary>
public class ToolPackBuilderTests
{
    /// <summary>
    ///     Creates an unrestricted access policy for scenarios that do not exercise containment.
    /// </summary>
    /// <returns>The policy.</returns>
    private static PathPolicy AnyPolicy() =>
        new(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);

    /// <summary>
    ///     Proves that a builder cannot exist without an access policy.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Constructor_NullPolicy_ThrowsArgumentNullException()
    {
        // Act & Assert: an unguarded builder would compose unguarded tools
        Assert.Throws<ArgumentNullException>(() => new ToolPackBuilder(null!));
    }

    /// <summary>
    ///     Proves that the policy the builder was constructed with reaches every pack.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_ConstructedPolicy_IsHandedToEveryPack()
    {
        // Arrange: two packs and one policy
        var policy = AnyPolicy();
        var first = new StubToolPack("text_file", tools: []);
        var second = new StubToolPack("image", tools: []);

        // Act: compose
        new ToolPackBuilder(policy).Add(first).Add(second).Build();

        // Assert: neither pack invented a policy of its own
        Assert.Same(policy, first.LastPolicy);
        Assert.Same(policy, second.LastPolicy);
    }

    /// <summary>
    ///     Proves that every tool of every supported pack reaches the composed list.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_SatisfiedPacks_ContributeEveryTool()
    {
        // Arrange: two packs publishing three tools between them
        var textFile = new StubToolPack(
            "text_file",
            tools: [StubToolPack.Tool("text_file_read"), StubToolPack.Tool("text_file_create")]);
        var image = new StubToolPack("image", tools: [StubToolPack.Tool("image_read")]);

        // Act: compose
        var tools = new ToolPackBuilder(AnyPolicy()).Add(textFile).Add(image).Build();

        // Assert: composition is the union of the packs, not a selection from them
        Assert.Equal(3, tools.Count);
    }

    /// <summary>
    ///     Proves that tools appear in the order the packs were added and, within a pack, in the
    ///     order it produced them.
    /// </summary>
    /// <remarks>
    ///     The order a model sees is observable, so it is the caller's stated order rather than
    ///     an incidental one.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_MultiplePacks_PreservesAddOrderAndPackOrder()
    {
        // Arrange: a pack publishing two tools in a deliberate order, and a second pack
        var textFile = new StubToolPack(
            "text_file",
            tools: [StubToolPack.Tool("text_file_read"), StubToolPack.Tool("text_file_create")]);
        var image = new StubToolPack("image", tools: [StubToolPack.Tool("image_read")]);

        // Act: compose
        var tools = new ToolPackBuilder(AnyPolicy()).Add(textFile).Add(image).Build();

        // Assert: the exact sequence, not merely the set
        Assert.Equal(
            ["text_file_read", "text_file_create", "image_read"],
            tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that composing nothing yields an empty list rather than a failure.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_NoPacks_ReturnsEmptyList()
    {
        // Act: build without adding anything
        var tools = new ToolPackBuilder(AnyPolicy()).Build();

        // Assert: an application that attaches no packs simply offers no tools
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that a pack needing a capability the host did not declare contributes nothing.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_PackRequiringUndeclaredCapability_ContributesNoTools()
    {
        // Arrange: an image pack offered to a host that cannot see
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose for a host declaring nothing
        var tools = new ToolPackBuilder(AnyPolicy()).Add(image).Build();

        // Assert: the model is never offered a tool it cannot use
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that an unsupported pack is not even asked to create its tools.
    /// </summary>
    /// <remarks>
    ///     This is the difference between "not registered" and "registered then refused": the
    ///     tools are never built, so there is nothing that could leak into the list.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_PackRequiringUndeclaredCapability_IsNotAskedToCreateTools()
    {
        // Arrange: an image pack offered to a host that cannot see
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose for a host declaring nothing
        new ToolPackBuilder(AnyPolicy()).Add(image).Build();

        // Assert: the pack was skipped, not filtered afterwards
        Assert.Equal(0, image.CreateToolsCallCount);
    }

    /// <summary>
    ///     Proves that a pack whose capability the host declared contributes its tools.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_PackRequiringDeclaredCapability_ContributesItsTools()
    {
        // Arrange: an image pack offered to a host that can see
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose for a host declaring vision
        var tools = new ToolPackBuilder(AnyPolicy())
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(image)
            .Build();

        // Assert: gating withholds nothing the host can support
        Assert.Single(tools);
    }

    /// <summary>
    ///     Proves that a pack requiring no capability is registered by every host.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_PackRequiringNoCapability_IsRegisteredByEveryHost()
    {
        // Arrange: a pack that needs nothing of its host
        var textFile = new StubToolPack(
            "text_file",
            HostCapabilities.None,
            [StubToolPack.Tool("text_file_read")]);

        // Act: compose for the barest possible host
        var tools = new ToolPackBuilder(AnyPolicy()).Add(textFile).Build();

        // Assert: "requires none" is the natural expression of "always available"
        Assert.Single(tools);
    }

    /// <summary>
    ///     Proves that a builder no one has spoken to declares no capabilities.
    /// </summary>
    /// <remarks>
    ///     The default is deliberately the closed one: a host that forgets to declare gets
    ///     fewer tools, never more.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_NoCapabilitiesDeclared_RegistersOnlyPacksRequiringNone()
    {
        // Arrange: one pack needing nothing, one needing vision
        var textFile = new StubToolPack(
            "text_file",
            HostCapabilities.None,
            [StubToolPack.Tool("text_file_read")]);
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose without declaring anything
        var tools = new ToolPackBuilder(AnyPolicy()).Add(textFile).Add(image).Build();

        // Assert: only the undemanding pack is registered
        Assert.Equal(["text_file_read"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a second capability declaration replaces the first rather than adding to
    ///     it.
    /// </summary>
    /// <remarks>
    ///     A declaration states what the host is, and a host has one answer. Combining
    ///     successive calls would make the declaration a ratchet no caller could narrow.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_WithHostCapabilities_CalledTwice_LastDeclarationReplacesTheFirst()
    {
        // Arrange: an image pack, and a host that declares vision and then corrects itself
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: declare vision, then declare none
        var tools = new ToolPackBuilder(AnyPolicy())
            .WithHostCapabilities(HostCapabilities.Vision)
            .WithHostCapabilities(HostCapabilities.None)
            .Add(image)
            .Build();

        // Assert: the correction took effect, so a declaration can be narrowed
        Assert.Empty(tools);
    }

    /// <summary>
    ///     Proves that the capability declaration governs registration whenever it is made.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_WithHostCapabilities_DeclaredAfterAdd_StillGovernsRegistration()
    {
        // Arrange: an image pack added before the host says anything about itself
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: add first, declare afterwards
        var tools = new ToolPackBuilder(AnyPolicy())
            .Add(image)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Build();

        // Assert: gating happens at build time, so call order does not matter
        Assert.Single(tools);
    }

    /// <summary>
    ///     Proves that two packs claiming one family prefix are refused.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Add_SecondPackWithSameFamilyPrefix_ThrowsArgumentException()
    {
        // Arrange: a builder that already has a text file pack
        var builder = new ToolPackBuilder(AnyPolicy()).Add(new StubToolPack("text_file"));

        // Act & Assert: the collision is refused at the call that caused it
        Assert.Throws<ArgumentException>(() => builder.Add(new StubToolPack("text_file")));
    }

    /// <summary>
    ///     Proves that adding the same pack instance twice is a collision like any other.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Add_SamePackInstanceTwice_ThrowsArgumentException()
    {
        // Arrange: one pack and a builder that already holds it
        var pack = new StubToolPack("text_file");
        var builder = new ToolPackBuilder(AnyPolicy()).Add(pack);

        // Act & Assert: adding it again would publish every one of its tools twice
        Assert.Throws<ArgumentException>(() => builder.Add(pack));
    }

    /// <summary>
    ///     Proves that prefix comparison is ordinal.
    /// </summary>
    /// <remarks>
    ///     Recorded as a deliberate boundary rather than an oversight. Tool names are lowercase
    ///     by convention and every model provider compares them exactly, so a prefix
    ///     differing only in case cannot produce a name the guarded factory would accept; it
    ///     fails there, with a message about the name, rather than here with a message about a
    ///     collision that is not one.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Add_PrefixDifferingOnlyByCase_IsNotACollision()
    {
        // Arrange: a builder that already has a text file pack
        var builder = new ToolPackBuilder(AnyPolicy()).Add(new StubToolPack("text_file"));

        // Act: add a pack whose prefix differs only in case
        var result = builder.Add(new StubToolPack("Text_File"));

        // Assert: ordinal comparison, so this is not treated as the same family
        Assert.Same(builder, result);
    }

    /// <summary>
    ///     Proves that a pack publishing a tool outside its declared family is refused.
    /// </summary>
    /// <remarks>
    ///     The collision check at add time is only meaningful if the declaration is true. The
    ///     guarded factory validates that a name is well formed, not that it belongs to the pack
    ///     publishing it, so this check is additive rather than duplicated.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_ToolNameOutsideDeclaredFamily_ThrowsInvalidOperationException()
    {
        // Arrange: a pack declaring one family and publishing a tool from another
        var builder = new ToolPackBuilder(AnyPolicy())
            .Add(new StubToolPack("text_file", tools: [StubToolPack.Tool("image_read")]));

        // Act & Assert: the declaration is enforced, not trusted
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    /// <summary>
    ///     Proves that a tool carrying the declared prefix is accepted.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_ToolNameCarryingDeclaredPrefix_IsAccepted()
    {
        // Arrange: a pack publishing a tool from its own family
        var builder = new ToolPackBuilder(AnyPolicy())
            .Add(new StubToolPack("text_file", tools: [StubToolPack.Tool("text_file_read")]));

        // Act: compose
        var tools = builder.Build();

        // Assert: the agreement check rejects nothing legitimate
        Assert.Equal(["text_file_read"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a missing pack is refused.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Add_NullPack_ThrowsArgumentNullException()
    {
        // Arrange: an empty builder
        var builder = new ToolPackBuilder(AnyPolicy());

        // Act & Assert: a programming error in the composing application
        Assert.Throws<ArgumentNullException>(() => builder.Add(null!));
    }

    /// <summary>
    ///     Proves that a pack with no family prefix is refused.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Add_EmptyFamilyPrefix_ThrowsArgumentException()
    {
        // Arrange: an empty builder
        var builder = new ToolPackBuilder(AnyPolicy());

        // Act & Assert: a pack owning no prefix could not own any of its tool names
        Assert.Throws<ArgumentException>(() => builder.Add(new StubToolPack(string.Empty)));
    }

    /// <summary>
    ///     Proves that a pack returning no tool collection is refused.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_PackReturningNullCollection_ThrowsInvalidOperationException()
    {
        // Arrange: a pack that returns nothing at all rather than an empty collection
        var builder = new ToolPackBuilder(AnyPolicy()).Add(new StubToolPack("text_file"));

        // Act & Assert: the contract is enforced across the package boundary
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    /// <summary>
    ///     Proves that a pack returning a null tool is refused.
    /// </summary>
    /// <remarks>
    ///     A null tool reaching the runtime's tool list fails far from the pack that produced
    ///     it, and the resulting error names nothing a developer could act on.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_PackReturningNullTool_ThrowsInvalidOperationException()
    {
        // Arrange: a pack whose collection contains a hole
        var tools = new AIFunction[] { null! };
        var builder = new ToolPackBuilder(AnyPolicy())
            .Add(new StubToolPack("text_file", tools: tools));

        // Act & Assert: refused where the cause is still identifiable
        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    /// <summary>
    ///     Proves that building twice yields the same composition.
    /// </summary>
    /// <remarks>
    ///     A builder that emptied itself would return an empty list on a second call, which is
    ///     a trap rather than a discipline.
    /// </remarks>
    [Fact]
    public void ToolPackBuilder_Build_CalledTwice_ReturnsTheSameComposition()
    {
        // Arrange: a builder holding one pack
        var builder = new ToolPackBuilder(AnyPolicy())
            .Add(new StubToolPack("text_file", tools: [StubToolPack.Tool("text_file_read")]));

        // Act: build twice
        var first = builder.Build();
        var second = builder.Build();

        // Assert: the second call reports the same composition as the first
        Assert.Equal(first.Select(tool => tool.Name), second.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that a pack added after a build appears in the next one.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Build_PackAddedAfterAnEarlierBuild_AppearsInTheNextBuild()
    {
        // Arrange: a builder that has already been built once
        var builder = new ToolPackBuilder(AnyPolicy())
            .Add(new StubToolPack("text_file", tools: [StubToolPack.Tool("text_file_read")]));
        builder.Build();

        // Act: add another pack and build again
        builder.Add(new StubToolPack("image", tools: [StubToolPack.Tool("image_read")]));
        var tools = builder.Build();

        // Assert: each build reports the builder's state as it is at that call
        Assert.Equal(["text_file_read", "image_read"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves that adding a pack returns the same builder, so calls can be chained.
    /// </summary>
    [Fact]
    public void ToolPackBuilder_Add_ValidPack_ReturnsTheSameBuilder()
    {
        // Arrange: a builder and a pack
        var builder = new ToolPackBuilder(AnyPolicy());

        // Act: add the pack
        var result = builder.Add(new StubToolPack("text_file", tools: []));

        // Assert: attaching a pack costs the consumer one line
        Assert.Same(builder, result);
    }
}
