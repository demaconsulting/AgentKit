using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.Markdown;

/// <summary>
///     The markdown tool family: the pack an application attaches to give an agent a policy-governed
///     view of a Markdown file's heading structure.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. The outline tool's factory is internal, so a tool cannot be constructed
///     outside the family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <b>The family is deliberately a single tool.</b> It reports where a Markdown file's sections
///     are — their heading levels, titles and line ranges — and stops there, because reading and
///     editing a section by its line range is already what the text family's read and cut tools do.
///     Adding a markdown read or replace tool would duplicate them; the outline is the one thing the
///     text family cannot supply on its own, so it is the one thing this family provides.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so that
///     a test or a composing application can name the family without repeating a string literal that
///     could drift from the names the tools actually carry.
///     </para>
///     <para>
///     <b>The family requires no host capability.</b> Reading a file's heading structure needs
///     nothing of the model or the application beyond what every host already provides, so every host
///     receives the family.
///     </para>
///     <para>
///     The class is stateless and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family. The pack requires no host capability, so it is registered by every
///     composition. It publishes a single tool, <c>markdown_outline</c>, governed by the policy the
///     builder was constructed with — the pack itself grants nothing.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///
///     var policy = new PathPolicy(workspace, [PathRule.ReadOnly(workspace)]);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .Add(new MarkdownPack())
///         .Build();
///
///     // markdown_outline — the name carries the family prefix.
///     var names = tools.Select(tool =&gt; tool.Name).ToList();
///     var prefix = MarkdownPack.FamilyPrefix;
///     </code>
/// </example>
public sealed class MarkdownPack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "markdown";

    /// <summary>
    ///     Initializes a new instance of the <see cref="MarkdownPack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tools comes from the <see cref="ToolPackBuilder"/> the pack is added to, not from
    ///     construction. Declared explicitly rather than left implicit so that the documentation
    ///     the package ships describes every public member.
    /// </remarks>
    public MarkdownPack()
    {
    }

    /// <summary>
    ///     Gets the family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Implemented explicitly so that the constant above can keep the name the contract
    ///     uses. The class is sealed, so the member is not hidden from a derived type.
    /// </remarks>
    string IToolPack.FamilyPrefix => FamilyPrefix;

    /// <summary>
    ///     Gets the capabilities the host must provide for this pack's tools to operate.
    /// </summary>
    /// <remarks>
    ///     <see cref="HostCapabilities.None"/>: reading a Markdown file's structure asks nothing of
    ///     the host, so the family is registered by every composition rather than gated behind a
    ///     declaration an application would have to know to make.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     The family publishes a single tool today; it is still returned as a collection because the
    ///     pack contract is a collection and because a family grows without its callers changing. The
    ///     policy is passed to the tool's factory and captured there, so the tool cannot later observe
    ///     a different policy.
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>The outline tool.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to the tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        return
        [
            MarkdownOutlineTool.Create(policy)
        ];
    }
}
