using DemaConsulting.AgentKit.Samples.CustomTools;

// Entry point for the custom-tools sample.
//
// The flow is deliberately linear so a reader can follow it top to bottom: parse the command line,
// resolve and validate the workspace, build the provider agent, then run the chat. Provider choice
// is absorbed entirely inside AgentComposition; nothing here knows or cares which provider is used.

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

    // Resolve the workspace to an absolute path so the anchor is unambiguous and a relative path a
    // model supplies resolves against a real, fixed location. The workspace must already exist: it
    // holds the Markdown files the sample inspects, so a missing one is a mistake.
    var workspaceRoot = Path.GetFullPath(options.Workspace);
    if (!Directory.Exists(workspaceRoot))
    {
        throw new CommandLineException(
            $"The workspace folder '{options.Workspace}' does not exist. " +
            "Pass --workspace <path> pointing at an existing folder.");
    }

    // Ctrl-C ends the run cleanly rather than killing the process: cancel the token and let the
    // chat loop unwind. The second Ctrl-C (if the first is mid-turn) still hard-terminates.
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    // Print the workspace and provider so a reader knows the one granted location the custom and
    // shipped tools operate within, and which runtime built the agent.
    Console.WriteLine(
        $"Workspace: {workspaceRoot} (read-write, relative paths anchor here)\n" +
        $"Provider:  {options.Provider}\n" +
        "Tools:     docstats_wordcount (custom), clock_now (custom), text_file_* (shipped)");

    // Build the agent (the one provider-specific step) and always release its runtime resources.
    var setup = await AgentComposition.CreateAgentAsync(
        options,
        workspaceRoot,
        cancellation.Token);
    await using (setup.Cleanup)
    {
        await ChatLoop.RunAsync(setup.Agent, options, cancellation.Token);
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
