using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     System-level integration tests for the AgentKitCore system.
/// </summary>
public class AgentKitCoreTests
{
    /// <summary>
    ///     Proves that the system judges access by the real location of a path, refusing a file
    ///     that is only reachable by following a link out of the permitted location.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathContainment_FileBeneathDirectoryLink_IsDenied()
    {
        // Arrange: a system configured for one location, with a link escaping it
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var requested = Path.Combine(policy.ReadRule.Root!, "junction", "secret.txt");

        // Act: request the escaping path through the public API
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: the system refuses and hands back no location
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that the system applies the same containment decision to enumeration as to
    ///     direct access, so an escaped file is never listed.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathContainment_EnumerationAcrossLink_ExcludesEscapedFile()
    {
        // Arrange: a permitted file inside the location and a secret reachable through a link
        using var fixture = new ReparsePointFixture();
        ReparsePointFixture.WriteFile(fixture.Root, "inside.txt", "inside-content");
        ReparsePointFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        fixture.CreateDirectoryLink("junction", fixture.Outside);
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));

        // Act: list the permitted location through the public API
        var listed = policy
            .EnumerateFiles(policy.ReadRule.Root!, "*")
            .Select(Path.GetFileName)
            .ToArray();

        // Assert: the permitted file is listed and the escaped file is not
        Assert.Contains("inside.txt", listed);
        Assert.DoesNotContain("secret.txt", listed);
    }

    /// <summary>
    ///     Proves that the system can be configured to read widely while writing narrowly.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_ReadWideWriteNarrow_AllowsReadDeniesWrite()
    {
        // Arrange: unrestricted reads paired with writes confined to one location
        using var fixture = new ReparsePointFixture();
        var target = ReparsePointFixture.WriteFile(fixture.Outside, "reference.txt", "content");
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Rooted(fixture.Root));

        // Act: read and then attempt to write the same location
        var readPermitted = policy.TryResolveRead(target, out _, out _);
        var writePermitted = policy.TryResolveWrite(target, out _, out _);

        // Assert: the two rules act independently of one another
        Assert.True(readPermitted);
        Assert.False(writePermitted);
    }

    /// <summary>
    ///     Proves that the system reports a refused path as a returned denial rather than by
    ///     throwing.
    /// </summary>
    /// <remarks>
    ///     An exception at a tool call would end the agent's turn; a returned denial lets the
    ///     agent adapt and try a permitted path instead.
    /// </remarks>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DeniedPath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a system confined to one location
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request a path outside the permitted location
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: refusal arrives as a return value carrying a reason
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotEmpty(denialMessage!);
    }

    /// <summary>
    ///     Proves that a denial message the system produces carries no host locations.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DenialMessage_ContainsNoHostPaths()
    {
        // Arrange: a system confined to one location
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request a path outside the permitted location
        policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: neither the request, the permitted location, nor any path fragment appears
        Assert.NotNull(denialMessage);
        Assert.DoesNotContain(requested, denialMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(policy.ReadRule.Root!, denialMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.DirectorySeparatorChar.ToString(),
            denialMessage,
            StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the system refuses to create a path access policy without both rules.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_ConstructionWithoutRules_IsRejected()
    {
        // Act & Assert: neither rule may be omitted, so an unguarded policy cannot exist
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(null!, PathRule.Unrestricted()));
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(PathRule.Unrestricted(), null!));
    }

    /// <summary>
    ///     Proves that the ceilings a tool observes reach it through the access policy a host
    ///     actually builds, and are the published ones when the host configures nothing.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolLimits_PolicyCarriesDefaultLimits_ExposesPublishedValues()
    {
        // Arrange: a system configured for one location, with no opinion about ceilings
        using var fixture = new ReparsePointFixture();

        // Act: build the policy a host would build and read the ceilings it carries
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var limits = policy.Limits;

        // Assert: every tool this policy governs observes the published budget
        Assert.Equal(65536, limits.MaxReadBytes);
        Assert.Equal(32000, limits.MaxResultCharacters);
        Assert.Equal(8388608, limits.MaxBinaryBytes);
        Assert.Equal(4, limits.MaxAttachmentsPerTurn);
    }

    /// <summary>
    ///     Proves that an image a tool returns reaches the runtime as content rather than as
    ///     serialized JSON.
    /// </summary>
    /// <remarks>
    ///     The tool delegate is declared <c>Task&lt;object&gt;</c> deliberately: that is the
    ///     declared return type a tool returning a result union necessarily has, and it is the
    ///     case the underlying factory would serialize. A strongly-typed declaration would pass
    ///     without the guard and would prove nothing.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent()
    {
        // Arrange: a tool built the only supported way, returning a captioned image
        var bytes = new byte[] { 1, 2, 3 };
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(
                ToolResult.Image(bytes, "image/png", "A screenshot of the failing dialog."))),
            ToolName.Create("image_file", "read"),
            "Reads an image file and returns it with a caption.");

        // Act: invoke the tool through the runtime's own entry point
        var result = await function.InvokeAsync(
            new AIFunctionArguments(),
            TestContext.Current.CancellationToken);

        // Assert: the caption and the image both survive as content the provider can recognize
        var parts = Assert.IsAssignableFrom<IList<AIContent>>(result);
        Assert.Equal(2, parts.Count);
        Assert.IsType<TextContent>(parts[0]);
        Assert.IsType<DataContent>(parts[1]);
    }

    /// <summary>
    ///     Proves that a tool refused by the access policy returns a refusal to the model
    ///     instead of throwing.
    /// </summary>
    /// <remarks>
    ///     An exception raised at a tool call ends the agent's turn; a returned refusal lets
    ///     the model read the reason and choose a permitted path. This exercises the access
    ///     policy, the result constructors and the guarded factory together.
    /// </remarks>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemGuardedTool_DeniedPath_ReturnsDenialResultNotException()
    {
        // Arrange: a tool governed by a policy confined to one location
        using var fixture = new ReparsePointFixture();
        var policy = new PathPolicy(PathRule.Rooted(fixture.Root), PathRule.Rooted(fixture.Root));
        var function = GuardedToolFactory.Create(
            (Func<string, Task<object>>)(path => Task.FromResult(
                policy.TryResolveRead(path, out var realPath, out var denialMessage)
                    ? ToolResult.Text(realPath)
                    : ToolResult.Denied(DenialReason.PathNotPermitted, denialMessage))),
            ToolName.Create("text_file", "read"),
            "Reads a text file within the permitted location.");
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: ask the tool for a location outside the permitted one
        var result = await function.InvokeAsync(
            new AIFunctionArguments { ["path"] = requested },
            TestContext.Current.CancellationToken);

        // Assert: the call completes and the refusal arrives as text the model can act on
        var text = Assert.IsType<string>(result);
        Assert.Contains("PathNotPermitted", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the system refuses a tool name that would collide with the Agent
    ///     Framework's bare file access names.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolNaming_BareFileAccessName_IsRejected()
    {
        // Act & Assert: the only supported construction path will not issue a colliding name
        Assert.Throws<ArgumentException>(
            () => GuardedToolFactory.Create(
                (Func<object>)(() => ToolResult.Text("ok")),
                "read",
                "Reads a file."));
    }

    /// <summary>
    ///     Proves that a tool built the only supported way carries the validated name and the
    ///     description the model needs to choose it.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemGuardedTool_ConstructedTool_CarriesValidatedNameAndDescription()
    {
        // Arrange: the name and description a host would publish to the model
        var name = ToolName.Create("text_file", "read");
        const string description = "Reads a text file and returns its contents.";

        // Act: construct the tool through the only supported path
        var function = GuardedToolFactory.Create(
            (Func<object>)(() => ToolResult.Text("ok")),
            name,
            description);

        // Assert: both reach the model, so the tool is selectable rather than anonymous
        Assert.Equal("text_file_read", function.Name);
        Assert.Equal(description, function.Description);
    }

    /// <summary>
    ///     Proves that a host lacking a capability is never offered the tools that need it.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolPacks_HostWithoutCapability_PackContributesNoTools()
    {
        // Arrange: a policy, a pack that needs nothing, and a pack that needs vision
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var textFile = new StubToolPack(
            "text_file",
            HostCapabilities.None,
            [StubToolPack.Tool("text_file_read")]);
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose for a host that cannot accept image content
        var tools = new ToolPackBuilder(policy).Add(textFile).Add(image).Build();

        // Assert: the model never sees the tool, and the tool was never even built
        Assert.Equal(["text_file_read"], tools.Select(tool => tool.Name));
        Assert.Equal(0, image.CreateToolsCallCount);
    }

    /// <summary>
    ///     Proves that a capable host receives every tool of every pack it attaches.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolPacks_CapableHost_ReceivesEveryAttachedTool()
    {
        // Arrange: a policy and two packs, one of which needs vision
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var textFile = new StubToolPack(
            "text_file",
            HostCapabilities.None,
            [StubToolPack.Tool("text_file_read"), StubToolPack.Tool("text_file_write")]);
        var image = new StubToolPack(
            "image",
            HostCapabilities.Vision,
            [StubToolPack.Tool("image_read")]);

        // Act: compose for a host declaring vision
        var tools = new ToolPackBuilder(policy)
            .WithHostCapabilities(HostCapabilities.Vision)
            .Add(textFile)
            .Add(image)
            .Build();

        // Assert: every tool, in the order the application attached its packs
        Assert.Equal(
            ["text_file_read", "text_file_write", "image_read"],
            tools.Select(tool => tool.Name));
        Assert.Same(policy, image.LastPolicy);
    }

    /// <summary>
    ///     Proves that the system refuses two packs claiming one family prefix.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolPacks_CollidingFamilyPrefixes_AreRejected()
    {
        // Arrange: an application attaching two packs that both claim one family
        var policy = new PathPolicy(PathRule.Unrestricted(), PathRule.Unrestricted());
        var builder = new ToolPackBuilder(policy).Add(new StubToolPack("text_file", tools: []));

        // Act & Assert: ambiguous tool names are refused where the application composed them
        Assert.Throws<ArgumentException>(
            () => builder.Add(new StubToolPack("text_file", tools: [])));
    }
}
