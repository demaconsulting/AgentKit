using DemaConsulting.AgentKit.Samples.DocumentAssistant;

// Entry point for the document-assistant sample.
//
// The flow is deliberately linear so a reader can follow it top to bottom: parse the command line,
// resolve and validate the two locations, build the provider agent, then run the chat. Provider
// choice is absorbed entirely inside AgentComposition; nothing here knows or cares which provider
// is used.

// Exit codes (see ExitCodes): 0 success (including a clean Ctrl-C), 2 for a usage error, 1 for a
// failure — a provider that did not respond in time, or an unexpected fault.

// Ctrl-C ends the run cleanly rather than killing the process: cancel the token and let the work in
// progress unwind. The handler is registered before anything talks to a provider, so listing models
// can be canceled too, and the token stays in scope for the cancellation handler below, which is how
// a Ctrl-C is told apart from a provider timeout. The second Ctrl-C (if the first is mid-turn) still
// hard-terminates.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var options = CommandLineOptions.Parse(args);

    // Help is a terminal, successful action: print and leave without touching a provider.
    if (options.HelpRequested)
    {
        Console.WriteLine(CommandLineOptions.HelpText());
        return ExitCodes.Success;
    }

    // Listing models is the other terminal action: the picker an application would show, filtered
    // to the models that can run this sample. It needs a provider but no workspace.
    if (options.ListModels)
    {
        await AgentComposition.ListModelsAsync(options, Console.Out, cancellation.Token);
        return ExitCodes.Success;
    }

    // Resolve the workspace to an absolute path so the anchor is unambiguous and a relative path a
    // model supplies resolves against a real, fixed location. The workspace must already exist: it
    // holds the user's documents, so a missing one is a mistake rather than something to create.
    var workspaceRoot = Path.GetFullPath(options.Workspace);
    if (!Directory.Exists(workspaceRoot))
    {
        throw new CommandLineException(
            $"The workspace folder '{options.Workspace}' does not exist. " +
            "Pass --workspace <path> pointing at an existing folder.");
    }

    // The session location is the application's own choice — AgentKit has no session convention —
    // so the sample picks one, prints it, and creates it. Unlike the workspace it is created if
    // absent: an application that asks an agent to write somewhere owns that location existing.
    var sessionRoot = Path.GetFullPath(
        options.Session
        ?? Path.Combine(Path.GetTempPath(), CommandLineOptions.DefaultSessionFolderName));
    Directory.CreateDirectory(sessionRoot);


    // Print both locations with their access, because the whole demonstration turns on the reader
    // knowing which location carries which permission, and on seeing that the session path is one
    // the application chose rather than one the library assumed.
    Console.WriteLine(
        $"Workspace: {workspaceRoot} ({(options.WorkspaceReadOnly ? "read-only" : "read-write")}, "
        + "relative paths anchor here)\n" +
        $"Session:   {sessionRoot} (read-write, address it by absolute path)\n" +
        $"Provider:  {options.Provider}\n" +
        $"Model:     {options.Model ?? "(provider default)"}"
        + $"{(options.ReasoningEffort is { } effort ? $", effort {effort}" : "")}"
        + $"{(options.ContextTier is { } tier ? $", tier {tier}" : "")}"
        + $"{(options.ContextWindow is { } window ? $", window {window:N0}" : "")}\n" +
        $"Vision:    {(options.VisionEnabled ? "enabled" : "disabled")}");

    // Build the agent (the one provider-specific step) and always release its runtime resources.
    var setup = await AgentComposition.CreateAgentAsync(
        options,
        workspaceRoot,
        sessionRoot,
        cancellation.Token);
    await using (setup.Cleanup)
    {
        await ChatLoop.RunAsync(setup.Agent, options, cancellation.Token);
    }

    return ExitCodes.Success;
}
catch (CommandLineException usageError)
{
    // A user's mistake: a clean one-line message and a usage exit code, no stack trace.
    await Console.Error.WriteLineAsync(usageError.Message);
    await Console.Error.WriteLineAsync("Run with --help to see the available options.");
    return ExitCodes.Usage;
}
catch (OperationCanceledException canceled)
{
    // Ctrl-C is a clean, intentional exit; a cancellation nobody asked for - a provider that did not
    // respond in time - is reported and fails the run.
    return await ExitCodes.ReportCancellationAsync(canceled, cancellation.Token, Console.Error);
}
catch (Exception unexpected)
{
    // Anything else is a genuine fault worth a full diagnostic for a developer running the sample.
    await Console.Error.WriteLineAsync($"Unexpected error: {unexpected}");
    return ExitCodes.Failure;
}
