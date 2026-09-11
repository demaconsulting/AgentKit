using AgentKitCore;
using DemaConsulting.AgentKit.Core;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     System-level integration tests for the AgentKitCore system.
/// </summary>
public class AgentKitCoreTests
{
    /// <summary>
    ///     Proves that the system can be instantiated and provides expected functionality
    ///     when integrated with all components.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemIntegration_DefaultConstruction_ReturnsExpectedGreeting()
    {
        // Arrange: set up system-level integration test
        var demo = new Demo();
        const string testName = "System";

        // Act: exercise system functionality end-to-end
        var result = demo.DemoMethod(testName);

        // Assert: system produces expected integrated behavior
        Assert.Equal("Hello, System!", result);
    }

    /// <summary>
    ///     Proves that the system handles configuration and customization properly
    ///     across all integrated components.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemCustomization_CustomPrefix_ReturnsExpectedGreeting()
    {
        // Arrange: set up system with custom configuration
        const string customPrefix = "Welcome";
        var demo = new Demo(customPrefix);
        const string testName = "Integration";

        // Act: exercise system with custom configuration
        var result = demo.DemoMethod(testName);

        // Assert: system respects configuration across components
        Assert.Equal("Welcome, Integration!", result);
    }

    /// <summary>
    ///     Proves that the system rejects null input passed to DemoMethod
    ///     with the expected exception at the system level.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemValidation_DemoMethodNullInput_ThrowsArgumentNullException()
    {
        // Arrange: set up system components
        var demo = new Demo();

        // Act & Assert: system validates DemoMethod null input properly
        Assert.Throws<ArgumentNullException>(() => demo.DemoMethod(null!));
    }

    /// <summary>
    ///     Proves that the system rejects empty input passed to DemoMethod
    ///     with the expected exception at the system level.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemValidation_DemoMethodEmptyInput_ThrowsArgumentException()
    {
        // Arrange: set up system components
        var demo = new Demo();

        // Act & Assert: system validates DemoMethod empty input properly
        Assert.Throws<ArgumentException>(() => demo.DemoMethod(string.Empty));
    }

    /// <summary>
    ///     Proves that the system rejects a null constructor prefix
    ///     with the expected exception at the system level.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemValidation_ConstructorNullPrefix_ThrowsArgumentNullException()
    {
        // Act & Assert: system validates constructor null prefix properly
        Assert.Throws<ArgumentNullException>(() => new Demo(null!));
    }

    /// <summary>
    ///     Proves that the system rejects an empty constructor prefix
    ///     with the expected exception at the system level.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemValidation_ConstructorEmptyPrefix_ThrowsArgumentException()
    {
        // Act & Assert: system validates constructor empty prefix properly
        Assert.Throws<ArgumentException>(() => new Demo(string.Empty));
    }

    /// <summary>
    ///     Proves that the Prefix property exposes the configured prefix at the system level.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemIntegration_CustomPrefix_ExposesPrefix()
    {
        // Arrange: construct system with a custom prefix
        const string customPrefix = "Greetings";
        var demo = new Demo(customPrefix);

        // Act: read the Prefix property through the public API
        var prefix = demo.Prefix;

        // Assert: the system exposes the configured prefix correctly
        Assert.Equal(customPrefix, prefix);
    }

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
}
