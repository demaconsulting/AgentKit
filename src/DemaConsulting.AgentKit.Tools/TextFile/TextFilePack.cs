using DemaConsulting.AgentKit.Core;
using Microsoft.Extensions.AI;

namespace DemaConsulting.AgentKit.Tools.TextFile;

/// <summary>
///     The text file tool family: the pack an application attaches to give an agent policy-governed
///     searching, reading and editing of the contents of text files.
/// </summary>
/// <remarks>
///     <para>
///     A pack is the unit of attachment, which is why this is the only public way to obtain the
///     family's tools. Each tool's factory is internal, so a tool cannot be constructed outside the
///     family that claims its prefix, and every tool is necessarily built through
///     <see cref="GuardedToolFactory"/> with the policy the composition supplies.
///     </para>
///     <para>
///     <b>This family reads and edits the <em>contents</em> of a text file.</b> It searches across
///     files, reads a ranged, line-numbered window of one, creates a new file, replaces exact text,
///     and cuts, copies and pastes ranges of lines. Managing the files themselves — listing, copying,
///     moving
///     and deleting them regardless of type — belongs to the sibling <c>file</c> family, and reading
///     the section structure of a Markdown file belongs to the <c>markdown</c> family. Keeping those
///     jobs in separate families is what lets each tool's description say one clear thing.
///     </para>
///     <para>
///     <b>Navigation is by line number; editing in place is by content.</b> Search, read, cut and
///     copy address lines by number, while replace addresses text by exact content. The spike observed
///     models mixing the two dialects freely and correctly — searching for a line number, then
///     replacing by matched content — so the family offers both rather than forcing one.
///     </para>
///     <para>
///     <b>The cut, copy and paste tools share one buffer per composition.</b> Every call to
///     <see cref="CreateTools"/> allocates a fresh <see cref="TextFileLineBuffers"/> and gives it to
///     the cut, copy and paste tools, so a range cut or copied in one call can be pasted back in
///     another against the same file set, while two independently composed tool sets never share
///     slots. The buffer lives exactly as long as the tools do.
///     </para>
///     <para>
///     <see cref="FamilyPrefix"/> is published as a constant as well as through the contract, so that
///     a test or a composing application can name the family without repeating a string literal that
///     could drift from the names the tools actually carry.
///     </para>
///     <para>
///     <b>The family requires no host capability.</b> Reading and editing text needs nothing of the
///     model or the application beyond what every host already provides, so every host receives the
///     family.
///     </para>
///     <para>
///     The class is stateless — the per-composition buffer lives on the tools it creates, not on the
///     pack — and therefore safe for concurrent use from any number of threads.
///     </para>
/// </remarks>
/// <example>
///     <para>
///     Attaching the family. The pack requires no host capability, so it is registered by every
///     composition. It publishes seven tools, in this order: <c>text_file_search</c>,
///     <c>text_file_read</c>, <c>text_file_create</c>, <c>text_file_replace</c>,
///     <c>text_file_cut_lines</c>, <c>text_file_copy_lines</c>, and <c>text_file_paste_lines</c>.
///     Every one of them is governed by
///     the policy the builder was constructed with — the pack itself grants nothing.
///     </para>
///     <code>
///     var workspace = Path.GetFullPath("workspace");
///     var session = Path.GetFullPath("session");
///
///     var policy = new PathPolicy(
///         workingDirectory: workspace,
///         grants: [PathRule.ReadOnly(workspace), PathRule.ReadWrite(session)]);
///
///     IReadOnlyList&lt;AIFunction&gt; tools = new ToolPackBuilder(policy)
///         .Add(new TextFilePack())
///         .Build();
///
///     // text_file_search, text_file_read, ... — every name carries the family prefix.
///     var names = tools.Select(tool =&gt; tool.Name).ToList();
///     var prefix = TextFilePack.FamilyPrefix;
///     </code>
/// </example>
public sealed class TextFilePack : IToolPack
{
    /// <summary>
    ///     The family prefix every tool in this pack carries.
    /// </summary>
    /// <remarks>
    ///     Every name the pack publishes begins with this value followed by an underscore, which
    ///     <see cref="ToolPackBuilder.Build"/> verifies rather than trusts.
    /// </remarks>
    public const string FamilyPrefix = "text_file";

    /// <summary>
    ///     Initializes a new instance of the <see cref="TextFilePack"/> class.
    /// </summary>
    /// <remarks>
    ///     The pack carries no state and grants nothing on its own: the policy that governs its
    ///     tools comes from the <see cref="ToolPackBuilder"/> the pack is added to, not from
    ///     construction, and the cut/paste buffer is allocated per <see cref="CreateTools"/> call
    ///     rather than held on the pack. Declared explicitly rather than left implicit so that the
    ///     documentation the package ships describes every public member.
    /// </remarks>
    public TextFilePack()
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
    ///     <see cref="HostCapabilities.None"/>: text file access asks nothing of the host, so the
    ///     family is registered by every composition rather than gated behind a declaration an
    ///     application would have to know to make.
    /// </remarks>
    public HostCapabilities RequiredCapabilities => HostCapabilities.None;

    /// <summary>
    ///     Creates the family's tools, governed by the supplied access policy.
    /// </summary>
    /// <remarks>
    ///     The order — search, read, create, replace, cut, copy, paste — is fixed rather than
    ///     incidental, because the order a model sees the tools in is observable. A fresh
    ///     <see cref="TextFileLineBuffers"/> is allocated here and shared between the cut, copy and
    ///     paste tools, giving the buffer exactly the lifetime of this composition's tools. The policy
    ///     is passed to each tool's factory and captured there, so no tool in the family can observe a
    ///     different policy from its neighbor.
    /// </remarks>
    /// <param name="policy">The access policy every returned tool observes.</param>
    /// <returns>The search, read, create, replace, cut, copy and paste tools, in that order.</returns>
    /// <exception cref="ArgumentNullException">
    ///     Thrown when <paramref name="policy"/> is <see langword="null"/>.
    /// </exception>
    public IEnumerable<AIFunction> CreateTools(PathPolicy policy)
    {
        // Validated here rather than left to each tool's factory so that the failure names the
        // composing application's mistake at the point it was made.
        ArgumentNullException.ThrowIfNull(policy);

        // One buffer per composition, shared by cut and paste: a range cut here can be pasted back
        // here, and two separately composed tool sets never share slots.
        var buffers = new TextFileLineBuffers();

        return
        [
            TextFileSearchTool.Create(policy),
            TextFileReadTool.Create(policy),
            TextFileCreateTool.Create(policy),
            TextFileReplaceTool.Create(policy),
            TextFileCutLinesTool.Create(policy, buffers),
            TextFileCopyLinesTool.Create(policy, buffers),
            TextFilePasteLinesTool.Create(policy, buffers)
        ];
    }
}
