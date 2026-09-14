namespace DemaConsulting.AgentKit.Samples.ResearchAssistant.Tests;

/// <summary>
///     Unit tests for the research-assistant sample's command-line surface.
/// </summary>
/// <remarks>
///     The options object performs no I/O and constructs nothing, so these scenarios can pin the
///     whole of its behavior directly: what is required, what defaults, what is rejected, and — the
///     part that matters for an unattended run — how a credential is resolved without ever being
///     printed.
/// </remarks>
public class CommandLineOptionsTests
{
    /// <summary>
    ///     Proves the corpus is required, because there is nothing to anchor or grant without it.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_NoCorpus_ThrowsCommandLineException()
    {
        // Arrange / Act / Assert: a run with no corpus names its own mistake
        var error = Assert.Throws<CommandLineException>(() => CommandLineOptions.Parse(["--provider", "ollama"]));
        Assert.Contains("--corpus", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unrecognized argument is rejected rather than ignored.
    /// </summary>
    /// <remarks>
    ///     Ignoring it would let a misspelled <c>--corpus</c> run the agent over some other folder,
    ///     which is the kind of mistake that is only noticed after the fact.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_Parse_UnknownArgument_ThrowsCommandLineException()
    {
        // Arrange / Act / Assert: an unknown flag is a usage error
        var error = Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--corpus", "docs", "--corpse", "docs"]));
        Assert.Contains("--corpse", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unknown embedding backend is named rather than silently defaulted.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_UnknownEmbeddingBackend_ThrowsCommandLineException()
    {
        // Arrange / Act / Assert: the backend is the application's decision, so a typo in it is
        // reported rather than resolved to whichever backend happens to be the default
        var error = Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--corpus", "docs", "--embeddings", "onnx"]));

        Assert.Multiple(
            () => Assert.Contains("onnx", error.Message, StringComparison.Ordinal),
            () => Assert.Contains("local", error.Message, StringComparison.Ordinal),
            () => Assert.Contains("ollama", error.Message, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a defaulted run is the one a reader can execute with nothing installed.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_OnlyCorpus_DefaultsToOfflineEmbeddingsAndDelegation()
    {
        // Arrange / Act: the shortest useful command line
        var options = CommandLineOptions.Parse(["--corpus", "docs"]);

        // Assert: no server, no credential and no model download are needed to run the sample, and
        // delegation is on so the sample demonstrates all three families by default
        Assert.Multiple(
            () => Assert.Equal("docs", options.Corpus),
            () => Assert.Equal(EmbeddingBackend.Local, options.Embeddings),
            () => Assert.Equal(AgentProvider.Copilot, options.Provider),
            () => Assert.True(options.DelegationEnabled),
            () => Assert.Empty(options.Prompts));
    }

    /// <summary>
    ///     Proves prompts accumulate in order, which is what lets one run span several turns.
    /// </summary>
    /// <remarks>
    ///     This sample's subject is work that outlives a turn — a plan made once and worked through,
    ///     a memory filed in one turn and corrected in another — so a single-prompt mode could not
    ///     demonstrate it.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_Parse_RepeatedPrompts_AreKeptInOrder()
    {
        // Arrange / Act: two prompts, which run as two turns on one session
        var options = CommandLineOptions.Parse(
            ["--corpus", "docs", "--prompt", "first turn", "--prompt", "second turn"]);

        // Assert: both are kept, in the order stated
        Assert.Equal(["first turn", "second turn"], options.Prompts);
    }

    /// <summary>
    ///     Proves a flag missing its value is reported against the flag that is missing it.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_FlagWithoutValue_ThrowsCommandLineException()
    {
        // Arrange / Act / Assert: the following flag is not misread as the value
        var error = Assert.Throws<CommandLineException>(
            () => CommandLineOptions.Parse(["--corpus", "--provider", "ollama"]));
        Assert.Contains("--corpus", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an explicitly supplied token is the one used, whatever the environment holds.
    /// </summary>
    /// <remarks>
    ///     The environment fallback exists for unattended runs and is deliberately second: a run
    ///     that states a credential means that credential.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_ResolveGitHubToken_ExplicitToken_WinsOverTheEnvironment()
    {
        // Arrange: a run stating its own token
        var options = CommandLineOptions.Parse(["--corpus", "docs", "--github-token", "stated-token"]);

        // Act: resolve the credential for the run
        var resolved = options.ResolveGitHubToken();

        // Assert: exactly what was stated
        Assert.Equal("stated-token", resolved);
    }

    /// <summary>
    ///     Proves a blank token is treated as no token at all.
    /// </summary>
    /// <remarks>
    ///     An empty value is how an unattended workflow expresses "no secret was configured".
    ///     Treating it as a credential would turn a clean fallback into an authentication failure
    ///     that reads like a broken sample.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_ResolveGitHubToken_BlankToken_IsNotTreatedAsACredential()
    {
        // Arrange: a stated token that is only whitespace
        var options = new CommandLineOptions { Corpus = "docs", GitHubToken = "   " };

        // Act: resolve the credential, with the environment as the only remaining source
        var resolved = options.ResolveGitHubToken();

        // Assert: whatever the environment says, the blank value was not it
        Assert.NotEqual("   ", resolved);
    }

    /// <summary>
    ///     Proves the help text describes the flags a reader needs in order to run the sample at
    ///     all, including the one that makes it runnable with nothing installed.
    /// </summary>
    [Fact]
    public void CommandLineOptions_HelpText_AnyRun_DescribesTheEmbeddingChoice()
    {
        // Arrange / Act: build the help text
        var help = CommandLineOptions.HelpText();

        // Assert: the embedding backend is the one option unique to this sample, so it must be
        // discoverable without reading the source
        Assert.Multiple(
            () => Assert.Contains("--corpus", help, StringComparison.Ordinal),
            () => Assert.Contains("--embeddings local|ollama", help, StringComparison.Ordinal),
            () => Assert.Contains("--recall-question", help, StringComparison.Ordinal),
            () => Assert.Contains("--no-delegation", help, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves help is answerable even when the rest of the command line is not yet valid.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_HelpWithoutCorpus_RequestsHelpRatherThanFailing()
    {
        // Arrange / Act: a user asking what the options are has not yet supplied them
        var options = CommandLineOptions.Parse(["--help"]);

        // Assert: help short-circuits validation
        Assert.True(options.HelpRequested);
    }

    /// <summary>
    ///     Proves the recall question is parsed and is independent of the ordinary prompts.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_RecallQuestion_IsKeptApartFromThePrompts()
    {
        // Arrange / Act: a run that researches, then asks a question from memory alone
        var options = CommandLineOptions.Parse(
        [
            "--corpus", "corpus",
            "--prompt", "Review every document",
            "--recall-question", "What is the relief valve set to?"
        ]);

        // Assert: the recall question is not a fourth prompt; it runs on its own agent afterwards
        Assert.Multiple(
            () => Assert.Equal(["Review every document"], options.Prompts),
            () => Assert.Equal("What is the relief valve set to?", options.RecallQuestion));
    }

    /// <summary>
    ///     Proves no recall turn is arranged unless one was asked for.
    /// </summary>
    [Fact]
    public void CommandLineOptions_Parse_NoRecallQuestion_LeavesItUnset()
    {
        // Arrange / Act: an ordinary run
        var options = CommandLineOptions.Parse(["--corpus", "corpus"]);

        // Assert: absent, so nothing extra is built and nothing extra is printed
        Assert.Null(options.RecallQuestion);
    }

    /// <summary>
    ///     Proves the Ollama run names the concrete model it will actually use.
    /// </summary>
    /// <remarks>
    ///     The banner previously reported "(provider default)" here, although the sample's own
    ///     default was already known and could be named. A run whose model is unrecorded cannot be
    ///     attributed or reproduced.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_DescribeModel_OllamaWithoutModel_NamesTheSamplesOwnDefault()
    {
        // Arrange: an Ollama run that states no model
        var options = new CommandLineOptions { Corpus = "corpus", Provider = AgentProvider.Ollama };

        // Act: describe the model
        var description = options.DescribeModel();

        // Assert: the name that will go on the wire, not a placeholder
        Assert.Contains(CommandLineOptions.DefaultOllamaModel, description, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Proves an unnamed Copilot model is reported as unknown rather than as a default.
    /// </summary>
    /// <remarks>
    ///     The Copilot runtime resolves the model at session time from what the signed-in user may
    ///     use, and reports nothing back through any surface AgentKit exposes. Saying so — and
    ///     naming the flag that removes the ambiguity — is honest where a placeholder was not.
    /// </remarks>
    [Fact]
    public void CommandLineOptions_DescribeModel_CopilotWithoutModel_SaysItCannotBeKnown()
    {
        // Arrange: a Copilot run that states no model
        var options = new CommandLineOptions { Corpus = "corpus" };

        // Act: describe the model
        var description = options.DescribeModel();

        // Assert: unknown, with the remedy named
        Assert.Multiple(
            () => Assert.Contains("unknown", description, StringComparison.Ordinal),
            () => Assert.Contains("--model", description, StringComparison.Ordinal));
    }

    /// <summary>
    ///     Proves a stated model is reported exactly, whichever provider is in use.
    /// </summary>
    [Fact]
    public void CommandLineOptions_DescribeModel_ModelStated_ReportsItExactly()
    {
        // Arrange: a run that pinned its model
        var options = new CommandLineOptions { Corpus = "corpus", Model = "gpt-5.4-mini" };

        // Act: describe the model
        var description = options.DescribeModel();

        // Assert: the name and nothing added to it
        Assert.Equal("gpt-5.4-mini", description);
    }
}
