using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     System-level integration tests for the AgentKitCore system.
/// </summary>
public class AgentKitCoreTests
{
    /// <summary>
    ///     Proves that the system judges access by the normalized location of a path, refusing a
    ///     file reached by climbing out of the permitted location.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathContainment_RelativeEscape_IsDenied()
    {
        // Arrange: a system configured for one location, with a secret outside it
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: request the escaping path through the public API
        var permitted = policy.TryResolveRead(
            "../outside/secret.txt",
            out var realPath,
            out var denialMessage);

        // Assert: the system refuses and hands back no location
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotNull(denialMessage);
    }

    /// <summary>
    ///     Proves that a listing of the permitted location reports the files it contains and does
    ///     not reach into a sibling directory outside it.
    /// </summary>
    /// <remarks>
    ///     This scenario states the containment boundary of a listing only. The narrower guarantee
    ///     — that a file inside the permitted location which a read would refuse is also excluded —
    ///     is pinned by <c>PathPolicy_EnumerateFiles_DeniedPatternFile_IsNotListed</c>, because a
    ///     sibling directory is never walked and so cannot exercise the per-candidate decision.
    /// </remarks>
    [Fact]
    public void AgentKitCore_SystemPathContainment_Enumeration_ListsOnlyPermittedFiles()
    {
        // Arrange: a permitted file inside the location and a secret in a sibling directory
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "inside.txt", "inside-content");
        TempDirectoryFixture.WriteFile(fixture.Outside, "secret.txt", "outside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: list the permitted location through the public API
        var listed = policy
            .EnumerateFiles(policy.WorkingDirectory, "*")
            .Select(Path.GetFileName)
            .ToArray();

        // Assert: the permitted file is listed and the sibling file is not
        Assert.Contains("inside.txt", listed);
        Assert.DoesNotContain("secret.txt", listed);
    }

