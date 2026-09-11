using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Core.Tests;

/// <summary>
///     A tool pack whose prefix, required capabilities and tools are supplied by the test.
/// </summary>
/// <remarks>
///     The pack contract is an interface, so the only way to exercise it — and the only way to
///     exercise the builder that consumes it — is through an implementation the test controls.
///     The stub also records whether it was asked to create its tools, which is what makes
///     "a pack the host cannot support is never asked" an observable property rather than an
///     inference from an empty result.
/// </remarks>
internal sealed class StubToolPack : IToolPack
{
    /// <summary>
    ///     The tools this pack returns, or null to return no collection at all.
    /// </summary>
    private readonly IEnumerable<AIFunction>? _tools;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StubToolPack"/> class.
    /// </summary>
    /// <param name="familyPrefix">The family prefix the pack declares.</param>
    /// <param name="requiredCapabilities">The capabilities the pack declares it needs.</param>
    /// <param name="tools">
    ///     The tools the pack returns, or null to return no collection at all.
    /// </param>
    public StubToolPack(
        string familyPrefix,
        HostCapabilities requiredCapabilities = HostCapabilities.None,
        IEnumerable<AIFunction>? tools = null)
    {
        FamilyPrefix = familyPrefix;
        RequiredCapabilities = requiredCapabilities;
        _tools = tools;
    }

    /// <summary>
    ///     Gets the family prefix the pack declares.
    /// </summary>
    public string FamilyPrefix { get; }

    /// <summary>
    ///     Gets the capabilities the pack declares it needs.
    /// </summary>
    public HostCapabilities RequiredCapabilities { get; }

    /// <summary>
    ///     Gets the number of times the pack was asked to create its tools.
    /// </summary>
    public int CreateToolsCallCount { get; private set; }

    /// <summary>
    ///     Gets the access policy the pack was last handed, or null if it was never asked.
    /// </summary>
    public PathPolicy? LastPolicy { get; private set; }

    /// <summary>
    ///     Creates a tool carrying the supplied name, built the only supported way.
    /// </summary>
    /// <param name="name">The tool name.</param>
    /// <returns>The created tool.</returns>
    public static AIFunction Tool(string name) => GuardedToolFactory.Create(
        (Func<object>)(() => ToolResult.Text("ok")),
        name,
        "A stub tool used by the pack composition tests.");

    /// <summary>
    ///     Records the call and returns the tools the test supplied.
    /// </summary>
    /// <param name="policy">The access policy the builder handed over.</param>
    /// <returns>The tools the test supplied.</returns>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        CreateToolsCallCount++;
        LastPolicy = policy;
        return _tools!;
    }
}
