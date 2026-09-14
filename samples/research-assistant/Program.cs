using DemaConsulting.AgentKit.Samples.ResearchAssistant;

// Entry point for the research-assistant sample.
//
// The flow is deliberately linear so a reader can follow it top to bottom: parse the command line,
// resolve and validate the two locations, build the provider agent, then run the conversation.
// Provider choice and embedding-backend choice are absorbed entirely inside AgentComposition, so
// nothing here knows or cares which of either is in use.

// Exit codes: 0 success (including a clean Ctrl-C), 2 for a usage error, 1 for an unexpected fault.
const int exitSuccess = 0;
const int exitUnexpected = 1;
const int exitUsage = 2;

try
{
    var options = CommandLineOptions.Parse(args);

    // Help is a terminal, successful action: print and leave without touching a provider.
    if (options.HelpRequested)
    {
        Console.WriteLine(CommandLineOptions.HelpText());
        return exitSuccess;
    }

    // Resolve the corpus to an absolute path so the anchor is unambiguous. It must already exist:
    // it holds the documents being researched, so a missing one is a mistake rather than something
    // to create.
    var corpusRoot = Path.GetFullPath(options.Corpus);
    if (!Directory.Exists(corpusRoot))
    {
        throw new CommandLineException(
            $"The corpus folder '{options.Corpus}' does not exist. " +
            "Pass --corpus <path> pointing at an existing folder of documents.");
    }

    // The notes location is the application's own choice — AgentKit has no such convention — so the
    // sample picks one, prints it, and creates it. Unlike the corpus it is created if absent: an
    // application that asks an agent to write somewhere owns that location existing.
    var notesRoot = Path.GetFullPath(
        options.Notes
        ?? Path.Combine(Path.GetTempPath(), CommandLineOptions.DefaultNotesFolderName));
    Directory.CreateDirectory(notesRoot);

    // Ctrl-C ends the run cleanly rather than killing the process.
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    // Print the whole configuration, because every line of it is something the reader needs in
    // order to interpret what follows: which location carries which permission, which backend
    // produced the vectors behind every recall, and whether delegation was offered at all. The
    // token is reported as present or absent and never printed.
    Console.WriteLine(
        $"Corpus:     {corpusRoot} (read-only, relative paths anchor here)\n" +
        $"Notes:      {notesRoot} (read-write, address it by absolute path)\n" +
        $"Provider:   {options.Provider}\n" +
        $"Model:      {options.DescribeModel()}\n" +
        $"Embeddings: {options.Embeddings}" +
        (options.Embeddings == EmbeddingBackend.Ollama ? $" ({options.EmbeddingModel} at {options.Host})" : " (offline, lexical)") + "\n" +
        $"Delegation: {(options.DelegationEnabled ? "enabled" : "disabled")}\n" +
        $"Auth:       {(options.ResolveGitHubToken() is null ? "logged-in user" : "supplied GitHub token")}");

    // The optional machine-readable tool-call transcript, opened here so the host owns the file
    // handle for exactly as long as the conversation runs.
    using var transcript = options.Transcript is null
        ? null
        : new StreamWriter(options.Transcript, append: false);

    // Build the agent (the one provider-specific step) and always release its runtime resources.
    var setup = await AgentComposition.CreateAgentAsync(
        options,
        corpusRoot,
        notesRoot,
        cancellation.Token);
    await using (setup.Cleanup)
    {
        await ChatLoop.RunAsync(setup.Agent, options, transcript, cancellation.Token);

        // The recall turn runs last, because it can only demonstrate anything once the earlier
        // turns have filled the store it shares.
        if (setup.RecallAgent is not null && options.RecallQuestion is not null)
        {
            await ChatLoop.RunRecallAsync(
                setup.RecallAgent,
                options.RecallQuestion,
                transcript,
                cancellation.Token);
        }
    }

    return exitSuccess;
}
catch (CommandLineException usageError)
{
    // A user's mistake: a clean one-line message and a usage exit code, no stack trace.
    await Console.Error.WriteLineAsync(usageError.Message);
    await Console.Error.WriteLineAsync("Run with --help to see the available options.");
    return exitUsage;
}
catch (OperationCanceledException)
{
    // Ctrl-C: a clean, intentional exit.
    return exitSuccess;
}
catch (Exception unexpected)
{
    // Anything else is a genuine fault worth a full diagnostic for a developer running the sample.
    await Console.Error.WriteLineAsync($"Unexpected error: {unexpected}");
    return exitUnexpected;
}