    /// <summary>
    ///     Proves that the system can be configured to read widely while writing narrowly.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_ReadWideWriteNarrow_AllowsReadDeniesWrite()
    {
        // Arrange: an unrestricted read grant paired with a read-write grant confined to one place
        using var fixture = new TempDirectoryFixture();
        var target = TempDirectoryFixture.WriteFile(fixture.Outside, "reference.txt", "content");
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.Unrestricted(AccessLevel.ReadOnly), PathRule.ReadWrite(fixture.Root)]);

        // Act: read and then attempt to write the same location
        var readPermitted = policy.TryResolveRead(target, out _, out _);
        var writePermitted = policy.TryResolveWrite(target, out _, out _);

        // Assert: the grants act independently of one another
        Assert.True(readPermitted);
        Assert.False(writePermitted);
    }

    /// <summary>
    ///     Proves that the system reports a refused path as a returned denial rather than by
    ///     throwing.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DeniedPath_ReturnsDenialWithoutThrowing()
    {
        // Arrange: a system confined to one location
        using var fixture = new TempDirectoryFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request a path outside the permitted location
        var permitted = policy.TryResolveRead(requested, out var realPath, out var denialMessage);

        // Assert: refusal arrives as a return value carrying a reason
        Assert.False(permitted);
        Assert.Null(realPath);
        Assert.NotEmpty(denialMessage!);
    }

    /// <summary>
    ///     Proves that a denial message the system produces discloses the permitted location so a
    ///     confined model learns where it may work.
    /// </summary>
    /// <remarks>
    ///     The map is stated relative to the working directory, the form in which a model addresses
    ///     it, so it is complete and usable without stating where the host keeps it.
    /// </remarks>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DenialMessage_DisclosesPermittedLocations()
    {
        // Arrange: a system confined to one location
        using var fixture = new TempDirectoryFixture();
        var policy = CreateRootedPolicy(fixture.Root);
        var requested = Path.Combine(fixture.Outside, "elsewhere.txt");

        // Act: request a path outside the permitted location
        policy.TryResolveRead(requested, out _, out var denialMessage);

        // Assert: the request is echoed and the permitted location is enumerated with its level
        Assert.NotNull(denialMessage);
        Assert.Contains(requested, denialMessage, StringComparison.Ordinal);
        Assert.Contains("  - . (read-write)", denialMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a path stated the way a model states it is resolved against the working
    ///     directory the host configured.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_RelativePathFromModel_ResolvesAgainstWorkingDirectory()
    {
        // Arrange: a system configured for one working directory, holding one file
        using var fixture = new TempDirectoryFixture();
        TempDirectoryFixture.WriteFile(fixture.Root, "notes.txt", "inside-content");
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: request the file the way a model would name it
        var permitted = policy.TryResolveRead("notes.txt", out var realPath, out var denialMessage);

        // Assert: the real file inside the working directory, not a refusal
        Assert.True(permitted);
        Assert.Null(denialMessage);
        Assert.Equal("inside-content", File.ReadAllText(realPath!));
    }

    /// <summary>
    ///     Proves that a denial the system produces tells the model how to recover, by naming what is
    ///     permitted and, when the request was relative, how it was interpreted.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DenialMessage_StatesHowToRecover()
    {
        // Arrange: a system confined to one working directory
        using var fixture = new TempDirectoryFixture();
        var policy = CreateRootedPolicy(fixture.Root);

        // Act: request a relative path that escapes the working directory
        policy.TryResolveRead("../outside/elsewhere.txt", out _, out var denialMessage);

        // Assert: the denial echoes the input, states the interpretation, and names the location
        Assert.NotNull(denialMessage);
        Assert.Contains("Requested: \"../outside/elsewhere.txt\"", denialMessage, StringComparison.Ordinal);
        Assert.Contains(
            "Interpreted as: ../outside/elsewhere.txt (relative to the working directory)",
            denialMessage,
            StringComparison.Ordinal);
        Assert.Contains("  - . (read-write)", denialMessage, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a denial the system produces for a relative request discloses no host
    ///     absolute path: neither the working directory nor any granted location is stated
    ///     absolutely.
    /// </summary>
    /// <remarks>
    ///     A denial joins the transcript, which leaves this process. Relative requests are joined to
    ///     the working directory and normalized before containment is judged, so the relative map is
    ///     fully addressable and the host's absolute layout adds nothing the model needs.
    /// </remarks>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_DenialMessage_DisclosesNoHostAbsolutePath()
    {
        // Arrange: a working directory granted read-only and a sibling granted read-write
        using var fixture = new TempDirectoryFixture();
        Directory.CreateDirectory(fixture.Outside);
        var policy = new PathPolicy(
            fixture.Root,
            [PathRule.ReadOnly(fixture.Root), PathRule.ReadWrite(fixture.Outside)]);

        // Act: a relative write the read-only grant refuses
        policy.TryResolveWrite("notes.txt", out _, out var denialMessage);

        // Assert: the map is complete and relative, and no absolute host location appears
        Assert.NotNull(denialMessage);
        Assert.Contains("  - . (read-only)", denialMessage, StringComparison.Ordinal);
        Assert.Contains("  - ../outside (read-write)", denialMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(policy.WorkingDirectory, denialMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Path.GetTempPath().TrimEnd('\\', '/'), denialMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     Proves that an image a tool returns reaches the provider on a channel it honors, even
    ///     when the provider would drop it from a tool response.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemImagePromotion_ToolImageResult_ReachesTheProviderOnAUserMessage()
    {
        // Arrange: a conversation whose tool result carries the caption-then-image shape a
        // guarded tool produces
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        var image = new DataContent(bytes, "image/png");
        var toolResult = new List<AIContent> { new TextContent("A screenshot."), image };
        using var provider = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(provider);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What does the screenshot show?"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)])
        };

        // Act: send the conversation as a function-invocation loop would
        await client.GetResponseAsync(
            messages,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the provider received the image on a user message, unchanged
        Assert.Equal(3, provider.ReceivedMessages.Count);
        Assert.Equal(ChatRole.User, provider.ReceivedMessages[2].Role);
        Assert.Same(image, Assert.IsType<DataContent>(provider.ReceivedMessages[2].Contents[1]));
    }

    /// <summary>
    ///     Proves that a promoted image leaves only a placeholder in the tool result the provider
    ///     receives, so the image is sent once and cannot flood the context as text.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemImagePromotion_PromotedImage_LeavesOnlyAPlaceholderInTheToolResult()
    {
        // Arrange: a conversation whose tool result carries a large image after a caption
        var image = new DataContent(new byte[250_000], "image/png");
        var toolResult = new List<AIContent> { new TextContent("A screenshot."), image };
        using var provider = new ScriptedChatClient();
        using var client = new ImagePromotingChatClient(provider);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.User, "What does the screenshot show?"),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)])
        };

        // Act: send the conversation as a function-invocation loop would
        await client.GetResponseAsync(
            messages,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the tool result keeps the caption and a short placeholder, and no image content
        var forwarded = Assert.IsType<FunctionResultContent>(Assert.Single(provider.ReceivedMessages[1].Contents));
        var parts = Assert.IsAssignableFrom<IEnumerable<AIContent>>(forwarded.Result).ToList();
        Assert.DoesNotContain(parts, part => part is DataContent);
        var placeholder = Assert.IsType<TextContent>(parts[1]).Text;
        Assert.Contains("image/png", placeholder, StringComparison.Ordinal);
        Assert.True(placeholder.Length < 200, $"Placeholder was {placeholder.Length} characters.");
        Assert.Same(image, Assert.IsType<DataContent>(provider.ReceivedMessages[2].Contents[1]));
    }

    /// <summary>
    ///     Proves that an application can bound a tool the library did not build by the library's
    ///     ceilings, so an oversized result from it is refused rather than reaching the provider.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemForeignTool_GuardedOversizedResult_IsRefused()
    {
        // Arrange: a tool built with the plain function factory, returning 300 structured rows,
        // bounded by a 1,500-character result ceiling
        var report = AIFunctionFactory.Create(
            (int rows) => Enumerable.Range(1, rows).Select(n => new { row = n, square = n * n }),
            "report_rows",
            "Returns one row per number with its square.");
        var bounded = report.AsGuarded(new ToolLimits(maxResultCharacters: 1_500));

        // Act: invoke both as a runtime would
        var arguments = new AIFunctionArguments { ["rows"] = 300 };
        var unbounded = await report.InvokeAsync(arguments, TestContext.Current.CancellationToken);
        var guarded = await bounded.InvokeAsync(arguments, TestContext.Current.CancellationToken);

        // Assert: unwrapped, the result is far beyond the ceiling; bounded, it is refused
        Assert.True(Assert.IsType<System.Text.Json.JsonElement>(unbounded).GetRawText().Length > 1_500);
        Assert.StartsWith("Denied (ResourceTooLarge)", Assert.IsType<string>(guarded), StringComparison.Ordinal);
        Assert.Equal(report.Name, bounded.Name);
    }

    /// <summary>
    ///     Proves that the system refuses to create a path access policy without a working directory,
    ///     and rejects a null grant.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemPathPolicy_ConstructionWithoutWorkingDirectory_IsRejected()
    {
        // Act & Assert: the anchor is required, and a null grant entry is a programming error
        using var fixture = new TempDirectoryFixture();
        Assert.ThrowsAny<ArgumentException>(
            () => new PathPolicy(null!, [PathRule.Unrestricted(AccessLevel.ReadWrite)]));
        Assert.Throws<ArgumentNullException>(
            () => new PathPolicy(fixture.Root, [null!]));
    }

    /// <summary>
    ///     Proves that the ceilings a tool observes reach it through the access policy a host
    ///     actually builds, and are the published ones when the host configures nothing.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolLimits_PolicyCarriesDefaultLimits_ExposesPublishedValues()
    {
        // Arrange: a system configured for one location, with no opinion about ceilings
        using var fixture = new TempDirectoryFixture();

        // Act: build the policy a host would build and read the ceilings it carries
        var policy = CreateRootedPolicy(fixture.Root);
        var limits = policy.Limits;

        // Assert: every tool this policy governs observes the published budget
        Assert.Equal(65536, limits.MaxReadBytes);
        Assert.Equal(32000, limits.MaxResultCharacters);
        Assert.Equal(8388608, limits.MaxBinaryBytes);
        Assert.Equal(2, limits.MaxAgentDepth);
        Assert.Equal(16777216, limits.MaxImagePixels);
        Assert.Equal(1000, limits.MaxDeleteEntries);
    }

    /// <summary>
    ///     Proves that an image a tool returns reaches the runtime as content rather than as
    ///     serialized JSON.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemGuardedTool_ImageResult_ReachesRuntimeAsContent()
    {
        // Arrange: a tool built the only supported way, returning a captioned image
        var bytes = new byte[] { 1, 2, 3 };
        var function = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(
                ToolResult.Image(bytes, "image/png", "A screenshot of the failing dialog."))),
            ToolName.Create("image", "read"),
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
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemGuardedTool_DeniedPath_ReturnsDenialResultNotException()
    {
        // Arrange: a tool governed by a policy confined to one location
        using var fixture = new TempDirectoryFixture();
        var policy = CreateRootedPolicy(fixture.Root);
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
        var policy = CreateUnrestrictedPolicy();
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
        var policy = CreateUnrestrictedPolicy();
        var textFile = new StubToolPack(
            "text_file",
            HostCapabilities.None,
            [StubToolPack.Tool("text_file_read"), StubToolPack.Tool("text_file_create")]);
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
            ["text_file_read", "text_file_create", "image_read"],
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
        var policy = CreateUnrestrictedPolicy();
        var builder = new ToolPackBuilder(policy).Add(new StubToolPack("text_file", tools: []));

        // Act & Assert: ambiguous tool names are refused where the application composed them
        Assert.Throws<ArgumentException>(
            () => builder.Add(new StubToolPack("text_file", tools: [])));
    }

    /// <summary>
    ///     Proves that a retention handle an application owns covers tools wrapped into it at
    ///     different times, so every reference is paged through one pair of readers.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_RetentionHandle_PagesOutputsOfToolsWrappedSeparately()
    {
        // Arrange: one retention for a conversation, a tool wrapped at the start and a foreign tool
        // added later
        var retention = new ToolOutputRetention(new ToolLimits(maxResultCharacters: 2_000));
        var early = retention.Wrap(LargeReportTool());
        var late = retention.Wrap(AIFunctionFactory.Create(
            () => string.Join("\n", Enumerable.Range(1, 500).Select(n => "late " + n)),
            "late_report",
            "A report added later."));
        List<AIFunction> tools = [early, late, .. retention.ReaderTools];

        // Act: both tools retain, and the readers page each reference
        await InvokeAsync(tools, "report_build");
        await InvokeAsync(tools, "late_report");
        var first = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 1,
        }));
        var second = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-2",
            ["startLine"] = 500,
            ["lineCount"] = 1,
        }));

        // Assert: one sequence of references, readable whichever tool issued it
        Assert.StartsWith("output-1 lines 1-1 of 5000", first, StringComparison.Ordinal);
        Assert.Equal("output-2 lines 500-500 of 500\n500| late 500", second);
    }

    /// <summary>
    ///     Proves that a retention exported with a conversation and restored later answers a
    ///     reference the model was given before the restore.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_RestoredRetention_AnswersAnEarlierReference()
    {
        // Arrange: a retention that retained a report, persisted as JSON as a host would
        var limits = new ToolLimits(maxResultCharacters: 2_000);
        var retention = new ToolOutputRetention(limits);
        await InvokeAsync([retention.Wrap(LargeReportTool())], "report_build");
        var json = System.Text.Json.JsonSerializer.Serialize(retention.Export(), ToolOutputRetentionSnapshot.JsonTypeInfo);

        // Act: reload it and read the earlier reference
        var snapshot = System.Text.Json.JsonSerializer.Deserialize(json, ToolOutputRetentionSnapshot.JsonTypeInfo)!;
        var restored = ToolOutputRetention.Restore(snapshot, limits);
        var page = Assert.IsType<string>(await InvokeAsync([.. restored.ReaderTools], ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 5000,
            ["lineCount"] = 1,
        }));

        // Assert: the same content the reference named before the restore
        Assert.StartsWith("output-1 lines 5000-5000 of 5000", page, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a host-owned history over the threshold is consolidated through the injected
    ///     summarizer into one record, keeping the system message and the most recent turns verbatim.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemChatReducer_OverThresholdHistory_IsConsolidatedKeepingTheTail()
    {
        // Arrange: a reducer over a deterministic summarizer, and a long history with a system message
        var summarizer = new FakeSummarizer(request => "The vault code is 4417-BRAVO.");
        IChatReducer reducer = new AgentKitChatReducer(summarizer, thresholdCharacters: 200, verbatimTurns: 1);
        var system = new ChatMessage(ChatRole.System, "You are concise.");
        List<ChatMessage> history = [system, new(ChatRole.User, "Remember: the vault code is 4417-BRAVO.")];
        history.Add(new ChatMessage(ChatRole.Assistant, "Noted."));
        for (var turn = 1; turn <= 4; turn++)
        {
            history.Add(new ChatMessage(ChatRole.User, "Tell me about topic " + turn + "."));
            history.Add(new ChatMessage(ChatRole.Assistant, new string('x', 100)));
        }

        // Act: reduce, as a chat history provider would before retrieval
        var reduced = (await reducer.ReduceAsync(history, TestContext.Current.CancellationToken)).ToList();

        // Assert: system, one record holding the fact, then the last turn; and the material held the fact
        Assert.Equal(4, reduced.Count);
        Assert.Same(system, reduced[0]);
        Assert.Contains("4417-BRAVO", reduced[1].Text, StringComparison.Ordinal);
        Assert.Same(history[^2], reduced[2]);
        Assert.Same(history[^1], reduced[3]);
        Assert.Contains("4417-BRAVO", Assert.Single(summarizer.Requests).Material, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that a composition retaining large outputs turns an oversized custom tool's result
    ///     into a bounded preview whose reference reads back, and searches, the full output.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_OversizedCustomTool_PreviewReferenceReadsBackTheFullOutput()
    {
        // Arrange: a custom tool producing far more than the default result ceiling, composed with
        // retention through the builder
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy())
            .Add(new StubToolPack("report", tools: [LargeReportTool()]))
            .WithToolOutputSpill()
            .Build();

        // Act: invoke the tool, then page to its end and search it through the readers
        var preview = Assert.IsType<string>(await InvokeAsync(tools, "report_build"));
        var tail = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 4999,
            ["lineCount"] = 2,
        }));
        var found = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputSearchTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["pattern"] = "row 2500 ",
        }));

        // Assert: a bounded preview naming the reference, and the readers reach every line of it
        Assert.StartsWith("[Output output-1 from report_build", preview, StringComparison.Ordinal);
        Assert.True(preview.Length <= ToolLimits.DefaultMaxResultCharacters);
        Assert.Equal("output-1 lines 4999-5000 of 5000\n4999| row 4999 of the report\n5000| row 5000 of the report", tail);
        Assert.Equal("2500:row 2500 of the report\n", found);
    }

    /// <summary>
    ///     Proves that a reference survives a session's rotation, because rotation carries the
    ///     composed tool list - and the store it holds - unchanged.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_ReferenceSurvivesSessionRotation()
    {
        // Arrange: a retaining composition, one output retained, and a session over a narrow window
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy())
            .Add(new StubToolPack("report", tools: [LargeReportTool()]))
            .WithToolOutputSpill()
            .Build();
        await InvokeAsync(tools, "report_build");
        var factory = new InMemoryProviderSessionFactory(SessionTestData.SizedResponder(15), windowTokens: 300);
        var options = new AgentSessionOptions(new FakeSummarizer(0.2), tools: tools, verbatimTurns: 2);
        await using var session = await CompactingAgentSession.CreateAsync(
            options, factory, TestContext.Current.CancellationToken);

        // Act: converse until the session rotates, then read the reference through the tools the
        // replacement session was seeded with
        for (var turn = 0; turn < 20 && session.RotationCount == 0; turn++)
        {
            await session.SendAsync(
                SessionTestData.UserOfTokens(15, "q" + turn).Text,
                TestContext.Current.CancellationToken);
        }

        var seeded = factory.Sessions[^1].Seed.Tools;
        var page = Assert.IsType<string>(await InvokeAsync(seeded, ToolOutputReadTool.ToolName, new()
        {
            ["id"] = "output-1",
            ["startLine"] = 1,
            ["lineCount"] = 1,
        }));

        // Assert: the session rotated, and the reference still reads the retained output
        Assert.True(session.RotationCount > 0);
        Assert.Equal("output-1 lines 1-1 of 5000\n   1| row 1 of the report", page);
    }

    /// <summary>
    ///     Proves that retention is bounded: once a reference's output has been evicted, a reader
    ///     answers with the fact that it is no longer retained.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_EvictedReference_IsAnsweredNoLongerRetained()
    {
        // Arrange: a composition retaining one output at a time
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy())
            .Add(new StubToolPack("report", tools: [LargeReportTool()]))
            .WithToolOutputSpill(new ToolOutputOptions(maxRetainedOutputs: 1))
            .Build();

        // Act: retain two outputs, then read the first
        await InvokeAsync(tools, "report_build");
        await InvokeAsync(tools, "report_build");
        var evicted = Assert.IsType<string>(await InvokeAsync(tools, ToolOutputReadTool.ToolName, new() { ["id"] = "output-1" }));

        // Assert: an explicit fact, not a silent miss
        Assert.StartsWith("Denied (TargetNotFound)", evicted, StringComparison.Ordinal);
        Assert.Contains("no longer retained", evicted, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves that the readers accept no path, so retention adds no way to reach the file system.
    /// </summary>
    [Fact]
    public void AgentKitCore_SystemToolOutput_Readers_AcceptNoPath()
    {
        // Arrange / Act: a retaining composition's reader schemas
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy()).WithToolOutputSpill().Build();

        // Assert: only the two readers, neither with a path parameter
        Assert.Equal([ToolOutputReadTool.ToolName, ToolOutputSearchTool.ToolName], tools.Select(tool => tool.Name));
        Assert.All(tools, tool => Assert.False(
            tool.JsonSchema.GetProperty("properties").TryGetProperty("path", out _),
            tool.Name + " accepts a path."));
    }

    /// <summary>
    ///     Proves that without retention a tool declaring its output oversized gives exactly the
    ///     refusal it always gave, so a host that does not opt in observes no change.
    /// </summary>
    /// <returns>A task that completes when the scenario has been verified.</returns>
    [Fact]
    public async Task AgentKitCore_SystemToolOutput_WithoutRetention_OversizedOutputIsRefusedAsBefore()
    {
        // Arrange: a tool declaring its output oversized, composed without retention
        const string message = "The report exceeds the 32000-character result limit.";
        var tool = GuardedToolFactory.Create(
            (Func<Task<object>>)(() => Task.FromResult(ToolResult.Oversized(new string('x', 50_000), message))),
            "report_build",
            "Builds the full report.");
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy())
            .Add(new StubToolPack("report", tools: [tool]))
            .Build();

        // Act: invoke it
        var result = await InvokeAsync(tools, "report_build");

        // Assert: the identical refusal, and no reader was offered
        Assert.Equal(ToolResult.Denied(DenialReason.ResourceTooLarge, message), result);
        Assert.Equal(["report_build"], tools.Select(candidate => candidate.Name));
    }

    /// <summary>
    ///     Proves a vision-only application lists only the models that can see, starts a session only
    ///     on one, and offers the image family only because the selected model accepts images.
    /// </summary>
    /// <remarks>
    ///     The whole pattern an application follows: filter the picker by the requirement, re-enforce
    ///     it when the session starts, and derive the host's vision declaration from the selection.
    /// </remarks>
    [Fact]
    public async Task AgentKitCore_SystemModelSelection_VisionOnlyApp_SelectsAndGatesTheImageFamily()
    {
        // Arrange: a catalog with a vision model, a text model and an unusable one
        const ModelCapabilities required = ModelCapabilities.Vision | ModelCapabilities.ToolCalls;
        var catalog = new ScriptedModelCatalog(
            [ModelTestData.TextModel(), ModelTestData.VisionModel(), ModelTestData.UnusableModel()]);
        var image = new StubToolPack("image", HostCapabilities.Vision, [StubToolPack.Tool("image_read")]);

        // Act: list for the picker, select the vision model, compose its tools
        var listed = await catalog.ListAsync(new ModelQuery { Require = required }, TestContext.Current.CancellationToken);
        var selection = await catalog.SelectAsync(
            new ModelSettings(listed[0].Id) { ReasoningEffort = "high" },
            required,
            TestContext.Current.CancellationToken);
        var tools = new ToolPackBuilder(CreateUnrestrictedPolicy())
            .WithHostCapabilities(selection.HostCapabilities)
            .Add(image)
            .Build();

        // Assert: only the vision model was listed, and the image family was offered to it
        Assert.Equal(["vision-large"], listed.Select(model => model.Id));
        Assert.Equal(["image_read"], tools.Select(tool => tool.Name));
    }

    /// <summary>
    ///     Proves a persisted choice of a model that cannot satisfy the application is refused at
    ///     session start, with a reason a user can read.
    /// </summary>
    [Fact]
    public async Task AgentKitCore_SystemModelSelection_PersistedTextModel_IsRefusedAtSessionStart()
    {
        // Arrange: a vision-only application whose persisted choice is a text model
        var catalog = new ScriptedModelCatalog([ModelTestData.TextModel(), ModelTestData.VisionModel()]);

        // Act
        var failure = await Assert.ThrowsAsync<ModelSettingsException>(
            () => catalog.SelectAsync(new ModelSettings("text-small"), ModelCapabilities.Vision, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            "Model 'text-small' does not support vision, which this application requires.",
            Assert.Single(failure.Problems));
    }

    /// <summary>
    ///     Proves the selected settings reach every request an IChatClient pipeline sends.
    /// </summary>
    [Fact]
    public async Task AgentKitCore_SystemModelSelection_SettingsReachTheProviderOnEveryRequest()
    {
        // Arrange
        var catalog = new ScriptedModelCatalog([ModelTestData.VisionModel()]);
        var selection = await catalog.SelectAsync(
            new ModelSettings("vision-large") { ReasoningEffort = "low", MaxOutputTokens = 2000 },
            cancellationToken: TestContext.Current.CancellationToken);
        var provider = new OptionsCapturingChatClient();
        using var client = new ModelSettingsChatClient(provider, selection);

        // Act: two requests
        await client.GetResponseAsync("one", cancellationToken: TestContext.Current.CancellationToken);
        await client.GetResponseAsync("two", cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, provider.Received.Count);
        Assert.All(provider.Received, options =>
        {
            Assert.Equal("vision-large", options.ModelId);
            Assert.Equal(ReasoningEffort.Low, options.Reasoning?.Effort);
            Assert.Equal(2000, options.MaxOutputTokens);
        });
    }

    /// <summary>
    ///     A chat client that records the options of every request, standing in for a provider.
    /// </summary>
    private sealed class OptionsCapturingChatClient : IChatClient
    {
        /// <summary>
        ///     Gets the options each request arrived with.
        /// </summary>
        public List<ChatOptions> Received { get; } = [];

        /// <inheritdoc/>
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Received.Add(options ?? new ChatOptions());
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        /// <inheritdoc/>
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The scenario sends no streamed request.");

        /// <inheritdoc/>
        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        /// <inheritdoc/>
        public void Dispose()
        {
            // Nothing is held.
        }
    }

    /// <summary>
    ///     Creates a policy whose working directory is also its single read-write grant.
    /// </summary>
    /// <param name="root">The location that is both the anchor and the grant.</param>
    /// <returns>A policy anchored at and granting <paramref name="root"/>.</returns>
    private static PathPolicy CreateRootedPolicy(string root)
    {
        return new PathPolicy(root, [PathRule.ReadWrite(root)]);
    }

    /// <summary>
    ///     Builds a custom tool whose report runs to five thousand lines, well past the default
    ///     result ceiling.
    /// </summary>
    /// <returns>The tool, built the only supported way.</returns>
    private static AIFunction LargeReportTool() => GuardedToolFactory.Create(
        (Func<Task<object>>)(() => Task.FromResult(ToolResult.Text(string.Join(
            "\n",
            Enumerable.Range(1, 5000).Select(n => "row " + n + " of the report"))))),
        "report_build",
        "Builds the full report.");

    /// <summary>
    ///     Invokes the named tool from a composed list, as the runtime would.
    /// </summary>
    /// <param name="tools">The composed list.</param>
    /// <param name="name">The tool to invoke.</param>
    /// <param name="arguments">The arguments, or null for none.</param>
    /// <returns>The tool's result.</returns>
    private static async Task<object?> InvokeAsync(
        IReadOnlyList<AIFunction> tools,
        string name,
        AIFunctionArguments? arguments = null)
    {
        var tool = tools.Single(candidate => candidate.Name == name);
        return await tool.InvokeAsync(arguments ?? new AIFunctionArguments(), TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Creates a policy that grants read-write access everywhere, for tests concerned only with
    ///     tool composition rather than containment.
    /// </summary>
    /// <returns>An unrestricted read-write policy anchored at the temporary directory.</returns>
    private static PathPolicy CreateUnrestrictedPolicy()
    {
        return new PathPolicy(Path.GetTempPath(), [PathRule.Unrestricted(AccessLevel.ReadWrite)]);
    }
}
